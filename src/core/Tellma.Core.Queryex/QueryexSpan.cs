// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex
{
    /// <summary>
    ///     A half-open character range within the text of one expression.
    /// </summary>
    /// <remarks>
    ///     Offsets are relative to the single expression text they belong to — one clause, or one
    ///     leaf of a filter tree — never to a whole query. Which text a span indexes into is named
    ///     separately, by the location a diagnostic or a discovery site carries.
    /// </remarks>
    /// <param name="Start">The zero-based index of the first character.</param>
    /// <param name="Length">The number of characters covered. May be zero.</param>
    public readonly record struct QueryexSpan(int Start, int Length)
    {
        /// <summary>The index one past the last character covered.</summary>
        public int End => Start + Length;
    }
}
