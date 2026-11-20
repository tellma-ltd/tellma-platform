// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Data.Writes
{
    // Spec 0012 §5.2: a batch declares every table its statements write, and the database writes
    // more than a delete's own statements name when a foreign key referencing a deleted table
    // carries an ON DELETE action. A sibling's key cascades (§4.2), a schedule's state cascades and
    // its jobs' schedule is set to null (spec 0020 §2.1 and §7.2), a user's stamp and preference rows
    // cascade (spec 0014): the tables those actions write are the database's consequence of the
    // delete, so the delete declares them, read from the relational model rather than listed by hand.
    /// <summary>
    ///     The foreign keys of the tenant model whose <c>ON DELETE</c> action writes their own table
    ///     when a row they reference is deleted: <c>CASCADE</c>, which deletes the referencing rows,
    ///     and <c>SET NULL</c> and <c>SET DEFAULT</c>, which update them. Read once from the
    ///     relational model, so every key a convention or a model configuration declares is counted,
    ///     whichever feature declared it. A singleton.
    /// </summary>
    internal sealed class ForeignKeyActions
    {
        /// <summary>The schema of a table the model maps without one.</summary>
        private const string DefaultSchema = "dbo";

        /// <summary>The keys with a writing action, by the table they reference, each list in dependent and name order.</summary>
        private readonly FrozenDictionary<TableName, ForeignKeyAction[]> _byPrincipal;

        /// <summary>The keys with a writing action, by the table that holds them, each list in principal and name order.</summary>
        private readonly FrozenDictionary<TableName, ForeignKeyAction[]> _byDependent;

        /// <summary>Reads the keys of a model.</summary>
        /// <param name="model">The tenant model.</param>
        public ForeignKeyActions(IModel model)
        {
            ArgumentNullException.ThrowIfNull(model);

            // --- Every key whose delete action writes the referencing table, grouped by the referenced one ---
            Dictionary<TableName, List<ForeignKeyAction>> byPrincipal = [];
            foreach (ITable table in model.GetRelationalModel().Tables)
            {
                foreach (IForeignKeyConstraint key in table.ForeignKeyConstraints)
                {
                    if (key.OnDeleteAction is not (ReferentialAction.Cascade or ReferentialAction.SetNull or ReferentialAction.SetDefault))
                    {
                        continue;
                    }

                    TableName principal = NameOf(key.PrincipalTable);
                    if (!byPrincipal.TryGetValue(principal, out List<ForeignKeyAction>? keys))
                    {
                        byPrincipal[principal] = keys = [];
                    }

                    keys.Add(new ForeignKeyAction(
                        key.Name,
                        NameOf(table),
                        principal,
                        key.OnDeleteAction,
                        [.. key.Columns.Select(static column => column.Name)],
                        [.. key.PrincipalColumns.Select(static column => column.Name)]));
                }
            }

            // In a fixed order, so the tables a plan derives never depend on the model's enumeration order.
            _byPrincipal = byPrincipal.ToFrozenDictionary(
                static pair => pair.Key,
                static pair => pair.Value
                    .OrderBy(static key => key.Dependent.Schema, StringComparer.Ordinal)
                    .ThenBy(static key => key.Dependent.Name, StringComparer.Ordinal)
                    .ThenBy(static key => key.Constraint, StringComparer.Ordinal)
                    .ToArray());
            _byDependent = _byPrincipal.Values
                .SelectMany(static keys => keys)
                .GroupBy(static key => key.Dependent)
                .ToFrozenDictionary(
                    static group => group.Key,
                    static group => group
                        .OrderBy(static key => key.Principal.Schema, StringComparer.Ordinal)
                        .ThenBy(static key => key.Principal.Name, StringComparer.Ordinal)
                        .ThenBy(static key => key.Constraint, StringComparer.Ordinal)
                        .ToArray());
        }

        /// <summary>
        ///     The keys that reference a table with a writing delete action, ordered by the referencing
        ///     table and the key's name; empty when none does.
        /// </summary>
        /// <param name="principal">The referenced table.</param>
        /// <returns>The keys.</returns>
        public IReadOnlyList<ForeignKeyAction> Referencing(TableName principal)
        {
            ArgumentNullException.ThrowIfNull(principal);
            return _byPrincipal.TryGetValue(principal, out ForeignKeyAction[]? keys) ? keys : [];
        }

        /// <summary>
        ///     The keys a table holds with a writing delete action, ordered by the referenced table and
        ///     the key's name; empty when it holds none.
        /// </summary>
        /// <param name="dependent">The referencing table.</param>
        /// <returns>The keys.</returns>
        public IReadOnlyList<ForeignKeyAction> HeldBy(TableName dependent)
        {
            ArgumentNullException.ThrowIfNull(dependent);
            return _byDependent.TryGetValue(dependent, out ForeignKeyAction[]? keys) ? keys : [];
        }

        /// <summary>
        ///     The tables the database writes when rows of the given tables are deleted: every table
        ///     whose key to a deleted table cascades or sets a value, and, where a key cascades, the
        ///     tables the cascaded rows' own keys write in turn. A table whose rows a key only updates
        ///     loses no rows, so it writes nothing further.
        /// </summary>
        /// <param name="deleted">The tables whose rows the delete's own statements remove.</param>
        /// <returns>The tables the keys' actions write; the deleted tables themselves only when a key's action reaches one.</returns>
        public IReadOnlySet<TableName> WrittenByDeleting(IEnumerable<TableName> deleted)
        {
            ArgumentNullException.ThrowIfNull(deleted);
            return Walk(deleted).Written;
        }

        /// <summary>
        ///     The tables that lose rows when rows of the given tables are deleted: the given tables, and
        ///     every table whose key to one of them cascades, transitively.
        /// </summary>
        /// <param name="deleted">The tables whose rows the delete's own statements remove.</param>
        /// <returns>The tables that lose rows.</returns>
        public IReadOnlySet<TableName> RemovedByDeleting(IEnumerable<TableName> deleted)
        {
            ArgumentNullException.ThrowIfNull(deleted);
            return Walk(deleted).Removed;
        }

        /// <summary>The walk over the keys' actions from the deleted tables.</summary>
        /// <param name="deleted">The tables whose rows the delete's own statements remove.</param>
        /// <returns>The tables the actions write, and the tables that lose rows, the deleted ones included.</returns>
        private (HashSet<TableName> Written, HashSet<TableName> Removed) Walk(IEnumerable<TableName> deleted)
        {
            // --- A walk over the cascades: each table that loses rows is visited once ---
            HashSet<TableName> removed = [.. deleted];
            Queue<TableName> pending = new(removed);
            HashSet<TableName> written = [];
            while (pending.TryDequeue(out TableName? table))
            {
                foreach (ForeignKeyAction key in Referencing(table))
                {
                    _ = written.Add(key.Dependent);
                    if (key.OnDelete == ReferentialAction.Cascade && removed.Add(key.Dependent))
                    {
                        pending.Enqueue(key.Dependent);
                    }
                }
            }

            return (written, removed);
        }

        /// <summary>The name of a relational table, in the default schema when the model maps it without one.</summary>
        /// <param name="table">The table.</param>
        /// <returns>The name.</returns>
        private static TableName NameOf(ITable table)
        {
            return new TableName(string.IsNullOrEmpty(table.Schema) ? DefaultSchema : table.Schema, table.Name);
        }
    }

    /// <summary>A foreign key whose <c>ON DELETE</c> action writes its own table when a row it references is deleted.</summary>
    /// <param name="Constraint">The key's constraint name, such as <c>FK_Jobs_ScheduleId</c>.</param>
    /// <param name="Dependent">The table that holds the key, which the action writes.</param>
    /// <param name="Principal">The table the key references, whose deleted rows fire the action.</param>
    /// <param name="OnDelete">The action: <see cref="ReferentialAction.Cascade" />, <see cref="ReferentialAction.SetNull" /> or <see cref="ReferentialAction.SetDefault" />.</param>
    /// <param name="Columns">The key's columns in the dependent table, in key order.</param>
    /// <param name="PrincipalColumns">The principal table's columns the key references, in key order.</param>
    internal sealed record ForeignKeyAction(
        string Constraint,
        TableName Dependent,
        TableName Principal,
        ReferentialAction OnDelete,
        IReadOnlyList<string> Columns,
        IReadOnlyList<string> PrincipalColumns);
}
