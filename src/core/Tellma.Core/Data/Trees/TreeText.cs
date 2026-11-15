// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Data.Trees
{
    // The tree statements of spec 0012 §10.2–§10.4: T1 the affected set, T2 the top-down path walk,
    // T3 the cycle fence, T3b the old nodes, T4 the re-path, T5a the recount scope and T5b the
    // recount; and the whole-table recount of the verify job.
    /// <summary>
    ///     Writes the tree statements into a save, an update or a delete, under the statement's
    ///     batch-local prefix (<c>@tb{b}_</c>, or a placeholder the save emitter replaces), and the
    ///     whole-table recount the verify job runs.
    /// </summary>
    /// <remarks>
    ///     Both recursive walks carry <c>MAXRECURSION</c> one below the tree's depth limit: a walk
    ///     takes one recursion step per level below its first row, so a row deeper than the limit
    ///     is exactly what exhausts it (error 530). The scope walk (T5a) climbs from every affected
    ///     node to its root, so it bounds the absolute depth, not only the depth below a moved row.
    /// </remarks>
    internal sealed class TreeText
    {
        /// <summary>The text written so far.</summary>
        private readonly StringBuilder _text;

        /// <summary>The tree's table and columns.</summary>
        private readonly TreeShape _tree;

        /// <summary>The statement-local prefix, <c>@tb{b}_</c>.</summary>
        private readonly string _prefix;

        /// <summary>Whether every data-modifying statement carries <c>RECOMPILE</c>.</summary>
        private readonly bool _recompile;

        /// <summary>Creates the writer.</summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="tree">The tree's table and columns.</param>
        /// <param name="prefix">The statement-local prefix, <c>@tb{b}_</c> or a placeholder of that shape.</param>
        /// <param name="recompile">Whether every data-modifying statement carries <c>RECOMPILE</c>.</param>
        public TreeText(StringBuilder text, TreeShape tree, string prefix, bool recompile)
        {
            ArgumentNullException.ThrowIfNull(text);
            ArgumentNullException.ThrowIfNull(tree);
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            if (prefix[0] != '@')
            {
                throw new ArgumentException($"The statement-local prefix '{prefix}' names variables, so it starts with '@'.", nameof(prefix));
            }

            _text = text;
            _tree = tree;
            _prefix = prefix;
            _recompile = recompile;
        }

        /// <summary>
        ///     The whole-table recount (T5b over every row): repairs every row whose counts drifted,
        ///     then selects how many it repaired.
        /// </summary>
        /// <param name="tree">The tree's table and columns.</param>
        /// <returns>The text; its one result set is the <c>[Repaired]</c> count.</returns>
        public static string Verify(TreeShape tree)
        {
            ArgumentNullException.ThrowIfNull(tree);
            StringBuilder text = new();
            WriteRecount(text, tree, scope: null, option: string.Empty);
            _ = text.Append("SELECT @@ROWCOUNT AS [Repaired];\n");
            return text.ToString();
        }

        /// <summary>
        ///     Writes the statements a save runs after its root update (T1–T5b): the affected set,
        ///     the paths walked top-down from rows whose parent lies outside it, the cycle fence, the
        ///     old nodes, the re-path that never stamps, and the scoped recount.
        /// </summary>
        /// <param name="newRows">The save's table of new roots.</param>
        /// <param name="existingRows">The save's table of existing roots.</param>
        public void AfterSave(string newRows, string existingRows)
        {
            ArgumentException.ThrowIfNullOrEmpty(newRows);
            ArgumentException.ThrowIfNullOrEmpty(existingRows);
            string table = _tree.Table;
            string node = Col(_tree.Node);
            string parent = Col(_tree.Parent);
            string affected = Local(ReservedSqlNames.StatementNames.Affected);
            string paths = Local(ReservedSqlNames.StatementNames.Paths);
            string walk = Cte("walk");

            // --- T1: every saved row, and every descendant of a saved existing row by its node before the save ---
            Line($"DECLARE {affected} TABLE ([Id] {_tree.KeyType} PRIMARY KEY);");
            Line($"INSERT INTO {affected} ([Id])");
            Line($"SELECT [Id] FROM {newRows}");
            Line($"UNION SELECT [Id] FROM {existingRows}");
            Line($"UNION SELECT d.[Id] FROM {table} AS d");
            Line($"      WHERE EXISTS (SELECT 1 FROM {table} AS b JOIN {existingRows} AS s ON s.[Id] = b.[Id]");
            Statement($"                    WHERE d.{node}.IsDescendantOf(b.{node}) = 1)", Option(recursive: false));

            // --- T2: the paths, top-down from the affected rows whose parent's node is final ---
            // The anchors' parents are U-locked to the commit, by a seek on the key so no other row
            // is locked, and the locking read sees the latest committed node under row versioning. A
            // concurrent save moving an ancestor of the parent holds the moved row from its root
            // update and takes the parent when it re-paths it (T4); this save's recount (T5b) needs
            // the moved row. So the parent is read after the move commits, or the two deadlock and
            // the victim's re-run reads it then: never a node the move is about to change.
            Line($"DECLARE {paths} TABLE ([Id] {_tree.KeyType} PRIMARY KEY, [Path] varchar(892) NOT NULL);");
            Line($"WITH {walk} ([Id], [Path]) AS (");
            Line($"    SELECT t.[Id], CAST(ISNULL(p.{node}.ToString(), '/') + CAST(t.[Id] AS varchar(20)) + '/' AS varchar(892))");
            Line($"    FROM {table} AS t");
            Line($"    JOIN {affected} AS a ON a.[Id] = t.[Id]");
            Line($"    LEFT JOIN {table} AS p WITH (UPDLOCK, ROWLOCK, FORCESEEK) ON p.[Id] = t.{parent}");
            Line($"    WHERE t.{parent} IS NULL OR t.{parent} NOT IN (SELECT [Id] FROM {affected})");
            Line("    UNION ALL");
            Line("    SELECT c.[Id], CAST(x.[Path] + CAST(c.[Id] AS varchar(20)) + '/' AS varchar(892))");
            Line($"    FROM {table} AS c");
            Line($"    JOIN {affected} AS a ON a.[Id] = c.[Id]");
            Line($"    JOIN {walk} AS x ON x.[Id] = c.{parent}");
            Line(")");
            Statement($"INSERT INTO {paths} ([Id], [Path]) SELECT [Id], [Path] FROM {walk}", Option(recursive: true));

            // --- T3: an affected row no walk reached lies on a cycle ---
            Line($"IF (SELECT COUNT(*) FROM {paths}) <> (SELECT COUNT(*) FROM {affected})");
            Line($"    THROW {TellmaSqlErrors.Invariant.ToString(CultureInfo.InvariantCulture)}, N'{TellmaSqlErrors.TreeCycleCode}', 1;");

            // --- T3b: the old nodes, before any re-path: the ancestors that lose descendants ---
            CaptureOld(affected);

            // --- T4: the re-path of the rows whose node changed; never stamped ---
            Line($"UPDATE t SET t.{node} = hierarchyid::Parse(x.[Path])");
            Line($"FROM {table} AS t JOIN {paths} AS x ON x.[Id] = t.[Id]");
            Statement($"WHERE t.{node} <> hierarchyid::Parse(x.[Path])", Option(recursive: false));

            // --- T5a and T5b: the scope and the recount ---
            Recount(affected);
        }

        /// <summary>
        ///     Writes the capture of the old nodes (T3b) of a set of rows, before an update recounts
        ///     them or a delete removes them.
        /// </summary>
        /// <param name="rows">The table holding the rows' ids.</param>
        public void CaptureOld(string rows)
        {
            ArgumentException.ThrowIfNullOrEmpty(rows);
            string old = Local(ReservedSqlNames.StatementNames.Old);
            Line($"DECLARE {old} TABLE ([Node] hierarchyid NOT NULL);");
            Statement($"INSERT INTO {old} ([Node]) SELECT t.{Col(_tree.Node)} FROM {_tree.Table} AS t JOIN {rows} AS a ON a.[Id] = t.[Id]", Option(recursive: false));
        }

        /// <summary>
        ///     Writes the scoped recount (T5a and T5b): the old nodes, the current nodes of the
        ///     affected rows, and every ancestor of both, then the counts of each row in that scope
        ///     whose counts changed. The old nodes are captured first (<see cref="CaptureOld" />).
        /// </summary>
        /// <param name="affected">The table of affected rows, or null after a delete, whose scope is the old nodes alone.</param>
        public void Recount(string? affected)
        {
            string node = Col(_tree.Node);
            string scope = Local(ReservedSqlNames.StatementNames.Ancestors);
            string climb = Cte(ReservedSqlNames.StatementNames.Ancestors);

            // --- T5a: the scope, O(rows × depth), never the table ---
            Line($"DECLARE {scope} TABLE ([Node] hierarchyid NOT NULL PRIMARY KEY);");
            Line($"WITH {climb} ([Node]) AS (");
            Line($"    SELECT [Node] FROM {Local(ReservedSqlNames.StatementNames.Old)}");
            if (affected is not null)
            {
                Line($"    UNION SELECT t.{node} FROM {_tree.Table} AS t JOIN {affected} AS a ON a.[Id] = t.[Id]");
            }

            Line($"    UNION ALL SELECT x.[Node].GetAncestor(1) FROM {climb} AS x WHERE x.[Node].GetLevel() > 1)");
            Statement($"INSERT INTO {scope} ([Node]) SELECT DISTINCT [Node] FROM {climb}", Option(recursive: true));

            // --- T5b: the rows in scope whose counts changed ---
            WriteRecount(_text, _tree, scope, Option(recursive: false));
        }

        /// <summary>Writes T5b: the counts of every row in a scope, or of every row, that drifted.</summary>
        /// <param name="text">Where the text is written.</param>
        /// <param name="tree">The tree's table and columns.</param>
        /// <param name="scope">The table of nodes in scope, or null for the whole table.</param>
        /// <param name="option">The statement's <c>OPTION</c> clause, or empty.</param>
        private static void WriteRecount(StringBuilder text, TreeShape tree, string? scope, string option)
        {
            string node = Col(tree.Node);
            string subtree = Col(tree.SubtreeCount);

            // A non-activatable tree keeps the subtree count alone.
            if (tree.ActiveSubtreeCount is string activeColumn && tree.IsActive is string isActive)
            {
                string active = Col(activeColumn);
                _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE t SET t.{subtree} = x.[Cnt], t.{active} = x.[ActiveCnt]\n");
                _ = text.Append(CultureInfo.InvariantCulture, $"FROM {tree.Table} AS t\n");
                if (scope is not null)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"JOIN {scope} AS n ON n.[Node] = t.{node}\n");
                }

                _ = text.Append(CultureInfo.InvariantCulture, $"CROSS APPLY (SELECT COUNT(*) AS [Cnt], SUM(CASE WHEN d.{Col(isActive)} = 1 THEN 1 ELSE 0 END) AS [ActiveCnt]\n");
                _ = text.Append(CultureInfo.InvariantCulture, $"             FROM {tree.Table} AS d WHERE d.{node}.IsDescendantOf(t.{node}) = 1) AS x\n");
                _ = text.Append(CultureInfo.InvariantCulture, $"WHERE t.{subtree} <> x.[Cnt] OR t.{active} <> x.[ActiveCnt]");
            }
            else
            {
                _ = text.Append(CultureInfo.InvariantCulture, $"UPDATE t SET t.{subtree} = x.[Cnt]\n");
                _ = text.Append(CultureInfo.InvariantCulture, $"FROM {tree.Table} AS t\n");
                if (scope is not null)
                {
                    _ = text.Append(CultureInfo.InvariantCulture, $"JOIN {scope} AS n ON n.[Node] = t.{node}\n");
                }

                _ = text.Append(CultureInfo.InvariantCulture, $"CROSS APPLY (SELECT COUNT(*) AS [Cnt] FROM {tree.Table} AS d WHERE d.{node}.IsDescendantOf(t.{node}) = 1) AS x\n");
                _ = text.Append(CultureInfo.InvariantCulture, $"WHERE t.{subtree} <> x.[Cnt]");
            }

            _ = text.Append(option.Length == 0 ? string.Empty : "\n" + option).Append(";\n");
        }

        /// <summary>A column, bracket-quoted.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The quoted column.</returns>
        private static string Col(string column)
        {
            return SqlNames.Quote(column);
        }

        /// <summary>
        ///     A statement's <c>OPTION</c> clause: the recursion limit of a walk, and the recompile
        ///     hint when the statement plans for its rows, in one clause.
        /// </summary>
        /// <param name="recursive">Whether the statement reads a recursive walk.</param>
        /// <returns>The clause, or empty.</returns>
        private string Option(bool recursive)
        {
            return recursive
                ? string.Create(CultureInfo.InvariantCulture, $"OPTION (MAXRECURSION {_tree.MaxDepth - 1}{(_recompile ? ", RECOMPILE" : string.Empty)})")
                : _recompile ? "OPTION (RECOMPILE)" : string.Empty;
        }

        /// <summary>A batch-local name under the statement's prefix.</summary>
        /// <param name="word">The word.</param>
        /// <returns>The name.</returns>
        private string Local(string word)
        {
            return _prefix + word;
        }

        /// <summary>
        ///     A common table expression's name under the statement's prefix without its <c>@</c>:
        ///     <c>tb{b}_walk</c>. A walk's name is scoped to its statement; the prefix keeps it
        ///     clear of any table's.
        /// </summary>
        /// <param name="word">The word.</param>
        /// <returns>The name.</returns>
        private string Cte(string word)
        {
            return _prefix[1..] + word;
        }

        /// <summary>Writes a line.</summary>
        /// <param name="line">The line.</param>
        private void Line(string line)
        {
            _ = _text.Append(line).Append('\n');
        }

        /// <summary>Writes the last line of a statement, with its <c>OPTION</c> clause and the terminator.</summary>
        /// <param name="line">The line.</param>
        /// <param name="option">The clause, or empty.</param>
        private void Statement(string line, string option)
        {
            _ = _text.Append(line).Append(option.Length == 0 ? string.Empty : "\n" + option).Append(";\n");
        }
    }

    /// <summary>A tree table's name and the columns its statements read and write.</summary>
    /// <param name="Table">The table, delimited.</param>
    /// <param name="KeyType">The key's SQL type: <c>int</c> or <c>bigint</c>.</param>
    /// <param name="Parent">The parent key column.</param>
    /// <param name="Node">The node column.</param>
    /// <param name="SubtreeCount">The subtree count column.</param>
    /// <param name="ActiveSubtreeCount">The active subtree count column; null on a non-activatable tree.</param>
    /// <param name="IsActive">The active flag column; null on a non-activatable tree.</param>
    /// <param name="MaxDepth">The deepest level a row may take.</param>
    internal sealed record TreeShape(
        string Table, string KeyType, string Parent, string Node, string SubtreeCount, string? ActiveSubtreeCount, string? IsActive, int MaxDepth)
    {
        /// <summary>The shape of a tree entity's table.</summary>
        /// <param name="entity">The entity.</param>
        /// <returns>The shape.</returns>
        /// <exception cref="ArgumentException">The entity is not a tree.</exception>
        public static TreeShape Of(EntityMetadata entity)
        {
            ArgumentNullException.ThrowIfNull(entity);
            TreeMetadata tree = entity.Tree
                ?? throw new ArgumentException($"The entity {entity.Name} is not a tree, so it has no tree statements.", nameof(entity));

            // An activatable tree keeps the active count beside the subtree count.
            string? isActive = tree.ActiveSubtreeCount is null
                ? null
                : entity.Properties.Single(static property => string.Equals(property.Name, nameof(IActivatable.IsActive), StringComparison.Ordinal)).Column;
            return new TreeShape(
                entity.Table.Delimited,
                entity.Key.ClrType == typeof(long) ? "bigint" : "int",
                tree.ParentKey.Column,
                tree.NodeColumn,
                tree.SubtreeCount.Column,
                tree.ActiveSubtreeCount?.Column,
                isActive,
                tree.MaxDepth);
        }
    }
}
