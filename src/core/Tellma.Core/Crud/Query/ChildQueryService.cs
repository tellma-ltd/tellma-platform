// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud.ExcelRows;

namespace Tellma.Core.Crud.Query
{
    // The child query surfaces are spec 0015 §2.4; their query is §5.2 as a stack's, and an
    // exportable surface's row source is §15.
    /// <summary>
    ///     The read-only operations of the child query surfaces: each query runs exactly as a
    ///     stack's, under the child's own read securable, with the convention's search and no named
    ///     filters; an exportable surface also serves the Excel row source of its display-shape
    ///     export, under the same read. Scoped.
    /// </summary>
    /// <param name="registry">The stack registry, which says which surfaces are exportable.</param>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="queries">The scope's query operation.</param>
    /// <param name="excelRows">The scope's Excel row-source operation.</param>
    internal sealed class ChildQueryService(IStackRegistry registry, QuerySurfaces surfaces, QueryOperation queries, ExcelRowsOperation excelRows) : IChildQueryService
    {
        /// <inheritdoc />
        public Task<RowPage> QueryAsync(string resource, QueryRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(request);
            return queries.RunAsync(Surface(resource), request, searchFilter: null, cancellationToken);
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The source is every row, or names filters by name, which a child query surface does not declare.</exception>
        public IExcelRowSource ExcelRowSource(string resource, ExportSource source)
        {
            ArgumentNullException.ThrowIfNull(resource);
            ArgumentNullException.ThrowIfNull(source);
            QuerySurface surface = Surface(resource);
            return registry.FindChildQuery(resource) is { Exportable: true }
                ? excelRows.Create(surface, source, searchFilter: null)
                : throw new InvalidOperationException($"The child query surface {resource} is not exportable: its root stack serves no export.");
        }

        /// <summary>A child query surface by its resource.</summary>
        /// <param name="resource">The surface's child entity name.</param>
        /// <returns>The surface.</returns>
        /// <exception cref="InvalidOperationException">No child query surface has the name.</exception>
        private QuerySurface Surface(string resource)
        {
            return surfaces.Child(resource) ?? throw new InvalidOperationException($"No child query surface is named '{resource}'.");
        }
    }
}
