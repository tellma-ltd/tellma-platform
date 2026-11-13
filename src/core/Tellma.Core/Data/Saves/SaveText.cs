// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Core.Data.Saves
{
    // The text follows spec 0012 §8.1–§8.5: the guard, the root inserts, each collection's
    // synchronise (nested collections under their parent's, with the descendants of removed rows
    // deleted deepest first), the root updates and the receipt; the capture rule and the blob
    // captures on every write.
    /// <summary>
    ///     Writes a save's text for a plan, its statement-local names under a placeholder ordinal
    ///     (<see cref="Placeholder" />) that the emitter replaces with the statement's own.
    /// </summary>
    internal sealed class SaveText
    {
        /// <summary>The statement-local prefix the text is written with, before the ordinal is known.</summary>
        public const string Placeholder = ReservedSqlNames.StatementPrefix + "#_";

        /// <summary>The marker of a row whose stamp did not match.</summary>
        public const string ConflictReason = "C";

        /// <summary>The marker of a row that no longer exists.</summary>
        public const string MissingReason = "M";

        /// <summary>The collation every compared character column carries, so a case-only edit is a change.</summary>
        private const string Binary = " COLLATE " + DatabaseCollations.Binary;

        /// <summary>The text written so far.</summary>
        private readonly StringBuilder _text = new();

        /// <summary>The plan.</summary>
        private readonly SavePlan _plan;

        /// <summary>The hint every data-modifying statement ends with; empty when not recompiling.</summary>
        private readonly string _option;

        /// <summary>The most ids of each list the conflict message names.</summary>
        private readonly int _conflictCap;

        /// <summary>The capture tables declared so far.</summary>
        private int _captures;

        /// <summary>The removed-rows tables declared so far.</summary>
        private int _gone;

        /// <summary>Creates the writer.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="recompile">Whether every data-modifying statement carries <c>OPTION (RECOMPILE)</c>.</param>
        /// <param name="conflictCap">The most ids of each list the conflict message names.</param>
        private SaveText(SavePlan plan, bool recompile, int conflictCap)
        {
            _plan = plan;
            _option = recompile ? "\nOPTION (RECOMPILE)" : string.Empty;
            _conflictCap = conflictCap;
        }

        /// <summary>The statement-local words a save declares, beside its parameters, for later statements to name.</summary>
        /// <param name="plan">The plan.</param>
        /// <returns>The words.</returns>
        public static IReadOnlyList<string> DeclaredWords(SavePlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            return
            [
                ReservedSqlNames.StatementNames.Now,
                ReservedSqlNames.StatementNames.New,
                ReservedSqlNames.StatementNames.Saved,
                ReservedSqlNames.StatementNames.Touched,
                ReservedSqlNames.StatementNames.Conflicts,
                .. plan.Captures.Select(static capture => capture.Word),
            ];
        }

        /// <summary>Writes the text of a plan.</summary>
        /// <param name="plan">The plan.</param>
        /// <param name="recompile">Whether every data-modifying statement carries <c>OPTION (RECOMPILE)</c>.</param>
        /// <param name="conflictCap">The most ids of each list the conflict message names.</param>
        /// <returns>The text, its statement-local names under <see cref="Placeholder" />.</returns>
        public static string Write(SavePlan plan, bool recompile, int conflictCap)
        {
            ArgumentNullException.ThrowIfNull(plan);
            SaveText writer = new(plan, recompile, conflictCap);
            writer.WriteAll();
            return writer._text.ToString();
        }

        /// <summary>A statement-local name under the placeholder ordinal.</summary>
        /// <param name="word">The word.</param>
        /// <returns>The name.</returns>
        private static string Local(string word)
        {
            return Placeholder + word;
        }

        /// <summary>A table-valued parameter's name under the placeholder ordinal.</summary>
        /// <param name="index">The parameter's index.</param>
        /// <returns>The name.</returns>
        private static string Tvp(int index)
        {
            return Local(ReservedSqlNames.StatementNames.TableValued(index));
        }

        /// <summary>A scalar parameter's name under the placeholder ordinal.</summary>
        /// <param name="index">The parameter's index.</param>
        /// <returns>The name.</returns>
        private static string Scalar(int index)
        {
            return Local(ReservedSqlNames.StatementNames.Scalar(index));
        }

        /// <summary>A column, bracket-delimited.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The delimited column.</returns>
        private static string Col(string column)
        {
            return $"[{column.Replace("]", "]]", StringComparison.Ordinal)}]";
        }

        /// <summary>The compared columns of a row, qualified by an alias, character columns binary-collated.</summary>
        /// <param name="alias">The alias.</param>
        /// <param name="columns">The columns.</param>
        /// <returns>The select list.</returns>
        private static string Compared(string alias, IEnumerable<SaveColumn> columns)
        {
            return string.Join(", ", columns.Select(column => $"{alias}.{Col(column.Name)}{(column.IsText ? Binary : string.Empty)}"));
        }

        /// <summary>Writes every part, in order.</summary>
        private void WriteAll()
        {
            SaveNode root = _plan.Root;
            string key = root.KeyType;

            // --- The batch-local sets of fixed meaning, and every blob capture table ---
            Line($"DECLARE {Local(ReservedSqlNames.StatementNames.Now)} datetimeoffset(7) = SYSUTCDATETIME();");
            Line($"DECLARE {Local(ReservedSqlNames.StatementNames.New)} TABLE ([Id] {key} NOT NULL PRIMARY KEY);");
            Line($"DECLARE {Local(ReservedSqlNames.StatementNames.Saved)} TABLE ([Id] {key} NOT NULL PRIMARY KEY);");
            Line($"DECLARE {Local(ReservedSqlNames.StatementNames.Touched)} TABLE ([Id] {key} NOT NULL, [Collection] tinyint NOT NULL, [Op] char(1) NOT NULL);");
            Line($"DECLARE {Local(ReservedSqlNames.StatementNames.Conflicts)} TABLE ([Id] {key} PRIMARY KEY, [Reason] char(1) NOT NULL);");
            foreach (SaveCapture capture in _plan.Captures)
            {
                Line($"DECLARE {Local(capture.Word)} TABLE ([RowId] {capture.Node.KeyType} NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);");
            }

            // The saved set is filled before any write: every root of the payload, new and existing.
            Statement($"INSERT INTO {Local(ReservedSqlNames.StatementNames.Saved)} ([Id]) SELECT [Id] FROM {Tvp(root.NewTable)} UNION SELECT [Id] FROM {Tvp(root.ExistingTable)}");

            WriteGuard(root);
            WriteRootInsert(root);
            foreach (SaveNode child in root.Children)
            {
                WriteSynchronise(child);
            }

            WriteRootUpdate(root);

            // --- The receipt ---
            string touched = Local(ReservedSqlNames.StatementNames.Touched);
            Line(
                $"SELECT {Local(ReservedSqlNames.StatementNames.Now)} AS [Stamp], (SELECT COUNT(*) FROM {touched} WHERE [Op] = 'U') AS [ChildUpdated], (SELECT COUNT(*) FROM {touched} WHERE [Op] = 'D') AS [Deleted];");
        }

        /// <summary>
        ///     Writes the concurrency guard: U locks on every existing root, held to the commit; a
        ///     missing row conflicts even under override. The conflict rows are selected before the
        ///     <c>THROW</c>, whose message names a capped list of each kind.
        /// </summary>
        /// <param name="root">The root's node.</param>
        private void WriteGuard(SaveNode root)
        {
            string conflicts = Local(ReservedSqlNames.StatementNames.Conflicts);
            string message = Local(ReservedSqlNames.StatementNames.Message);
            Line($"INSERT INTO {conflicts} ([Id], [Reason])");
            Line($"SELECT s.[Id], CASE WHEN t.[Id] IS NULL THEN '{MissingReason}' ELSE '{ConflictReason}' END");
            Line($"FROM {Tvp(root.ExistingTable)} AS s");
            Line($"LEFT JOIN {root.Table} AS t WITH (UPDLOCK, ROWLOCK) ON t.[Id] = s.[Id]");
            Statement($"WHERE t.[Id] IS NULL OR ({Scalar(0)} = 0 AND t.[ModifiedAt] <> s.[ModifiedAt])");
            Line($"IF EXISTS (SELECT 1 FROM {conflicts})");
            Line("BEGIN");
            Line("    SELECT c.[Id], c.[Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]");
            Line($"    FROM {conflicts} AS c");
            Line($"    LEFT JOIN {root.Table} AS t ON t.[Id] = c.[Id]");
            Line("    LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];");
            Line($"    DECLARE {message} nvarchar(2048) = N'{{\"count\":' + CAST((SELECT COUNT(*) FROM {conflicts}) AS nvarchar(10))");
            Line($"        + N',\"conflicts\":[' + {IdList(conflicts, ConflictReason, "c")}");
            Line($"        + N'],\"missing\":[' + {IdList(conflicts, MissingReason, "m")}");
            Line("        + N']}';");
            Line($"    THROW {TellmaSqlErrors.Concurrency.ToString(CultureInfo.InvariantCulture)}, {message}, 1;");
            Line("END;");
        }

        /// <summary>The capped, ordered, comma-separated ids of one kind of conflict, or empty.</summary>
        /// <param name="conflicts">The conflicts table.</param>
        /// <param name="reason">The kind.</param>
        /// <param name="alias">The derived table's alias.</param>
        /// <returns>The expression.</returns>
        private string IdList(string conflicts, string reason, string alias)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP ({_conflictCap}) [Id] FROM {conflicts} WHERE [Reason] = '{reason}' ORDER BY [Id]) AS {alias}), N'')");
        }

        /// <summary>
        ///     Writes the insert of the new roots: audit columns stamped by the statement, every other
        ///     column from the row as the pipeline left it, and a tree's provisional node.
        /// </summary>
        /// <param name="root">The root's node.</param>
        private void WriteRootInsert(SaveNode root)
        {
            string now = Local(ReservedSqlNames.StatementNames.Now);
            string user = Scalar(1);
            List<string> columns = [.. root.Columns.Select(static column => Col(column.Name))];
            List<string> values = [.. root.Columns.Select(column => column.IsAudit ? AuditValue(column.Name, now, user) : $"s.{Col(column.Name)}")];

            // The tree statements that follow replace the provisional node with the row's path.
            if (root.Entity.Tree is TreeMetadata tree)
            {
                columns.Add(Col(tree.NodeColumn));
                values.Add("hierarchyid::Parse('/0/' + CAST(s.[Id] AS varchar(20)) + '/')");
            }

            Output output = PlanOutput(root, op: 'I', id: "inserted.[Id]", parent: null, toNew: true, touched: false, blobs: root.BlobColumns.Select(column => ("NULL", $"inserted.{Col(column)}")));
            Line($"INSERT INTO {root.Table} ({string.Join(", ", columns)})");
            Line(output.Clause);
            Line($"SELECT {string.Join(", ", values)}");
            Statement($"FROM {Tvp(root.NewTable)} AS s");
            output.WriteAfter(this);
        }

        /// <summary>The statement's value for an audit column: the batch's stamp or the current user.</summary>
        /// <param name="column">The column.</param>
        /// <param name="now">The stamp variable.</param>
        /// <param name="user">The user parameter.</param>
        /// <returns>The value.</returns>
        private static string AuditValue(string column, string now, string user)
        {
            return column.EndsWith("ById", StringComparison.Ordinal) ? user : now;
        }

        /// <summary>
        ///     Writes the synchronise of one collection: the delete of the rows the payload no longer
        ///     holds under the supplied parents (their descendants first), the update of the changed
        ///     rows, the insert of the new rows, then each nested collection under it.
        /// </summary>
        /// <param name="node">The collection's node.</param>
        private void WriteSynchronise(SaveNode node)
        {
            string parents = Tvp(node.ParentsTable);
            string newRows = Tvp(node.NewTable);
            string existing = Tvp(node.ExistingTable);
            string parentKey = Col(node.ParentKey!);
            string absent = $"  AND NOT EXISTS (SELECT 1 FROM {newRows} AS s WHERE s.[Id] = c.[Id])\n  AND NOT EXISTS (SELECT 1 FROM {existing} AS s WHERE s.[Id] = c.[Id])";

            // --- The delete: through a removed-rows table when the rows own collections, whose rows go first ---
            Output deleted = PlanOutput(node, op: 'D', id: "deleted.[Id]", parent: $"deleted.{parentKey}", toNew: false, touched: true, blobs: node.BlobColumns.Select(column => ($"deleted.{Col(column)}", "NULL")));
            if (node.Children.Count > 0)
            {
                string gone = Local(string.Create(CultureInfo.InvariantCulture, $"gone{_gone++}"));
                Line($"DECLARE {gone} TABLE ([Id] {node.KeyType} PRIMARY KEY);");
                Line($"INSERT INTO {gone} ([Id])");
                Line($"SELECT c.[Id] FROM {node.Table} AS c");
                Line($"WHERE c.{parentKey} IN (SELECT [Id] FROM {parents})");
                Statement(absent);
                foreach (SaveNode child in node.Children)
                {
                    WriteDescendantDelete(child, $"SELECT [Id] FROM {gone}");
                }

                Line($"DELETE c{deleted.Prefixed}");
                Statement($"FROM {node.Table} AS c WHERE c.[Id] IN (SELECT [Id] FROM {gone})");
            }
            else
            {
                Line($"DELETE c{deleted.Prefixed}");
                Line($"FROM {node.Table} AS c");
                Line($"WHERE c.{parentKey} IN (SELECT [Id] FROM {parents})");
                Statement(absent);
            }

            deleted.WriteAfter(this);

            // --- The update: changed rows of the supplied parents only, never re-parented ---
            List<SaveColumn> set = [.. node.Columns.Where(static column => column.IsSet)];
            if (set.Count > 0)
            {
                Output updated = PlanOutput(node, op: 'U', id: "inserted.[Id]", parent: $"inserted.{parentKey}", toNew: false, touched: true, blobs: node.BlobColumns.Select(column => ($"deleted.{Col(column)}", $"inserted.{Col(column)}")));
                Line($"UPDATE c SET {string.Join(", ", set.Select(static column => $"c.{Col(column.Name)} = s.{Col(column.Name)}"))}");
                Line(updated.Clause);
                Line($"FROM {node.Table} AS c JOIN {existing} AS s ON s.[Id] = c.[Id]");
                Line($"WHERE c.{parentKey} IN (SELECT [Id] FROM {parents}) AND c.{parentKey} = s.{parentKey}");
                Statement($"  AND EXISTS (SELECT {Compared("s", set)}\n              EXCEPT SELECT {Compared("c", set)})");
                updated.WriteAfter(this);
            }

            // --- The insert ---
            Output inserted = PlanOutput(node, op: 'I', id: "inserted.[Id]", parent: $"inserted.{parentKey}", toNew: false, touched: true, blobs: node.BlobColumns.Select(column => ("NULL", $"inserted.{Col(column)}")));
            Line($"INSERT INTO {node.Table} ({string.Join(", ", node.Columns.Select(static column => Col(column.Name)))})");
            Line(inserted.Clause);
            Statement($"SELECT {string.Join(", ", node.Columns.Select(static column => $"s.{Col(column.Name)}"))} FROM {newRows} AS s");
            inserted.WriteAfter(this);

            // --- The nested collections, under the rows just written ---
            foreach (SaveNode child in node.Children)
            {
                WriteSynchronise(child);
            }
        }

        /// <summary>
        ///     Writes the delete of a descendant table's rows under removed rows, its own descendants
        ///     first, with the blob captures its table declares. These deletes touch nothing.
        /// </summary>
        /// <param name="node">The descendant's node.</param>
        /// <param name="owners">A query of the ids of the owning rows being removed.</param>
        private void WriteDescendantDelete(SaveNode node, string owners)
        {
            string parentKey = Col(node.ParentKey!);
            string scope = $"SELECT x.[Id] FROM {node.Table} AS x WHERE x.{parentKey} IN ({owners})";
            foreach (SaveNode child in node.Children)
            {
                WriteDescendantDelete(child, scope);
            }

            Output deleted = PlanOutput(node, op: 'D', id: "deleted.[Id]", parent: $"deleted.{parentKey}", toNew: false, touched: false, blobs: node.BlobColumns.Select(column => ($"deleted.{Col(column)}", "NULL")));
            Line($"DELETE g{deleted.Prefixed}");
            Statement($"FROM {node.Table} AS g WHERE g.{parentKey} IN ({owners})");
            deleted.WriteAfter(this);
        }

        /// <summary>
        ///     Writes the update of the existing roots, last: editable and derived columns, on rows that
        ///     changed or whose children did, stamped; the stamped ids are a result set.
        /// </summary>
        /// <param name="root">The root's node.</param>
        private void WriteRootUpdate(SaveNode root)
        {
            List<SaveColumn> set = [.. root.Columns.Where(static column => column.IsSet)];
            string now = Local(ReservedSqlNames.StatementNames.Now);
            string touched = Local(ReservedSqlNames.StatementNames.Touched);
            IEnumerable<string> assignments = set
                .Select(static column => $"t.{Col(column.Name)} = s.{Col(column.Name)}")
                .Append($"t.[ModifiedAt] = {now}")
                .Append($"t.[ModifiedById] = {Scalar(1)}");

            Output updated = PlanOutput(root, op: 'U', id: "inserted.[Id]", parent: null, toNew: false, touched: false, blobs: root.BlobColumns.Select(column => ($"deleted.{Col(column)}", $"inserted.{Col(column)}")));
            Line($"UPDATE t SET {string.Join(", ", assignments)}");
            if (updated.Clause.Length > 0)
            {
                Line(updated.Clause);
            }

            Line("OUTPUT inserted.[Id]");
            Line($"FROM {root.Table} AS t JOIN {Tvp(root.ExistingTable)} AS s ON s.[Id] = t.[Id]");
            Statement(set.Count == 0
                ? $"WHERE t.[Id] IN (SELECT [Id] FROM {touched})"
                : $"WHERE EXISTS (SELECT {Compared("s", set)}\n              EXCEPT SELECT {Compared("t", set)})\n   OR t.[Id] IN (SELECT [Id] FROM {touched})");
            updated.WriteAfter(this);
        }

        /// <summary>
        ///     Plans a data-modifying statement's <c>OUTPUT … INTO</c>: straight into the one set it
        ///     feeds, or into a capture table that fills each set after it.
        /// </summary>
        /// <param name="node">The written table's node.</param>
        /// <param name="op">The operation: <c>I</c>, <c>U</c> or <c>D</c>.</param>
        /// <param name="id">The written row's id.</param>
        /// <param name="parent">The written row's parent key; null for a root.</param>
        /// <param name="toNew">Whether the statement feeds the new-roots set.</param>
        /// <param name="touched">Whether the statement feeds the touched set.</param>
        /// <param name="blobs">The old and new value of each blob column.</param>
        /// <returns>The output.</returns>
        private Output PlanOutput(SaveNode node, char op, string id, string? parent, bool toNew, bool touched, IEnumerable<(string Old, string New)> blobs)
        {
            List<(string Old, string New)> values = [.. blobs];
            int targets = (toNew ? 1 : 0) + (touched ? 1 : 0) + values.Count;

            // A child under a child reaches the touched set only through its ancestors' rows.
            bool mapped = touched && node.Level > 1;
            if (targets == 0)
            {
                return new Output(string.Empty, []);
            }

            // --- One set, fed directly ---
            if (targets == 1 && !mapped)
            {
                string clause = toNew
                    ? $"OUTPUT {id} INTO {Local(ReservedSqlNames.StatementNames.New)} ([Id])"
                    : touched
                        ? string.Create(CultureInfo.InvariantCulture, $"OUTPUT {parent}, {node.RootCollection}, '{op}' INTO {Local(ReservedSqlNames.StatementNames.Touched)} ([Id], [Collection], [Op])")
                        : $"OUTPUT {id}, {values[0].Old}, {values[0].New} INTO {Local(_plan.Captures.First(capture => capture.Node == node && capture.Column == node.BlobColumns[0]).Word)} ([RowId], [OldBlobId], [NewBlobId])";
                return new Output(clause, []);
            }

            // --- Several sets: a capture table, each set filled from it after the statement ---
            string cap = Local(ReservedSqlNames.StatementNames.Capture(_captures++));
            string parentType = node.Parent is SaveNode parentNode ? parentNode.KeyType : node.KeyType;
            List<string> capColumns = ["[Id]", "[ParentId]", "[Op]"];
            List<string> capValues = [id, parent ?? "NULL", $"'{op}'"];
            StringBuilder declaration = new($"DECLARE {cap} TABLE ([Id] {node.KeyType} NOT NULL, [ParentId] {parentType} NULL, [Op] char(1) NOT NULL");
            for (int index = 0; index < values.Count; index++)
            {
                string column = node.BlobColumns[index];
                _ = declaration.Append(CultureInfo.InvariantCulture, $", {Col("Old_" + column)} int NULL, {Col("New_" + column)} int NULL");
                capColumns.Add(Col("Old_" + column));
                capColumns.Add(Col("New_" + column));
                capValues.Add(values[index].Old);
                capValues.Add(values[index].New);
            }

            Line(declaration.Append(");").ToString());

            List<string> after = [];
            if (toNew)
            {
                after.Add($"INSERT INTO {Local(ReservedSqlNames.StatementNames.New)} ([Id]) SELECT [Id] FROM {cap}");
            }

            if (touched)
            {
                after.Add(mapped ? MappedTouched(node, cap) : string.Create(
                    CultureInfo.InvariantCulture,
                    $"INSERT INTO {Local(ReservedSqlNames.StatementNames.Touched)} ([Id], [Collection], [Op]) SELECT [ParentId], {node.RootCollection}, [Op] FROM {cap}"));
            }

            foreach (string column in node.BlobColumns)
            {
                string word = _plan.Captures.First(capture => capture.Node == node && capture.Column == column).Word;
                after.Add($"INSERT INTO {Local(word)} ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], {Col("Old_" + column)}, {Col("New_" + column)} FROM {cap}");
            }

            return new Output($"OUTPUT {string.Join(", ", capValues)} INTO {cap} ({string.Join(", ", capColumns)})", after);
        }

        /// <summary>
        ///     The fill of the touched set from a nested collection's capture table: each written row's
        ///     parent is mapped to its root through its ancestors' rows in the payload.
        /// </summary>
        /// <param name="node">The nested collection's node, two or more levels down.</param>
        /// <param name="cap">The capture table.</param>
        /// <returns>The statement, without its terminator.</returns>
        private static string MappedTouched(SaveNode node, string cap)
        {
            // The joins climb from the written row's parent to the child of the root.
            StringBuilder joins = new();
            string previous = "x.[ParentId]";
            SaveNode ancestor = node.Parent!;
            string alias;
            while (true)
            {
                alias = string.Create(CultureInfo.InvariantCulture, $"a{ancestor.Level}");
                string key = Col(ancestor.ParentKey!);
                _ = joins.Append(CultureInfo.InvariantCulture, $"\nJOIN (SELECT [Id], {key} FROM {Tvp(ancestor.NewTable)} UNION ALL SELECT [Id], {key} FROM {Tvp(ancestor.ExistingTable)}) AS {alias} ON {alias}.[Id] = {previous}");
                if (ancestor.Level == 1)
                {
                    break;
                }

                previous = $"{alias}.{key}";
                ancestor = ancestor.Parent!;
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"INSERT INTO {Local(ReservedSqlNames.StatementNames.Touched)} ([Id], [Collection], [Op])\nSELECT {alias}.{Col(ancestor.ParentKey!)}, {node.RootCollection}, x.[Op] FROM {cap} AS x{joins}");
        }

        /// <summary>Writes a line.</summary>
        /// <param name="line">The line.</param>
        private void Line(string line)
        {
            _ = _text.Append(line).Append('\n');
        }

        /// <summary>Writes the last line of a data-modifying statement, with the hint and the terminator.</summary>
        /// <param name="line">The line.</param>
        private void Statement(string line)
        {
            _ = _text.Append(line).Append(_option).Append(";\n");
        }

        /// <summary>A statement's <c>OUTPUT … INTO</c> clause and the fills that follow it.</summary>
        /// <param name="Clause">The clause; empty when the statement feeds nothing.</param>
        /// <param name="After">The fills of each set from the capture table, without terminators.</param>
        private sealed record Output(string Clause, IReadOnlyList<string> After)
        {
            /// <summary>The clause after a statement's target alias: a newline and the clause, or nothing.</summary>
            public string Prefixed => Clause.Length == 0 ? string.Empty : "\n" + Clause;

            /// <summary>Writes the fills.</summary>
            /// <param name="writer">The writer.</param>
            public void WriteAfter(SaveText writer)
            {
                foreach (string fill in After)
                {
                    writer.Statement(fill);
                }
            }
        }
    }
}
