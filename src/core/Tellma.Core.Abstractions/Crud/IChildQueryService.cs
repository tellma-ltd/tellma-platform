// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>The read-only operations of the child query surfaces, each under the child's own read securable, which composes its owner's.</summary>
    public interface IChildQueryService
    {
        /// <summary>Queries a child query surface, exactly as a stack's query runs.</summary>
        /// <param name="resource">The surface's child entity name.</param>
        /// <param name="request">The query.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page of rows, with the capped count and the ancestors when requested.</returns>
        /// <exception cref="InvalidOperationException">No child query surface has the name.</exception>
        Task<RowPage> QueryAsync(string resource, QueryRequest request, CancellationToken cancellationToken = default);

        /// <summary>The row source of an exportable child query surface, bound to one source; it runs nothing until read.</summary>
        /// <param name="resource">The surface's child entity name.</param>
        /// <param name="source">The rows to read: by ids or by query, without named filters.</param>
        /// <returns>The row source.</returns>
        /// <exception cref="InvalidOperationException">No exportable child query surface has the name.</exception>
        IExcelRowSource ExcelRowSource(string resource, ExportSource source);
    }
}
