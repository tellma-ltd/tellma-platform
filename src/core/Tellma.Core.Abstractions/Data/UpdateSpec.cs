// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     A uniform update: the same values assigned to every selected row, stamped, in one statement.
    ///     Exactly one shape: <see cref="ByIds" /> or <see cref="ByQuery" />.
    /// </summary>
    /// <typeparam name="TEntity">The entity type.</typeparam>
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "The shapes are a closed set read as UpdateSpec<T>.ByIds and UpdateSpec<T>.ByQuery.")]
    public abstract record UpdateSpec<TEntity>
        where TEntity : class
    {
        /// <summary>Creates a shape; the set of shapes is closed.</summary>
        private protected UpdateSpec()
        {
        }

        /// <summary>The rows with the given ids that pass <see cref="Filter" />.</summary>
        /// <param name="Ids">The ids.</param>
        /// <param name="ReadFilter">
        ///     The caller's read filter: with <paramref name="AllOrNothing" />, existence is counted
        ///     under it, so a hidden id counts as missing.
        /// </param>
        /// <param name="Filter">The action's grant filter, conjoined with the ids.</param>
        /// <param name="Assignments">
        ///     Property name to value, assigned to every row: editable or server-owned properties,
        ///     never write-once or blob references.
        /// </param>
        /// <param name="Stamp">Whether the rows are stamped; false only for platform bookkeeping.</param>
        /// <param name="AllOrNothing">
        ///     Whether every id must be readable under <paramref name="ReadFilter" /> and pass
        ///     <paramref name="Filter" /> before anything is updated.
        /// </param>
        public sealed record ByIds(
            IReadOnlyList<object> Ids,
            FilterTree? ReadFilter,
            FilterTree? Filter,
            IReadOnlyDictionary<string, object?> Assignments,
            bool Stamp = true,
            bool AllOrNothing = false) : UpdateSpec<TEntity>;

        /// <summary>The rows a query selects, capped and count-verified.</summary>
        /// <param name="Filter">The query's filter, conjoined with the access filter.</param>
        /// <param name="Arguments">The query's declared parameters, or null for none.</param>
        /// <param name="Assignments">Property name to value, assigned to every row.</param>
        /// <param name="Cap">The most rows the statement may update.</param>
        /// <param name="ExpectedCount">The count the caller previewed; a different count fails the batch.</param>
        /// <param name="Stamp">Whether the rows are stamped; false only for platform bookkeeping.</param>
        public sealed record ByQuery(
            FilterTree Filter,
            QueryArguments? Arguments,
            IReadOnlyDictionary<string, object?> Assignments,
            int Cap,
            int ExpectedCount,
            bool Stamp = true) : UpdateSpec<TEntity>;
    }

    /// <summary>What an update wrote, read after the batch executes.</summary>
    /// <param name="UpdatedIds">The ids of the updated rows.</param>
    public sealed record UpdateReceipt(IReadOnlySet<object> UpdatedIds);
}
