// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>The kind of an encoded cell: how the worksheet writer writes it.</summary>
    internal enum EncodedCellKind
    {
        /// <summary>No value: the cell is left out, or written with its style alone.</summary>
        Blank,

        /// <summary>A number cell; its text is an invariant <c>xsd:double</c>.</summary>
        Number,

        /// <summary>A boolean cell (<c>t="b"</c>); its text is <c>1</c> or <c>0</c>.</summary>
        Boolean,

        /// <summary>An inline string cell (<c>t="inlineStr"</c>); its text is the string, unescaped.</summary>
        Text,
    }

    /// <summary>A value encoded for a cell: its kind and its text.</summary>
    /// <param name="Kind">The cell's kind.</param>
    /// <param name="Text">The cell's text; empty for a blank cell.</param>
    internal readonly record struct EncodedCell(EncodedCellKind Kind, string Text)
    {
        /// <summary>The blank cell.</summary>
        public static EncodedCell Blank { get; } = new(EncodedCellKind.Blank, string.Empty);

        /// <summary>A number cell.</summary>
        /// <param name="text">The number, invariant.</param>
        /// <returns>The cell.</returns>
        public static EncodedCell OfNumber(string text)
        {
            return new(EncodedCellKind.Number, text);
        }

        /// <summary>A boolean cell.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The cell.</returns>
        public static EncodedCell OfBoolean(bool value)
        {
            return new(EncodedCellKind.Boolean, value ? "1" : "0");
        }

        /// <summary>An inline string cell.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The cell.</returns>
        public static EncodedCell OfText(string text)
        {
            return new(EncodedCellKind.Text, text);
        }
    }
}
