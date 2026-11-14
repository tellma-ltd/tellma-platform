// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>
    ///     The rows the Excel codec reads, bound to one stack, or one child query surface, and one
    ///     <see cref="ExportSource" />. The codec never opens a connection: the CRUD pipeline implements
    ///     this over the data layer with the caller's read filter.
    /// </summary>
    /// <remarks>
    ///     Creating a row source runs nothing. The stack's read check runs on the first
    ///     <see cref="Pages" /> or <see cref="ReadAsync" /> call, before any row is read.
    /// </remarks>
    public interface IExcelRowSource
    {
        /// <summary>
        ///     Streams a root query page by page, one read batch per page of the stack's take ceiling,
        ///     each page carrying the complete rows of every dependent collection under it.
        /// </summary>
        /// <param name="query">The root query.</param>
        /// <param name="cancellationToken">Stops the stream between pages and cancels the page in flight.</param>
        /// <returns>The pages, in the query's order.</returns>
        IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query, CancellationToken cancellationToken = default);

        /// <summary>Reads several flat queries in one read batch, each completed in full, such as an import's lookups.</summary>
        /// <param name="queries">The queries.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Parallel to <paramref name="queries" />: each query's rows, each row its column values in select order.</returns>
        Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(
            IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default);
    }
}
