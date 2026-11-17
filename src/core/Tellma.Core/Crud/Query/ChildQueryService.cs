// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Crud.Query
{
    // The child query surfaces are spec 0015 §2.4; their query is §5.2 as a stack's.
    /// <summary>
    ///     The read-only operations of the child query surfaces: each query runs exactly as a
    ///     stack's, under the child's own read securable, with the convention's search and no named
    ///     filters. Scoped.
    /// </summary>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="queries">The scope's query operation.</param>
    internal sealed class ChildQueryService(QuerySurfaces surfaces, QueryOperation queries) : IChildQueryService
    {
        /// <inheritdoc />
        public Task<RowPage> QueryAsync(string resource, QueryRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(request);
            QuerySurface surface = surfaces.Child(resource)
                ?? throw new InvalidOperationException($"No child query surface is named '{resource}'.");
            return queries.RunAsync(surface, request, searchFilter: null, cancellationToken);
        }

        /// <inheritdoc />
        public IExcelRowSource ExcelRowSource(string resource, ExportSource source)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(source);
            _ = surfaces.Child(resource) ?? throw new InvalidOperationException($"No child query surface is named '{resource}'.");
            throw new NotSupportedException($"The child query surface {resource} does not serve an Excel row source yet.");
        }
    }
}
