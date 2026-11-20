// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Collections.Frozen;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Data.Batches;

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

        /// <summary>
        ///     The first table, in table order, outside a root's aggregate that a key's action writes when
        ///     a save deletes the root's child rows and that has a user-level version tag rule; null when
        ///     there is none. A key's action reports no rows, so a save cannot tell which users such a
        ///     table's reached rows name.
        /// </summary>
        /// <param name="root">The top-level entity.</param>
        /// <param name="userIdColumns">The user-id columns of a table's user-level version tag rules.</param>
        /// <returns>The table, or null.</returns>
        public TableName? SaveConsequenceNamingUsers(EntityMetadata root, Func<TableName, IReadOnlyList<string>> userIdColumns)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(userIdColumns);

            // --- The aggregate's child tables, recursively; a save never deletes the root ---
            List<TableName> children = [];
            Stack<EntityMetadata> pending = new([root]);
            while (pending.TryPop(out EntityMetadata? entity))
            {
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    children.Add(collection.Child.Table);
                    pending.Push(collection.Child);
                }
            }

            HashSet<TableName> aggregate = [root.Table, .. children];
            return WrittenByDeleting(children)
                .Where(table => !aggregate.Contains(table))
                .OrderBy(static table => table.Schema, StringComparer.Ordinal)
                .ThenBy(static table => table.Name, StringComparer.Ordinal)
                .FirstOrDefault(table => userIdColumns(table).Count > 0);
        }

        /// <summary>
        ///     The keys' shapes a user-level version tag cannot follow, each a problem of
        ///     <c>core.version-tags</c>: a <c>SET DEFAULT</c> key on a rule's user-id column, whose new
        ///     user no capture can know, and a top-level entity whose save deletes child rows that a
        ///     key's action carries to a table with a rule. Found at startup, so no first save or
        ///     delete of such an entity fails at run time.
        /// </summary>
        /// <param name="metadata">The running model's metadata.</param>
        /// <param name="registry">The version tag registry, whose user-level rules name the user-id columns.</param>
        /// <returns>The problems, in key order and then in entity order; empty for a sound model.</returns>
        public IReadOnlyList<CompositionProblem> UserTagProblems(IEntityMetadataProvider metadata, IVersionTagRegistry registry)
        {
            ArgumentNullException.ThrowIfNull(metadata);
            ArgumentNullException.ThrowIfNull(registry);
            List<CompositionProblem> problems = [];

            // --- A SET DEFAULT key on a user-id column ---
            foreach (ForeignKeyAction key in _byPrincipal.Values
                .SelectMany(static keys => keys)
                .Where(static key => key.OnDelete == ReferentialAction.SetDefault)
                .OrderBy(static key => key.Constraint, StringComparer.Ordinal))
            {
                if (key.Columns.Intersect(UserIdCapture.Columns(registry, key.Dependent), StringComparer.Ordinal).Any())
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.VersionTags,
                        DefaultUserMessage(key),
                        "Declare the key ON DELETE CASCADE, SET NULL or NO ACTION, so the users it reaches can be captured."));
                }
            }

            // --- A save whose child deletes reach a table naming users ---
            foreach (EntityMetadata root in metadata.All.Where(static entity => entity.IsTopLevel).OrderBy(static entity => entity.Name, StringComparer.Ordinal))
            {
                if (SaveConsequenceNamingUsers(root, table => UserIdCapture.Columns(registry, table)) is TableName named)
                {
                    problems.Add(new CompositionProblem(
                        CoreCheckNames.VersionTags,
                        SaveMessage(root, named),
                        "Declare the key to the child table without an ON DELETE action, or keep the user-level rule off the referencing table."));
                }
            }

            return problems;
        }

        /// <summary>The refusal of a <c>SET DEFAULT</c> key on a user-level rule's user-id column.</summary>
        /// <param name="key">The key.</param>
        /// <returns>The message.</returns>
        public static string DefaultUserMessage(ForeignKeyAction key)
        {
            ArgumentNullException.ThrowIfNull(key);
            return $"The key {key.Constraint} sets a default user in {key.Dependent} when a row of {key.Principal} is deleted, so a user-level version tag cannot tell which user it names.";
        }

        /// <summary>The refusal of a save whose child deletes reach a table with a user-level rule.</summary>
        /// <param name="root">The saved entity.</param>
        /// <param name="named">The reached table.</param>
        /// <returns>The message.</returns>
        public static string SaveMessage(EntityMetadata root, TableName named)
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentNullException.ThrowIfNull(named);
            return $"Saving {root.Name} deletes child rows whose referencing keys write {named}, which has a user-level version tag rule, so a save cannot tell which users those rows name.";
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
