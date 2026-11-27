// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    // Spec 0019 §12.3: a background export reports after each page of its parent sheet, with the
    // rows written so far and, when one is known, the total: the capped count the first page of a
    // by-query export carries, or the ids of a by-ids one. The writer asks for the next page only
    // once it wrote the last, so the report after a page counts rows already written.
    /// <summary>
    ///     A background export's row source: the export's own, reporting the rows written after each
    ///     page of the parent sheet's query.
    /// </summary>
    /// <param name="inner">The export's row source.</param>
    /// <param name="root">The parent sheet's query, the one whose pages are counted.</param>
    /// <param name="ids">The rows a by-ids export names, which are its total; null for a by-query export, whose first page counts them.</param>
    /// <param name="report">Receives the rows written so far and the total, null when none is known.</param>
    internal sealed class ProgressRowSource(IExcelRowSource inner, ExcelQuery root, int? ids, Action<int, int?> report) : IExcelRowSource
    {
        /// <summary>The export's row source.</summary>
        private readonly IExcelRowSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        /// <summary>The parent sheet's query.</summary>
        private readonly ExcelQuery _root = root ?? throw new ArgumentNullException(nameof(root));

        /// <summary>Receives each report.</summary>
        private readonly Action<int, int?> _report = report ?? throw new ArgumentNullException(nameof(report));

        /// <inheritdoc />
        public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);

            // --- Another sheet's query streams unreported ---
            if (!ReferenceEquals(query, _root))
            {
                await foreach (ExcelPage page in _inner.Pages(query, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    yield return page;
                }

                yield break;
            }

            // --- The parent sheet: the total from the ids or the first page, then a report after each page ---
            int? total = ids;
            int written = 0;
            bool first = true;
            await foreach (ExcelPage page in _inner.Pages(query, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (first)
                {
                    total ??= page.Count;
                    first = false;
                }

                yield return page;
                written += page.Rows.Count;
                _report(written, total);
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(queries, cancellationToken);
        }
    }
}
