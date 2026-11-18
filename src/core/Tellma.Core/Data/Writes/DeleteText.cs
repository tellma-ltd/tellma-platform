// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Trees;

namespace Tellma.Core.Data.Writes
{
    // The delete of spec 0012 §9.2 from its captured keys on: the stamp comparison, a tree's old
    // nodes, the children deepest first, the root with its ids read back, the blob captures of §8.5
    // under the capture rule of §8.4, the user-id captures the bump of spec 0013 §2.5 reads the
    // affected users from, and a tree's recount (§10.3).
    /// <summary>
    ///     Writes the part of a delete statement that follows the key capture and the checks: it
    ///     names the keys table, which an earlier statement of the delete may have declared.
    /// </summary>
    internal sealed class DeleteText
    {
        /// <summary>The text written so far.</summary>
        private readonly StringBuilder _text;

        /// <summary>The plan.</summary>
        private readonly DeletePlan _plan;

        /// <summary>The statement-local prefix, <c>@tb{b}_</c>.</summary>
        private readonly string _prefix;

        /// <summary>The keys table.</summary>
        private readonly string _keys;

        /// <summary>The hint every data-modifying statement ends with; empty when not recompiling.</summary>
        private readonly string _option;

        /// <summary>The capture tables declared so far.</summary>
        private int _captures;

        /// <summary>Creates the writer.</summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="plan">The plan.</param>
        /// <param name="prefix">The statement's local prefix.</param>
        /// <param name="keys">The keys table.</param>
        /// <param name="recompile">Whether every data-modifying statement carries <c>OPTION (RECOMPILE)</c>.</param>
        public DeleteText(StringBuilder text, DeletePlan plan, string prefix, string keys, bool recompile)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(plan);
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            ArgumentException.ThrowIfNullOrEmpty(keys);
            _text = text;
            _plan = plan;
            _prefix = prefix;
            _keys = keys;
            _option = recompile ? "\nOPTION (RECOMPILE)" : string.Empty;
            Recompile = recompile;
        }

        /// <summary>Whether every data-modifying statement carries <c>OPTION (RECOMPILE)</c>.</summary>
        public bool Recompile { get; }

        /// <summary>Writes the declarations of every blob capture table and every user-id capture, first in the statement.</summary>
        public void WriteCaptureDeclarations()
        {
            foreach (DeleteCapture capture in _plan.Captures)
            {
                Line($"DECLARE {Local(capture.Word)} TABLE ([RowId] {capture.Node.KeyType} NOT NULL, [OldBlobId] int NULL, [NewBlobId] int NULL);");
            }

            foreach (DeleteNode node in _plan.UserIdCaptures)
            {
                Line(UserIdCapture.Declaration(Local(node.UserIdWord), node.KeyType, node.UserIdColumns));
            }
        }

        /// <summary>
        ///     Writes the stamp comparison of a delete by ids: the rows whose stored stamp differs
        ///     from the one they were read with are selected, then <c>THROW 50409</c> with the
        ///     message of the save's guard. Every row exists by now, so each is a conflict.
        /// </summary>
        /// <param name="stamps">The table of ids and stamps.</param>
        /// <param name="conflictCap">The most ids the message names.</param>
        public void WriteStampCheck(string stamps, int conflictCap)
        {
            ArgumentException.ThrowIfNullOrEmpty(stamps);
            string conflicts = Local(ReservedSqlNames.StatementNames.Conflicts);
            string message = Local(ReservedSqlNames.StatementNames.Message);
            string table = _plan.Root.Table;
            Line($"DECLARE {conflicts} TABLE ([Id] {_plan.Root.KeyType} PRIMARY KEY);");
            Statement($"INSERT INTO {conflicts} ([Id]) SELECT t.[Id] FROM {table} AS t JOIN {stamps} AS s ON s.[Id] = t.[Id] WHERE t.[ModifiedAt] <> s.[ModifiedAt]");
            Line($"IF EXISTS (SELECT 1 FROM {conflicts})");
            Line("BEGIN");
            Line("    SELECT c.[Id], 'C' AS [Reason], t.[ModifiedAt], t.[ModifiedById], u.[Name], u.[Name2], u.[Name3]");
            Line($"    FROM {conflicts} AS c JOIN {table} AS t ON t.[Id] = c.[Id] LEFT JOIN [core].[Users] AS u ON u.[Id] = t.[ModifiedById];");
            Line($"    DECLARE {message} nvarchar(2048) = N'{{\"count\":' + CAST((SELECT COUNT(*) FROM {conflicts}) AS nvarchar(10))");
            Line(string.Create(
                CultureInfo.InvariantCulture,
                $"        + N',\"conflicts\":[' + ISNULL((SELECT STRING_AGG(CAST([Id] AS nvarchar(20)), ',') WITHIN GROUP (ORDER BY [Id]) FROM (SELECT TOP ({conflictCap}) [Id] FROM {conflicts} ORDER BY [Id]) AS c), N'')"));
            Line("        + N'],\"missing\":[]}';");
            Line($"    THROW {TellmaSqlErrors.Concurrency.ToString(CultureInfo.InvariantCulture)}, {message}, 1;");
            Line("END;");
        }

        /// <summary>
        ///     Writes the deletes: a tree's old nodes first, then each collection's rows deepest
        ///     first, then the root's rows with their ids read back, then a tree's recount.
        /// </summary>
        public void WriteDeletes()
        {
            DeleteNode root = _plan.Root;
            TreeText? tree = root.Entity.Tree is null ? null : new TreeText(_text, TreeShape.Of(root.Entity), _prefix, Recompile);

            // --- A tree's old nodes, before the rows leave: their ancestors lose descendants ---
            tree?.CaptureOld(_keys);

            // --- The collections, deepest first, never by cascade ---
            string owners = $"SELECT [Id] FROM {_keys}";
            foreach (DeleteNode child in root.Children)
            {
                WriteDescendantDelete(child, owners);
            }

            // --- The root: the deleted ids are the statement's result set ---
            Output output = PlanOutput(root);
            Line($"DELETE t{output.Prefixed}");
            Line("OUTPUT deleted.[Id]");
            Statement($"FROM {root.Table} AS t WHERE t.[Id] IN ({owners})");
            output.WriteAfter(this);

            // --- A tree's recount over the old nodes' ancestors ---
            tree?.Recount(affected: null);
        }

        /// <summary>Writes the delete of a collection's rows under the owning rows, its own collections first.</summary>
        /// <param name="node">The collection's node.</param>
        /// <param name="owners">A query of the owning rows' ids.</param>
        private void WriteDescendantDelete(DeleteNode node, string owners)
        {
            string parentKey = SqlNames.Quote(node.ParentKey!);
            string scope = $"SELECT x.[Id] FROM {node.Table} AS x WHERE x.{parentKey} IN ({owners})";
            foreach (DeleteNode child in node.Children)
            {
                WriteDescendantDelete(child, scope);
            }

            Output output = PlanOutput(node);
            Line($"DELETE c{output.Prefixed}");
            Statement($"FROM {node.Table} AS c WHERE c.{parentKey} IN ({owners})");
            output.WriteAfter(this);
        }

        /// <summary>
        ///     Plans a delete's <c>OUTPUT … INTO</c> of its blob columns and its users: straight into
        ///     the one capture table, or into a capture table of its own that fills each capture after it.
        /// </summary>
        /// <param name="node">The node.</param>
        /// <returns>The output.</returns>
        private Output PlanOutput(DeleteNode node)
        {
            IReadOnlyList<string> columns = node.BlobColumns;
            IReadOnlyList<string> users = node.UserIdColumns;
            int targets = columns.Count + (users.Count > 0 ? 1 : 0);
            if (targets == 0)
            {
                return new Output(string.Empty, []);
            }

            // --- One set, fed directly ---
            if (targets == 1)
            {
                return users.Count > 0
                    ? new Output($"OUTPUT {UserIdCapture.Values("deleted.[Id]", "deleted", null, users)} INTO {Local(node.UserIdWord)} ({UserIdCapture.Targets(users)})", [])
                    : new Output($"OUTPUT deleted.[Id], deleted.{SqlNames.Quote(columns[0])}, NULL INTO {Local(Word(node, columns[0]))} ([RowId], [OldBlobId], [NewBlobId])", []);
            }

            // --- Several sets: a capture table, each set filled from it after the statement ---
            // The user-id images under names of their own, so a blob column of the same name never clashes.
            string cap = Local(ReservedSqlNames.StatementNames.Capture(_captures++));
            List<string> names = [.. columns.Select(static column => "Old_" + column), .. users.Select(static column => "UserOld_" + column)];
            Line($"DECLARE {cap} TABLE ([Id] {node.KeyType} NOT NULL{string.Concat(names.Select(static name => $", {SqlNames.Quote(name)} int NULL"))});");
            List<string> after = [.. columns.Select(column =>
                $"INSERT INTO {Local(Word(node, column))} ([RowId], [OldBlobId], [NewBlobId]) SELECT [Id], {SqlNames.Quote("Old_" + column)}, NULL FROM {cap}")];
            if (users.Count > 0)
            {
                string images = string.Join(", ", users.Select(static column => $"{SqlNames.Quote("UserOld_" + column)}, NULL"));
                after.Add($"INSERT INTO {Local(node.UserIdWord)} ({UserIdCapture.Targets(users)}) SELECT [Id], {images} FROM {cap}");
            }

            string values = string.Join(", ", columns.Concat(users).Select(static column => $"deleted.{SqlNames.Quote(column)}"));
            string targetColumns = string.Join(", ", names.Select(SqlNames.Quote));
            return new Output($"OUTPUT deleted.[Id], {values} INTO {cap} ([Id], {targetColumns})", after);
        }

        /// <summary>The capture word of a node's blob column.</summary>
        /// <param name="node">The node.</param>
        /// <param name="column">The column.</param>
        /// <returns>The word.</returns>
        private string Word(DeleteNode node, string column)
        {
            return _plan.Captures.First(capture => capture.Node == node && capture.Column == column).Word;
        }

        /// <summary>A batch-local name under the statement's prefix.</summary>
        /// <param name="word">The word.</param>
        /// <returns>The name.</returns>
        private string Local(string word)
        {
            return _prefix + word;
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

        /// <summary>A delete's <c>OUTPUT … INTO</c> clause and the fills that follow it.</summary>
        /// <param name="Clause">The clause; empty when the delete captures nothing.</param>
        /// <param name="After">The fills of each capture from the capture table, without terminators.</param>
        private sealed record Output(string Clause, IReadOnlyList<string> After)
        {
            /// <summary>The clause after a statement's target alias: a newline and the clause, or nothing.</summary>
            public string Prefixed => Clause.Length == 0 ? string.Empty : "\n" + Clause;

            /// <summary>Writes the fills.</summary>
            /// <param name="writer">The writer.</param>
            public void WriteAfter(DeleteText writer)
            {
                foreach (string fill in After)
                {
                    writer.Statement(fill);
                }
            }
        }
    }
}
