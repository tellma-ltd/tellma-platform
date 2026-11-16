// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;
using System.Text;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Reads a stored cell by its kind: a shared string through the workbook's
    ///     string table, inline and rich text joined, a formula by its cached value, booleans,
    ///     numbers, ISO dates and errors as they are.
    /// </summary>
    internal static class CellReader
    {
        /// <summary>Reads a cell.</summary>
        /// <param name="cell">The cell element.</param>
        /// <param name="sharedString">The workbook's shared string at an index; null for an index the table lacks.</param>
        /// <returns>The cell's kind and text.</returns>
        /// <exception cref="FormatException">A shared string cell names no string of the table.</exception>
        public static ExcelRawCell Read(Cell cell, Func<int, string?> sharedString)
        {
            ArgumentNullException.ThrowIfNull(cell);
            ArgumentNullException.ThrowIfNull(sharedString);

            CellValues type = cell.DataType?.Value ?? CellValues.Number;
            string? value = cell.CellValue?.Text;

            // An inline string carries its text in the cell; any other kind in its value.
            if (type == CellValues.InlineString)
            {
                return cell.InlineString is { } inline ? new ExcelRawCell(ExcelCellKind.Text, TextOf(inline)) : ExcelRawCell.Blank;
            }

            // A formula without a cached value has no value to import.
            if (value is null || (value.Length == 0 && type != CellValues.String))
            {
                return ExcelRawCell.Blank;
            }

            if (type == CellValues.SharedString)
            {
                bool valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index);
                string? text = valid ? sharedString(index) : null;
                return text is null
                    ? throw new FormatException("A shared string cell names no string of the workbook's table.")
                    : new ExcelRawCell(ExcelCellKind.Text, text);
            }

            return type switch
            {
                _ when type == CellValues.String => new ExcelRawCell(ExcelCellKind.Text, XmlStrings.Unescape(value)),
                _ when type == CellValues.Boolean => new ExcelRawCell(ExcelCellKind.Boolean, value),
                _ when type == CellValues.Date => new ExcelRawCell(ExcelCellKind.Date, value),
                _ when type == CellValues.Error => new ExcelRawCell(ExcelCellKind.Error, value),
                _ => new ExcelRawCell(ExcelCellKind.Number, value),
            };
        }

        /// <summary>
        ///     The text of a string item, inline or shared: its plain text, or its rich text runs joined;
        ///     phonetic runs are skipped and escapes undone.
        /// </summary>
        /// <param name="item">The item: an inline string or a shared string item.</param>
        /// <returns>The text.</returns>
        public static string TextOf(OpenXmlElement item)
        {
            ArgumentNullException.ThrowIfNull(item);
            StringBuilder builder = new();
            foreach (OpenXmlElement child in item.ChildElements)
            {
                // A plain item holds one t; a rich one holds runs, each with its t; rPh is a reading aid.
                switch (child)
                {
                    case Text text:
                        _ = builder.Append(text.Text);
                        break;
                    case Run run when run.Text is { } runText:
                        _ = builder.Append(runText.Text);
                        break;
                    default:
                        break;
                }
            }

            return XmlStrings.Unescape(builder.ToString());
        }
    }
}
