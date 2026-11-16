// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Scripted
{
    /// <summary>What a scripted batch recorded a call as.</summary>
    internal enum ScriptedKind
    {
        /// <summary>An entity query, <see cref="IDataBatch.Query{TEntity}" />.</summary>
        Query,

        /// <summary>A columnar query, <see cref="IDataBatch.Rows" />.</summary>
        Rows,

        /// <summary>A capped count, <see cref="IDataBatch.Count" />.</summary>
        Count,

        /// <summary>A row of path-free expressions, <see cref="IDataBatch.EvaluateConstant" />.</summary>
        EvaluateConstant,

        /// <summary>A save, <see cref="IDataBatch.Save{TEntity}" />.</summary>
        Save,

        /// <summary>A uniform update, <see cref="IDataBatch.Update{TEntity}" />.</summary>
        Update,

        /// <summary>A delete, <see cref="IDataBatch.Delete{TEntity}" />.</summary>
        Delete,

        /// <summary>A count assertion, <see cref="IDataBatch.Assert" />.</summary>
        Assert,

        /// <summary>Raw text, <see cref="IDataBatch.Sql" />.</summary>
        Sql,

        /// <summary>A table-valued parameter, <see cref="IDataBatch.Tvp{TRow}" />.</summary>
        Tvp,

        /// <summary>A batch-local id table, <see cref="IDataBatch.DeclareIdTable" />.</summary>
        DeclareIdTable,

        /// <summary>Cached inputs the batch depends on, <see cref="IDataBatch.DependsOn" />.</summary>
        DependsOn,

        /// <summary>A cached entity list, <see cref="IDataBatch.FromCache{TEntity}" />.</summary>
        FromCache,

        /// <summary>A tenant-level tag bump, <see cref="IDataBatch.BumpVersionTag" />.</summary>
        BumpVersionTag,
    }

    /// <summary>
    ///     One call a scripted batch recorded, in append order: what was called, with what, and the
    ///     batch it was appended to, whose table-valued parameters a response may read.
    /// </summary>
    /// <param name="batch">The batch it was appended to.</param>
    /// <param name="ordinal">Its 0-based position among the batch's calls.</param>
    /// <param name="kind">What was called.</param>
    /// <param name="entity">The entity or row type it was called with; null for none.</param>
    /// <param name="statement">What it was called with: the query, the spec, the text, the rows.</param>
    /// <param name="arguments">The query arguments it carried; null for none.</param>
    /// <param name="options">Its options: the row-query options, the SQL options, the cap, the concurrency mode.</param>
    /// <param name="name">The name it declared or named: a table-valued parameter's, an id table's, a tag's, an assertion's code.</param>
    internal sealed class ScriptedStatement(
        ScriptedBatch batch, int ordinal, ScriptedKind kind, Type? entity, object? statement, QueryArguments? arguments, object? options, string? name)
    {
        /// <summary>The batch it was appended to.</summary>
        public ScriptedBatch Batch { get; } = batch;

        /// <summary>Its 0-based position among the batch's calls.</summary>
        public int Ordinal { get; } = ordinal;

        /// <summary>What was called.</summary>
        public ScriptedKind Kind { get; } = kind;

        /// <summary>The entity or row type it was called with; null for none.</summary>
        public Type? Entity { get; } = entity;

        /// <summary>What it was called with: the query, the spec, the text, the rows.</summary>
        public object? Statement { get; } = statement;

        /// <summary>The query arguments it carried; null for none.</summary>
        public QueryArguments? Arguments { get; } = arguments;

        /// <summary>Its options: the row-query options, the SQL options, the cap, the concurrency mode.</summary>
        public object? Options { get; } = options;

        /// <summary>The name it declared or named: a table-valued parameter's, an id table's, a tag's, an assertion's code.</summary>
        public string? Name { get; } = name;

        /// <summary>The entity query of a <see cref="ScriptedKind.Query" /> call.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <returns>The query.</returns>
        public EntityQuery<T> Query<T>()
            where T : class
        {
            return (EntityQuery<T>)Statement!;
        }

        /// <summary>The spec of a <see cref="ScriptedKind.Rows" />, <see cref="ScriptedKind.Count" /> or <see cref="ScriptedKind.Assert" /> call.</summary>
        public QuerySpec Spec => (QuerySpec)Statement!;

        /// <summary>The text of a <see cref="ScriptedKind.Sql" /> call.</summary>
        public FormattableString Text => (FormattableString)Statement!;

        /// <summary>The cap of a <see cref="ScriptedKind.Count" /> call.</summary>
        public int Cap => (int)Options!;

        /// <summary>
        ///     The rows of the table-valued parameter the call's first key-set restriction reads: the
        ///     keys of a keyed query.
        /// </summary>
        /// <typeparam name="TRow">The parameter's row type.</typeparam>
        /// <returns>The rows.</returns>
        /// <exception cref="InvalidOperationException">The call carries no key-set restriction.</exception>
        public IReadOnlyList<TRow> KeySet<TRow>()
            where TRow : class
        {
            // An entity query is generic over its entity, so its restrictions are read by reflection.
            IReadOnlyList<KeySetRestriction> restrictions = Statement switch
            {
                QuerySpec spec => spec.Restrictions,
                { } query when Kind == ScriptedKind.Query => (IReadOnlyList<KeySetRestriction>)query.GetType().GetProperty(nameof(EntityQuery<>.Restrictions))!.GetValue(query)!,
                _ => [],
            };
            return restrictions.Count > 0
                ? Batch.TvpRows<TRow>(restrictions[0].TableSource)
                : throw new InvalidOperationException($"The {Kind} call {Ordinal} carries no key-set restriction.");
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Entity is null ? $"{Kind} #{Ordinal}" : $"{Kind}<{Entity.Name}> #{Ordinal}";
        }
    }
}
