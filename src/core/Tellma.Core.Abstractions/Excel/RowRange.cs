// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>An inclusive run of Excel rows, 1-based with the header excluded; on the wire <c>{ "from": 2, "to": 101 }</c>.</summary>
    /// <param name="From">The run's first row; at least 1.</param>
    /// <param name="To">The run's last row; at least <paramref name="From" />.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="From" /> is below 1, or <paramref name="To" /> below <paramref name="From" />.</exception>
    public sealed record RowRange(int From, int To)
    {
        /// <inheritdoc cref="RowRange(int, int)" />
        public int From { get; } = From >= 1 ? From : throw new ArgumentOutOfRangeException(nameof(From), From, "A row range starts at row 1 or later.");

        /// <inheritdoc cref="RowRange(int, int)" />
        public int To { get; } = To >= From ? To : throw new ArgumentOutOfRangeException(nameof(To), To, "A row range ends at or after its first row.");
    }
}
