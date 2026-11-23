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
        /// <summary>The type of a shared string cell, <c>t="s"</c>.</summary>
        public const string SharedStringType = "s";

        /// <summary>The type of a formula's cached string, <c>t="str"</c>.</summary>
        public const string StringType = "str";

        /// <summary>The type of an inline string, <c>t="inlineStr"</c>.</summary>
        public const string InlineStringType = "inlineStr";

        /// <summary>The type of a boolean, <c>t="b"</c>.</summary>
        public const string BooleanType = "b";

        /// <summary>The type of an ISO 8601 date, <c>t="d"</c>.</summary>
        public const string DateType = "d";

        /// <summary>The type of an error value, <c>t="e"</c>.</summary>
        public const string ErrorType = "e";

        /// <summary>Reads a cell.</summary>
        /// <param name="cell">The cell element.</param>
        /// <param name="sharedString">The workbook's shared string at an index; null for an index the table lacks.</param>
        /// <returns>The cell's kind and text.</returns>
        /// <exception cref="FormatException">A shared string cell names no string of the table.</exception>
        public static ExcelRawCell Read(Cell cell, Func<int, string?> sharedString)
        {
            ArgumentNullException.ThrowIfNull(cell);
            ArgumentNullException.ThrowIfNull(sharedString);

            // The type attribute as stored, so the document and the streaming reader share one rule.
            string? type = cell.DataType?.InnerText;
            string? inline = type == InlineStringType && cell.InlineString is { } item ? TextOf(item) : null;
            return Read(type, cell.CellValue?.Text, inline, sharedString);
        }

        /// <summary>
        ///     Reads a stored cell from its parts: the type attribute, the cached value and, for an
        ///     inline string, its text already joined. The streaming reader and the document reader
        ///     both read through here.
        /// </summary>
        /// <param name="type">The cell's <c>t</c> attribute as stored; null for a number.</param>
        /// <param name="value">The cell's <c>v</c> element's text, escapes not yet undone; null when it has none.</param>
        /// <param name="inline">An inline string's text, its runs joined and escapes undone; null when the cell has no <c>is</c> element.</param>
        /// <param name="sharedString">The workbook's shared string at an index; null for an index the table lacks.</param>
        /// <returns>The cell's kind and text.</returns>
        /// <exception cref="FormatException">A shared string cell names no string of the table.</exception>
        public static ExcelRawCell Read(string? type, string? value, string? inline, Func<int, string?> sharedString)
        {
            ArgumentNullException.ThrowIfNull(sharedString);

            // An inline string carries its text in the cell; any other kind in its value.
            if (type == InlineStringType)
            {
                return inline is null ? ExcelRawCell.Blank : new ExcelRawCell(ExcelCellKind.Text, inline);
            }

            // A formula without a cached value has no value to import.
            if (value is null || (value.Length == 0 && type != StringType))
            {
                return ExcelRawCell.Blank;
            }

            if (type == SharedStringType)
            {
                bool valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int index);
                string? text = valid ? sharedString(index) : null;
                return text is null
                    ? throw new FormatException("A shared string cell names no string of the workbook's table.")
                    : new ExcelRawCell(ExcelCellKind.Text, text);
            }

            return type switch
            {
                StringType => new ExcelRawCell(ExcelCellKind.Text, XmlStrings.Unescape(value)),
                BooleanType => new ExcelRawCell(ExcelCellKind.Boolean, value),
                DateType => new ExcelRawCell(ExcelCellKind.Date, value),
                ErrorType => new ExcelRawCell(ExcelCellKind.Error, value),
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
