// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A delete: children first, in explicit statements, never by cascade. Exactly one shape:
    ///     <see cref="ByIds" />, <see cref="ByQuery" /> or <see cref="WithDescendants" />.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "The shapes are a closed set read as DeleteSpec<T>.ByIds, .ByQuery and .WithDescendants.")]
    public abstract record DeleteSpec<TEntity>
        where TEntity : class
    {
        /// <summary>Creates a shape; the set of shapes is closed.</summary>
        private protected DeleteSpec()
        {
        }

        /// <summary>The rows with the given ids, each of which must exist and pass the grant filter.</summary>
        /// <param name="Ids">The ids.</param>
        /// <param name="ReadFilter">
        ///     The caller's read filter: existence is counted under it, so a hidden id counts as
        ///     missing.
        /// </param>
        /// <param name="Filter">The action's grant filter.</param>
        /// <param name="ExpectedStamps">
        ///     The stamp each row was read with, in the order of <paramref name="Ids" />, with seven
        ///     fractional digits; null to skip the comparison.
        /// </param>
        public sealed record ByIds(
            IReadOnlyList<object> Ids,
            FilterTree? ReadFilter,
            FilterTree? Filter,
            IReadOnlyList<DateTimeOffset?>? ExpectedStamps = null) : DeleteSpec<TEntity>;

        /// <summary>The rows a query selects, capped and count-verified in SQL.</summary>
        /// <param name="Filter">The query's filter, conjoined with the access filter.</param>
        /// <param name="Arguments">The query's declared parameters, or null for none.</param>
        /// <param name="Cap">The most rows the statement may delete.</param>
        /// <param name="ExpectedCount">The count the caller previewed; a different count fails the batch.</param>
        public sealed record ByQuery(FilterTree Filter, QueryArguments? Arguments, int Cap, int ExpectedCount) : DeleteSpec<TEntity>;

        /// <summary>Tree roots with every descendant, in one statement.</summary>
        /// <param name="Ids">The roots' ids.</param>
        /// <param name="ReadFilter">The caller's read filter, under which every root must exist.</param>
        /// <param name="Filter">The action's grant filter, which the whole closure must pass.</param>
        public sealed record WithDescendants(IReadOnlyList<object> Ids, FilterTree? ReadFilter, FilterTree? Filter) : DeleteSpec<TEntity>;
    }

    /// <summary>What a delete removed, read after the batch executes.</summary>
    /// <param name="DeletedIds">The ids of the deleted root rows.</param>
    public sealed record DeleteReceipt(IReadOnlySet<object> DeletedIds);

    /// <summary>
    ///     A delete appended to a batch: its receipt, and the batch-local names it declared for the
    ///     statements composed after it.
    /// </summary>
    /// <param name="Receipt">The receipt, available after the batch executes.</param>
    /// <param name="DeletedIds">The batch-local keys table holding the deleted root ids.</param>
    /// <param name="Captures">One capture per captured column of every written table.</param>
    public sealed record DeleteHandle(
        BatchResult<DeleteReceipt> Receipt, SqlIdentifier DeletedIds, IReadOnlyList<ColumnCapture> Captures);
}
