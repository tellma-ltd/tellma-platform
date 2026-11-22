// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Maps an export request to the one source its rows come from, and refuses a request no
    ///     export can serve: ids beside the clauses, an empty id list for a display export, a child
    ///     surface's named filters, a display request's column list out of bounds.
    /// </summary>
    internal static class ExportSources
    {
        /// <summary>
        ///     The source of a request: its ids with the ordering beside them, else its clauses, each
        ///     optional.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="template">Whether an empty id list is valid: the editable export's template of headers and lists.</param>
        /// <param name="child">Whether the request reads a child query surface, which takes no named filters.</param>
        /// <returns>The source.</returns>
        /// <exception cref="BadRequestException">The request carries ids beside a clause, an empty id list where it is not a template, or named filters for a child surface.</exception>
        public static ExportSource Of(ExportSourceRequest request, bool template, bool child)
        {
            ArgumentNullException.ThrowIfNull(request);

            // --- A child surface has no named filters of its own ---
            if (child && request.NamedFilters is { Count: > 0 })
            {
                throw Fault("A child query surface takes no named filters.", "namedFilters");
            }

            // --- By the clauses: each optional, as the grid's query ---
            if (request.Ids is not IReadOnlyList<long> ids)
            {
                return new ExportSource.ByQuery(request.Filters, request.NamedFilters, request.Search, request.OrderBy, request.Arguments, request.IncludeInactive);
            }

            // --- By ids: never beside a clause, and never empty but as a template ---
            return Clause(request) is string clause
                ? throw Fault($"The request names its rows by ids and by {clause}; it names them one way.", clause)
                : ids.Count == 0 && !template
                ? throw Fault("The request names no ids; an export by ids names at least one.", "ids")
                : new ExportSource.ByIds(ids, request.OrderBy);
        }

        /// <summary>Refuses a display request's columns: none, more than the maximum, a blank expression or an overlong header.</summary>
        /// <param name="request">The request.</param>
        /// <exception cref="BadRequestException">The columns are out of bounds.</exception>
        public static void CheckColumns(ExportRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            IReadOnlyList<ExportColumn>? columns = request.Columns;
            if (columns is null || columns.Count == 0)
            {
                throw Fault("The request lists no columns; an export writes at least one.", "columns");
            }

            if (columns.Count > ExportRequest.MaxColumns)
            {
                throw new BadRequestException(
                    string.Create(CultureInfo.InvariantCulture, $"The request lists {columns.Count} columns; an export writes at most {ExportRequest.MaxColumns}."),
                    new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = "columns", ["actual"] = columns.Count, ["maximum"] = ExportRequest.MaxColumns });
            }

            for (int index = 0; index < columns.Count; index++)
            {
                string at = string.Create(CultureInfo.InvariantCulture, $"columns[{index}]");
                ExportColumn? column = columns[index];
                if (column is null || string.IsNullOrWhiteSpace(column.Expression))
                {
                    throw Fault($"The column at {at} has no expression.", at);
                }

                if (column.Header is { Length: > ExportColumn.MaxHeaderLength } header)
                {
                    throw new BadRequestException(
                        string.Create(CultureInfo.InvariantCulture, $"The header of the column at {at} has {header.Length} characters; a header has at most {ExportColumn.MaxHeaderLength}."),
                        new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = $"{at}.header", ["actual"] = header.Length, ["maximum"] = ExportColumn.MaxHeaderLength });
                }
            }
        }

        /// <summary>The first clause a request sets beside its ids, by its wire name.</summary>
        /// <param name="request">The request.</param>
        /// <returns>The clause's name; null when none is set.</returns>
        private static string? Clause(ExportSourceRequest request)
        {
            return request.Filters is { Count: > 0 } ? "filters"
                : request.NamedFilters is { Count: > 0 } ? "namedFilters"
                : !string.IsNullOrWhiteSpace(request.Search) ? "search"
                : request.Arguments is { Count: > 0 } ? "arguments"
                : request.IncludeInactive ? "includeInactive"
                : null;
        }

        /// <summary>A request fault at a path.</summary>
        /// <param name="detail">The English sentence.</param>
        /// <param name="path">The path in the request.</param>
        /// <returns>The exception.</returns>
        private static BadRequestException Fault(string detail, string path)
        {
            return new BadRequestException(detail, new Dictionary<string, object?>(StringComparer.Ordinal) { ["path"] = path });
        }
    }
}
