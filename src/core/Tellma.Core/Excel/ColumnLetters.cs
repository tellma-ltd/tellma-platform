// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Excel
{
    /// <summary>Column letters (<c>A</c> … <c>XFD</c>) and cell references (<c>B7</c>), to and from column indexes.</summary>
    internal static class ColumnLetters
    {
        /// <summary>The columns a worksheet holds: <c>A</c> to <c>XFD</c>.</summary>
        public const int MaxColumns = 16_384;

        /// <summary>The rows a worksheet holds.</summary>
        public const int MaxRows = 1_048_576;

        /// <summary>The letters of every column, computed once: a cell reference is built per cell.</summary>
        private static readonly string[] Letters = [.. Enumerable.Range(0, MaxColumns).Select(Compute)];

        /// <summary>The letters of a column.</summary>
        /// <param name="index">The column's zero-based index.</param>
        /// <returns>The letters (<c>A</c> for 0, <c>AA</c> for 26).</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> lies outside the worksheet.</exception>
        public static string Of(int index)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, MaxColumns);
            return Letters[index];
        }

        /// <summary>A cell reference (<c>B7</c>).</summary>
        /// <param name="column">The column's zero-based index.</param>
        /// <param name="row">The row's one-based number.</param>
        /// <returns>The reference.</returns>
        public static string Reference(int column, int row)
        {
            return string.Concat(Of(column), row.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>An absolute range reference (<c>$A$1:$C$9</c>), as a defined name holds it.</summary>
        /// <param name="firstColumn">The first column's zero-based index.</param>
        /// <param name="firstRow">The first row's one-based number.</param>
        /// <param name="lastColumn">The last column's zero-based index.</param>
        /// <param name="lastRow">The last row's one-based number.</param>
        /// <returns>The reference.</returns>
        public static string AbsoluteRange(int firstColumn, int firstRow, int lastColumn, int lastRow)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"${Of(firstColumn)}${firstRow}:${Of(lastColumn)}${lastRow}");
        }

        /// <summary>Reads column letters, in either case.</summary>
        /// <param name="letters">The letters.</param>
        /// <param name="index">The column's zero-based index.</param>
        /// <returns>True when the letters name a column of the worksheet.</returns>
        public static bool TryParse(ReadOnlySpan<char> letters, out int index)
        {
            index = -1;
            if (letters.IsEmpty || letters.Length > 3)
            {
                return false;
            }

            int value = 0;
            foreach (char c in letters)
            {
                char upper = char.ToUpperInvariant(c);
                if (upper is < 'A' or > 'Z')
                {
                    return false;
                }

                value = (value * 26) + upper - 'A' + 1;
            }

            if (value > MaxColumns)
            {
                return false;
            }

            index = value - 1;
            return true;
        }

        /// <summary>Reads a cell reference (<c>B7</c>), in either case.</summary>
        /// <param name="reference">The reference.</param>
        /// <param name="column">The column's zero-based index.</param>
        /// <param name="row">The row's one-based number.</param>
        /// <returns>True when the reference names a cell of the worksheet.</returns>
        public static bool TryParseReference(ReadOnlySpan<char> reference, out int column, out int row)
        {
            row = 0;
            int split = 0;
            while (split < reference.Length && char.IsAsciiLetter(reference[split]))
            {
                split++;
            }

            return TryParse(reference[..split], out column)
                && int.TryParse(reference[split..], NumberStyles.None, CultureInfo.InvariantCulture, out row)
                && row is >= 1 and <= MaxRows;
        }

        /// <summary>Computes a column's letters.</summary>
        /// <param name="index">The zero-based index.</param>
        /// <returns>The letters.</returns>
        private static string Compute(int index)
        {
            // Bijective base 26: A..Z, then AA..ZZ, then AAA..XFD.
            Span<char> buffer = stackalloc char[3];
            int position = buffer.Length;
            int value = index + 1;
            while (value > 0)
            {
                int remainder = (value - 1) % 26;
                buffer[--position] = (char)('A' + remainder);
                value = (value - 1) / 26;
            }

            return new string(buffer[position..]);
        }
    }
}
