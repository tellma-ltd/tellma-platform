// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>What a stored cell holds, whatever its number format.</summary>
    internal enum ExcelCellKind
    {
        /// <summary>Nothing: an absent cell, an empty one, or a formula without a cached value.</summary>
        Blank,

        /// <summary>A number (<c>t="n"</c> or no type), its text the stored <c>xsd:double</c>.</summary>
        Number,

        /// <summary>A boolean (<c>t="b"</c>), its text <c>1</c> or <c>0</c>.</summary>
        Boolean,

        /// <summary>Text: a shared string, an inline string, or a formula's cached string.</summary>
        Text,

        /// <summary>An ISO 8601 date (<c>t="d"</c>), its text as stored.</summary>
        Date,

        /// <summary>An error value (<c>t="e"</c>), such as <c>#N/A</c>.</summary>
        Error,
    }

    /// <summary>A stored cell, read: its kind and its text, escapes undone and rich text runs joined.</summary>
    /// <param name="Kind">The cell's kind.</param>
    /// <param name="Text">The cell's text; empty for a blank cell.</param>
    internal readonly record struct ExcelRawCell(ExcelCellKind Kind, string Text)
    {
        /// <summary>The blank cell.</summary>
        public static ExcelRawCell Blank { get; } = new(ExcelCellKind.Blank, string.Empty);
    }
}
