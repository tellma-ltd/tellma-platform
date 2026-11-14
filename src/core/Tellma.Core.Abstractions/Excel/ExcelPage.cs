// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>One page of a root query's rows, with the complete rows of every dependent collection under them.</summary>
    /// <param name="Rows">The page's rows, each its column values in the query's select order: the reader's values (numbers, dates, strings, booleans, GUIDs), unformatted.</param>
    /// <param name="Dependents">Parallel to the query's dependents: each the complete rows of that collection under this page's rows, with its own dependents.</param>
    /// <param name="Count">The capped count on the first page of a query with a count cap; null otherwise.</param>
    /// <exception cref="ArgumentNullException"><paramref name="Rows" /> or <paramref name="Dependents" /> is null.</exception>
    public sealed record ExcelPage(IReadOnlyList<IReadOnlyList<object?>> Rows, IReadOnlyList<ExcelPage> Dependents, int? Count)
    {
        /// <inheritdoc cref="ExcelPage(IReadOnlyList{IReadOnlyList{object?}}, IReadOnlyList{ExcelPage}, int?)" />
        public IReadOnlyList<IReadOnlyList<object?>> Rows { get; init => field = value ?? throw new ArgumentNullException(nameof(Rows)); } =
            Rows ?? throw new ArgumentNullException(nameof(Rows));

        /// <inheritdoc cref="ExcelPage(IReadOnlyList{IReadOnlyList{object?}}, IReadOnlyList{ExcelPage}, int?)" />
        public IReadOnlyList<ExcelPage> Dependents { get; init => field = value ?? throw new ArgumentNullException(nameof(Dependents)); } =
            Dependents ?? throw new ArgumentNullException(nameof(Dependents));
    }
}
