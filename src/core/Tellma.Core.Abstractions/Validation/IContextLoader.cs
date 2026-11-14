// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     How a validator reads the context its rules need: every load registers a request on the
    ///     current validation round and returns a <see cref="ContextRef{T}" /> at once; awaiting
    ///     <see cref="LoadAsync" /> parks the validator until the round is dispatched as one batch.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Validators run concurrently. When every running validator has parked at
    ///         <see cref="LoadAsync" /> or completed, one batch carries every pending request, every ref
    ///         is filled and every parked validator resumes. An operation costs as many rounds as the
    ///         most <see cref="LoadAsync" /> awaits any one validator makes, bounded by the stack's
    ///         validation-round ceiling, which only an authoring bug exceeds.
    ///     </para>
    ///     <para>
    ///         Requests are deduplicated by their structure: the root, the key property, the filter, the
    ///         parameter values and the raw text. Key values of compatible requests are unioned into one
    ///         key set and their projections merged. A key the round did not find is an explicit
    ///         absence, remembered for the operation like any value.
    ///     </para>
    ///     <para>
    ///         A null <c>select</c> loads every mapped property. The keyed loads are not filtered by the
    ///         caller's read grant, because a rule such as uniqueness is global by definition;
    ///         <see cref="Visible{T}" /> is the load for a rule that must respect visibility. Every load
    ///         reads; none writes.
    ///     </para>
    /// </remarks>
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "select names the query clause it carries, as every query contract does; validators pass it by name (select: \"Id,CenterType\").")]
    public interface IContextLoader
    {
        /// <summary>Loads rows by id.</summary>
        /// <typeparam name="T">The rows' entity type.</typeparam>
        /// <typeparam name="TK">The key type.</typeparam>
        /// <param name="ids">The ids.</param>
        /// <param name="select">The projection, as query select text; null for every mapped property.</param>
        /// <returns>The rows found, by id; a missing id is absent.</returns>
        ContextRef<IReadOnlyDictionary<TK, T>> ByIds<T, TK>(IReadOnlyList<TK> ids, string? select = null)
            where TK : notnull;

        /// <summary>Loads rows by the values of a unique property, restricted to the values given.</summary>
        /// <typeparam name="T">The rows' entity type.</typeparam>
        /// <typeparam name="TK">The property's type.</typeparam>
        /// <param name="key">The unique property's name.</param>
        /// <param name="values">The values.</param>
        /// <param name="select">The projection, as query select text; null for every mapped property.</param>
        /// <returns>The rows found, by value; a missing value is absent.</returns>
        ContextRef<IReadOnlyDictionary<TK, T>> ByKey<T, TK>(string key, IReadOnlyList<TK> values, string? select = null)
            where TK : notnull;

        /// <summary>Loads child rows by their parents' ids.</summary>
        /// <typeparam name="TChild">The child entity type.</typeparam>
        /// <typeparam name="TK">The parent key's type.</typeparam>
        /// <param name="parentIds">The parents' ids.</param>
        /// <param name="select">The projection, as query select text; null for every mapped property.</param>
        /// <returns>Each parent's children, by parent id.</returns>
        ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TChild>>> ByParentIds<TChild, TK>(IReadOnlyList<TK> parentIds, string? select = null)
            where TK : notnull;

        /// <summary>Loads the rows a filter selects, unfiltered by the caller's read grant.</summary>
        /// <typeparam name="T">The rows' entity type.</typeparam>
        /// <param name="filter">The filter.</param>
        /// <param name="arguments">The values of the parameters the filter mentions; null for none.</param>
        /// <param name="select">The projection, as query select text; null for every mapped property.</param>
        /// <returns>The rows.</returns>
        ContextRef<IReadOnlyList<T>> Query<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null);

        /// <summary>Loads the rows a filter selects that the caller may read: the caller's read grant is conjoined.</summary>
        /// <typeparam name="T">The rows' entity type.</typeparam>
        /// <param name="filter">The filter.</param>
        /// <param name="arguments">The values of the parameters the filter mentions; null for none.</param>
        /// <param name="select">The projection, as query select text; null for every mapped property.</param>
        /// <returns>The rows.</returns>
        ContextRef<IReadOnlyList<T>> Visible<T>(FilterTree filter, QueryArguments? arguments = null, string? select = null);

        /// <summary>Loads flat rows of a query.</summary>
        /// <param name="spec">The query.</param>
        /// <param name="arguments">The values of the parameters the query mentions; null for none.</param>
        /// <returns>The rows.</returns>
        ContextRef<QueryRowSet> Rows(QuerySpec spec, QueryArguments? arguments = null);

        /// <summary>Whether any row of a root matches a filter.</summary>
        /// <param name="root">The root's entity name.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="arguments">The values of the parameters the filter mentions; null for none.</param>
        /// <returns>Whether a row matches.</returns>
        ContextRef<bool> Exists(string root, FilterTree filter, QueryArguments? arguments = null);

        /// <summary>Counts the rows of a root that match a filter, up to a cap.</summary>
        /// <param name="root">The root's entity name.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="cap">Where the count stops; a count above it reads as the cap plus one.</param>
        /// <param name="arguments">The values of the parameters the filter mentions; null for none.</param>
        /// <returns>The capped count.</returns>
        ContextRef<int> Count(string root, FilterTree filter, int cap, QueryArguments? arguments = null);

        /// <summary>Loads the ancestor chain of each row of a tree.</summary>
        /// <typeparam name="T">The tree's entity type.</typeparam>
        /// <typeparam name="TK">The key type.</typeparam>
        /// <param name="ids">The rows' ids.</param>
        /// <returns>Each row's ancestors, nearest first, by id.</returns>
        ContextRef<IReadOnlyDictionary<TK, IReadOnlyList<TK>>> Ancestors<T, TK>(IReadOnlyList<TK> ids)
            where TK : notnull;

        /// <summary>
        ///     The escape hatch: a raw read statement. Its options declare no writes, since a validation
        ///     batch never writes, and its text uses none of the platform's reserved names.
        /// </summary>
        /// <param name="sql">The statement, its values as interpolated parameters.</param>
        /// <param name="options">The statement's options; null for none.</param>
        /// <returns>The statement's result sets.</returns>
        ContextRef<RawResult> Sql(FormattableString sql, SqlOptions? options = null);

        /// <summary>
        ///     Parks the validator until the current round is dispatched: when every running validator
        ///     has parked here or completed, one batch carries every pending request and fills its refs.
        ///     Observes the operation's cancellation.
        /// </summary>
        /// <returns>A task that completes once the round ran.</returns>
        /// <exception cref="InvalidOperationException">The operation exceeded its validation-round ceiling.</exception>
        Task LoadAsync();

        /// <summary>Registers an in-memory row, such as a payload entity, so a later load of it by id costs nothing.</summary>
        /// <typeparam name="T">The row's entity type.</typeparam>
        /// <typeparam name="TK">The key type.</typeparam>
        /// <param name="entity">The row.</param>
        void Prime<T, TK>(T entity)
            where TK : notnull;
    }
}
