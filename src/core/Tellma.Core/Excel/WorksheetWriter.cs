// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;
using System.Globalization;

namespace Tellma.Core.Excel
{
    /// <summary>One column of a worksheet: its width, whether it is hidden, and the style a cell typed into it takes.</summary>
    /// <param name="Width">The width, in characters.</param>
    /// <param name="Hidden">Whether the column is hidden.</param>
    /// <param name="Style">The style index of the column's empty cells.</param>
    internal sealed record SheetColumn(double Width, bool Hidden, uint Style);

    /// <summary>A validation on a column's data cells: a list of values, or a longest text.</summary>
    /// <param name="Column">The column's zero-based index.</param>
    /// <param name="List">The values offered; null for a length check.</param>
    /// <param name="MaxLength">The longest text accepted; null for a list.</param>
    internal sealed record SheetValidation(int Column, IReadOnlyList<string>? List, int? MaxLength);

    /// <summary>How a worksheet is laid out: its columns, frozen pane, direction, filter, protection and validations.</summary>
    /// <param name="Columns">The columns, in order; at least one.</param>
    /// <param name="FrozenRows">The rows above the frozen pane; 0 for none.</param>
    /// <param name="FrozenColumns">The columns left of the frozen pane; 0 for none.</param>
    /// <param name="RightToLeft">Whether the sheet reads right to left.</param>
    /// <param name="Selected">Whether the sheet is the one the workbook opens on.</param>
    /// <param name="Protected">Whether the sheet is protected, without a password, against accidental edits.</param>
    /// <param name="FilterRow">The header row the filter sits on, one-based; null for no filter.</param>
    /// <param name="Validations">The validations on data cells.</param>
    internal sealed record SheetLayout(
        IReadOnlyList<SheetColumn> Columns,
        int FrozenRows,
        int FrozenColumns,
        bool RightToLeft,
        bool Selected,
        bool Protected,
        int? FilterRow,
        IReadOnlyList<SheetValidation> Validations);

    /// <summary>
    ///     Writes one worksheet forward-only through the Open XML SDK's writer: the view and columns,
    ///     then the rows as they come, then the filter, protection and validations once the last row
    ///     is known. Element instances are reused cell after cell, so memory stays flat.
    /// </summary>
    internal sealed class WorksheetWriter : IDisposable
    {
        /// <summary>The namespace of <c>xml:space</c>.</summary>
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";

        /// <summary>The writer.</summary>
        private readonly OpenXmlWriter _writer;

        /// <summary>The layout.</summary>
        private readonly SheetLayout _layout;

        /// <summary>A row element, reused for every row's start tag.</summary>
        private readonly Row _row = new();

        /// <summary>A cell element, reused for every cell's start tag.</summary>
        private readonly Cell _cell = new();

        /// <summary>A cell value element, reused.</summary>
        private readonly CellValue _value = new();

        /// <summary>An inline string element, reused.</summary>
        private readonly InlineString _inline = new();

        /// <summary>A text element, reused.</summary>
        private readonly Text _text = new();

        /// <summary>The attributes of the element being started, reused.</summary>
        private readonly List<OpenXmlAttribute> _attributes = new(3);

        /// <summary>The <c>xml:space="preserve"</c> attribute text with outer white space needs.</summary>
        private readonly OpenXmlAttribute[] _preserve = [new OpenXmlAttribute("xml", "space", XmlNamespace, "preserve")];

        /// <summary>Whether the sheet has been finished.</summary>
        private bool _finished;

        /// <summary>Starts a worksheet: writes everything before its first row.</summary>
        /// <param name="stream">The part's stream; left open.</param>
        /// <param name="layout">The layout.</param>
        public WorksheetWriter(Stream stream, SheetLayout layout)
        {
            ArgumentNullException.ThrowIfNull(stream);
            ArgumentNullException.ThrowIfNull(layout);
            _layout = layout;
            _writer = OpenXmlWriter.Create(stream);
            _writer.WriteStartDocument(true);
            _writer.WriteStartElement(new Worksheet());

            // --- The view: direction, selection, and the frozen pane below the headers and after the frozen columns ---
            _writer.WriteElement(new SheetViews(View(layout)));

            // --- The columns: widths, hidden columns, and the style a typed value takes ---
            Columns columns = new();
            for (int index = 0; index < layout.Columns.Count; index++)
            {
                SheetColumn column = layout.Columns[index];
                columns.Append(new Column
                {
                    Min = (uint)(index + 1),
                    Max = (uint)(index + 1),
                    Width = Math.Round(column.Width + 0.7109375, 4),
                    CustomWidth = true,
                    Style = column.Style == 0 ? null : (UInt32Value)column.Style,
                    Hidden = column.Hidden ? (BooleanValue)true : null,
                });
            }

            _writer.WriteElement(columns);
            _writer.WriteStartElement(new SheetData());
        }

        /// <summary>The number of the last row written; 0 before the first.</summary>
        public int RowNumber { get; private set; }

        /// <summary>Starts the next row.</summary>
        /// <exception cref="InvalidOperationException">The worksheet's rows are all taken.</exception>
        public void StartRow()
        {
            if (RowNumber >= ColumnLetters.MaxRows)
            {
                throw new InvalidOperationException("A worksheet holds 1,048,576 rows.");
            }

            RowNumber++;
            _attributes.Clear();
            _attributes.Add(new OpenXmlAttribute("r", string.Empty, RowNumber.ToString(CultureInfo.InvariantCulture)));
            _writer.WriteStartElement(_row, _attributes);
        }

        /// <summary>Writes a cell of the current row.</summary>
        /// <param name="column">The cell's zero-based column.</param>
        /// <param name="cell">The encoded value.</param>
        /// <param name="style">The cell's style index.</param>
        /// <param name="styleBlank">Whether a blank cell is still written, to carry its style.</param>
        public void WriteCell(int column, EncodedCell cell, uint style, bool styleBlank = false)
        {
            if (cell.Kind == EncodedCellKind.Blank && !(styleBlank && style != 0))
            {
                return;
            }

            // --- The cell's start tag: reference, style and type ---
            _attributes.Clear();
            _attributes.Add(new OpenXmlAttribute("r", string.Empty, ColumnLetters.Reference(column, RowNumber)));
            if (style != 0)
            {
                _attributes.Add(new OpenXmlAttribute("s", string.Empty, style.ToString(CultureInfo.InvariantCulture)));
            }

            switch (cell.Kind)
            {
                case EncodedCellKind.Boolean:
                    _attributes.Add(new OpenXmlAttribute("t", string.Empty, "b"));
                    break;
                case EncodedCellKind.Text:
                    _attributes.Add(new OpenXmlAttribute("t", string.Empty, "inlineStr"));
                    break;
                case EncodedCellKind.Number:
                case EncodedCellKind.Blank:
                default:
                    break;
            }

            _writer.WriteStartElement(_cell, _attributes);

            // --- The value: a v for a number or a boolean, an inline string for text ---
            switch (cell.Kind)
            {
                case EncodedCellKind.Number or EncodedCellKind.Boolean:
                    _writer.WriteStartElement(_value);
                    _writer.WriteString(cell.Text);
                    _writer.WriteEndElement();
                    break;
                case EncodedCellKind.Text:
                    _writer.WriteStartElement(_inline);
                    WriteText(cell.Text);
                    _writer.WriteEndElement();
                    break;
                case EncodedCellKind.Blank:
                default:
                    break;
            }

            _writer.WriteEndElement();
        }

        /// <summary>Ends the current row.</summary>
        public void EndRow()
        {
            _writer.WriteEndElement();
        }

        /// <summary>Ends the rows and writes what follows them: the protection, the filter over the used range, and the validations.</summary>
        public void Finish()
        {
            if (_finished)
            {
                return;
            }

            _finished = true;
            _writer.WriteEndElement();

            // --- The schema orders these after the rows: protection, filter, validations ---
            if (_layout.Protected)
            {
                _writer.WriteElement(new SheetProtection { Sheet = true });
            }

            if (FilterRange() is string filter)
            {
                _writer.WriteElement(new AutoFilter { Reference = filter });
            }

            if (_layout.Validations.Count > 0)
            {
                DataValidations validations = new() { Count = (uint)_layout.Validations.Count };
                foreach (SheetValidation validation in _layout.Validations)
                {
                    validations.Append(Validation(validation));
                }

                _writer.WriteElement(validations);
            }

            _writer.WriteEndElement();
            _writer.Close();
        }

        /// <summary>The filter's range: the header row to the last row, across every column.</summary>
        /// <returns>The range (<c>A1:H101</c>); null for no filter.</returns>
        public string? FilterRange()
        {
            if (_layout.FilterRow is not int first)
            {
                return null;
            }

            int last = Math.Max(first, RowNumber);
            return string.Concat(ColumnLetters.Reference(0, first), ":", ColumnLetters.Reference(_layout.Columns.Count - 1, last));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _writer.Dispose();
        }

        /// <summary>Writes a text element, escaping what XML cannot carry and preserving outer white space.</summary>
        /// <param name="text">The text.</param>
        private void WriteText(string text)
        {
            bool preserve = text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]));
            if (preserve)
            {
                _writer.WriteStartElement(_text, _preserve);
            }
            else
            {
                _writer.WriteStartElement(_text);
            }

            _writer.WriteString(XmlStrings.Escape(text));
            _writer.WriteEndElement();
        }

        /// <summary>The sheet view: its direction and, when anything is frozen, the frozen pane and the selection in it.</summary>
        /// <param name="layout">The layout.</param>
        /// <returns>The view.</returns>
        private static SheetView View(SheetLayout layout)
        {
            SheetView view = new()
            {
                WorkbookViewId = 0,
                RightToLeft = layout.RightToLeft ? (BooleanValue)true : null,
                TabSelected = layout.Selected ? (BooleanValue)true : null,
            };

            if (layout.FrozenRows == 0 && layout.FrozenColumns == 0)
            {
                return view;
            }

            // The active pane is the scrolling one: below the rows, after the columns, or both.
            PaneValues active = (layout.FrozenRows > 0, layout.FrozenColumns > 0) switch
            {
                (true, true) => PaneValues.BottomRight,
                (true, false) => PaneValues.BottomLeft,
                _ => PaneValues.TopRight,
            };
            string topLeft = ColumnLetters.Reference(layout.FrozenColumns, layout.FrozenRows + 1);
            view.Append(
                new Pane
                {
                    HorizontalSplit = layout.FrozenColumns > 0 ? (DoubleValue)layout.FrozenColumns : null,
                    VerticalSplit = layout.FrozenRows > 0 ? (DoubleValue)layout.FrozenRows : null,
                    TopLeftCell = topLeft,
                    ActivePane = active,
                    State = PaneStateValues.Frozen,
                },
                new Selection
                {
                    Pane = active,
                    ActiveCell = topLeft,
                    SequenceOfReferences = new ListValue<StringValue> { InnerText = topLeft },
                });
            return view;
        }

        /// <summary>A data validation over a column's data rows, down to the sheet's last row so added rows keep it.</summary>
        /// <param name="validation">The validation.</param>
        /// <returns>The element.</returns>
        private DataValidation Validation(SheetValidation validation)
        {
            int first = (_layout.FilterRow ?? 1) + 1;
            string range = string.Concat(
                ColumnLetters.Reference(validation.Column, first),
                ":",
                ColumnLetters.Reference(validation.Column, ColumnLetters.MaxRows));

            DataValidation element = new()
            {
                AllowBlank = true,
                ShowErrorMessage = true,
                SequenceOfReferences = new ListValue<StringValue> { InnerText = range },
            };

            if (validation.List is { } list)
            {
                element.Type = DataValidationValues.List;
                element.Append(new Formula1(string.Concat("\"", string.Join(',', list), "\"")));
            }
            else
            {
                element.Type = DataValidationValues.TextLength;
                element.Operator = DataValidationOperatorValues.LessThanOrEqual;
                element.Append(new Formula1(validation.MaxLength!.Value.ToString(CultureInfo.InvariantCulture)));
            }

            return element;
        }
    }
}
