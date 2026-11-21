// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A display export's row source: the stack's own, each page's rows folded from the select
    ///     terms back into one value per column, so the writer sees exactly the sheet's columns.
    /// </summary>
    /// <param name="inner">The stack's row source.</param>
    /// <param name="columns">The expansion whose terms the query selects.</param>
    internal sealed class FoldingRowSource(IExcelRowSource inner, DisplayColumns columns) : IExcelRowSource
    {
        /// <summary>The stack's row source.</summary>
        private readonly IExcelRowSource _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        /// <summary>The expansion.</summary>
        private readonly DisplayColumns _columns = columns ?? throw new ArgumentNullException(nameof(columns));

        /// <inheritdoc />
        public async IAsyncEnumerable<ExcelPage> Pages(ExcelQuery query, [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(query);
            await foreach (ExcelPage page in _inner.Pages(query, cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                yield return page with { Rows = [.. page.Rows.Select(_columns.Fold)] };
            }
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>>> ReadAsync(IReadOnlyList<ExcelQuery> queries, CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(queries, cancellationToken);
        }
    }
}
