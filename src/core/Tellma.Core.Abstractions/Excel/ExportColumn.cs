// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Excel
{
    /// <summary>One column of a display-shape export, as the grid shows it.</summary>
    /// <remarks>
    ///     A path ending at a navigation is written as the target's display name, and a path ending at
    ///     a multilingual group as the text in the reader's language; any other path or expression is
    ///     written as its value. The export refuses a blank expression and a header longer than
    ///     <see cref="MaxHeaderLength" /> as a bad request.
    /// </remarks>
    /// <param name="Expression">The column's key as the grid holds it: a path (<c>Customer.Code</c>) or a query expression (<c>Total * 2</c>).</param>
    /// <param name="Header">The header text, at most <see cref="MaxHeaderLength" /> characters; null derives it from the labels.</param>
    public sealed record ExportColumn(string Expression, string? Header)
    {
        /// <summary>The longest a header may be.</summary>
        public const int MaxHeaderLength = 255;
    }
}
