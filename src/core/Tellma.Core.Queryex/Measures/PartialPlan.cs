// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Measures
{
    /// <summary>How a partial's values are held and combined.</summary>
    internal enum PartialStorage
    {
        /// <summary>A total: a 128-bit mantissa at the column's scale.</summary>
        Sum,

        /// <summary>A count: a 64-bit whole number.</summary>
        Count,

        /// <summary>The least or greatest of whole numbers, held as 64-bit ones.</summary>
        Whole,

        /// <summary>The least or greatest of decimals: a 128-bit mantissa at the column's scale.</summary>
        Decimal,

        /// <summary>The least or greatest truth value, false first.</summary>
        Bool,

        /// <summary>The least or greatest calendar date.</summary>
        Date,

        /// <summary>The least or greatest date and time.</summary>
        DateTime,

        /// <summary>The least or greatest instant, by instant and then by offset.</summary>
        DateTimeOffset,

        /// <summary>The least or greatest text, ignoring case.</summary>
        Text,

        /// <summary>The least or greatest hierarchy position, by its path.</summary>
        Path,
    }

    /// <summary>How one partial is held, combined and typed.</summary>
    /// <param name="Kind">Which aggregation it is.</param>
    /// <param name="Storage">How its values are held and combined.</param>
    /// <param name="Width">For a least or greatest whole number, the whole type the backend holds it in.</param>
    internal sealed record PartialPlan(MeasurePartialKind Kind, PartialStorage Storage, WholeWidth Width)
    {
        /// <summary>Whether the reader has to report the partial's precision and scale.</summary>
        internal bool NeedsDecimalType => Storage is PartialStorage.Sum or PartialStorage.Decimal;

        /// <summary>Whether the partial keeps the greatest value rather than the least.</summary>
        internal bool IsMaximum => Kind == MeasurePartialKind.Max;
    }
}
