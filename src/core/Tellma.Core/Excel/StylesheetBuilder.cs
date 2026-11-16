// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml.Spreadsheet;

namespace Tellma.Core.Excel
{
    /// <summary>The font a cell style uses.</summary>
    internal enum CellFont
    {
        /// <summary>Regular.</summary>
        Regular,

        /// <summary>Bold: headers and the stronger subtotals.</summary>
        Bold,

        /// <summary>Italic: a pivot's phantom rows.</summary>
        Italic,
    }

    /// <summary>The fill a cell style uses.</summary>
    internal enum CellFill
    {
        /// <summary>No fill.</summary>
        None,

        /// <summary>The lightest grey: a first-level subtotal.</summary>
        Lightest,
    }

    /// <summary>The border a cell style uses.</summary>
    internal enum CellBorder
    {
        /// <summary>No border.</summary>
        None,

        /// <summary>A double line along the top: the grand total.</summary>
        DoubleTop,
    }

    /// <summary>One cell style: its number format, font, fill, border and indent.</summary>
    /// <param name="NumberFormat">The number format code.</param>
    /// <param name="Font">The font.</param>
    /// <param name="Fill">The fill.</param>
    /// <param name="Border">The border.</param>
    /// <param name="Indent">The alignment indent, in Excel's steps; 0 for none.</param>
    /// <param name="RightToLeft">Whether an indent is from the right edge, as on a right-to-left sheet.</param>
    internal readonly record struct ExcelCellStyle(
        string NumberFormat,
        CellFont Font = CellFont.Regular,
        CellFill Fill = CellFill.None,
        CellBorder Border = CellBorder.None,
        int Indent = 0,
        bool RightToLeft = false);

    /// <summary>
    ///     The workbook's styles, registered as cells need them: each distinct number format once,
    ///     each distinct style once, so a million cells share a handful of entries.
    /// </summary>
    internal sealed class StylesheetBuilder
    {
        /// <summary>The colour of the lightest fill.</summary>
        private const string LightestFill = "FFF2F2F2";

        /// <summary>The style index of each style registered, in registration order.</summary>
        private readonly Dictionary<ExcelCellStyle, uint> _styles = [];

        /// <summary>The custom number formats registered, by code.</summary>
        private readonly Dictionary<string, uint> _customFormats = new(StringComparer.Ordinal);

        /// <summary>The styles in index order.</summary>
        private readonly List<ExcelCellStyle> _ordered = [];

        /// <summary>Creates the builder, with the general regular style at index 0, which Excel applies to unstyled cells.</summary>
        public StylesheetBuilder()
        {
            _ = Register(new ExcelCellStyle(NumberFormatBuilder.General));
        }

        /// <summary>The style index of a style, registered on first use.</summary>
        /// <param name="style">The style.</param>
        /// <returns>Its index in the workbook's cell formats.</returns>
        public uint Register(ExcelCellStyle style)
        {
            ArgumentNullException.ThrowIfNull(style.NumberFormat);
            if (_styles.TryGetValue(style, out uint index))
            {
                return index;
            }

            if (NumberFormatBuilder.BuiltInId(style.NumberFormat) is null && !_customFormats.ContainsKey(style.NumberFormat))
            {
                _customFormats.Add(style.NumberFormat, (uint)(NumberFormatBuilder.FirstCustomId + _customFormats.Count));
            }

            index = (uint)_ordered.Count;
            _styles.Add(style, index);
            _ordered.Add(style);
            return index;
        }

        /// <summary>Builds the stylesheet part's root element.</summary>
        /// <returns>The stylesheet.</returns>
        public Stylesheet Build()
        {
            // --- Number formats: the custom ones, in id order ---
            NumberingFormats formats = new() { Count = (uint)_customFormats.Count };
            foreach ((string code, uint id) in _customFormats.OrderBy(static entry => entry.Value))
            {
                formats.Append(new NumberingFormat { NumberFormatId = id, FormatCode = code });
            }

            // --- Fonts, fills and borders, indexed by their enum values ---
            Fonts fonts = new(MakeFont(bold: false, italic: false), MakeFont(bold: true, italic: false), MakeFont(bold: false, italic: true)) { Count = 3 };
            Fills fills = new(
                new Fill(new PatternFill { PatternType = PatternValues.None }),
                new Fill(new PatternFill { PatternType = PatternValues.Gray125 }),
                new Fill(new PatternFill(new ForegroundColor { Rgb = LightestFill }, new BackgroundColor { Indexed = 64 }) { PatternType = PatternValues.Solid }))
            { Count = 3 };
            Borders borders = new(
                MakeBorder(top: null),
                MakeBorder(top: BorderStyleValues.Double))
            { Count = 2 };

            // --- Cell formats, one per registered style ---
            CellFormats cellFormats = new() { Count = (uint)_ordered.Count };
            foreach (ExcelCellStyle style in _ordered)
            {
                cellFormats.Append(Format(style));
            }

            // Excel expects the master style and the Normal cell style the formats derive from.
            Stylesheet stylesheet = new();
            if (_customFormats.Count > 0)
            {
                stylesheet.Append(formats);
            }

            stylesheet.Append(
                fonts,
                fills,
                borders,
                new CellStyleFormats(new CellFormat { NumberFormatId = 0, FontId = 0, FillId = 0, BorderId = 0 }) { Count = 1 },
                cellFormats,
                new CellStyles(new CellStyle { Name = "Normal", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
            return stylesheet;
        }

        /// <summary>The cell format of a style.</summary>
        /// <param name="style">The style.</param>
        /// <returns>The cell format.</returns>
        private CellFormat Format(ExcelCellStyle style)
        {
            uint formatId = NumberFormatBuilder.BuiltInId(style.NumberFormat) is int builtIn ? (uint)builtIn : _customFormats[style.NumberFormat];
            CellFormat format = new()
            {
                NumberFormatId = formatId,
                FontId = (uint)style.Font,
                FillId = style.Fill == CellFill.Lightest ? 2u : 0u,
                BorderId = (uint)style.Border,
                FormatId = 0,
                ApplyNumberFormat = formatId != 0,
                ApplyFont = style.Font != CellFont.Regular,
                ApplyFill = style.Fill != CellFill.None,
                ApplyBorder = style.Border != CellBorder.None,
            };

            // An indent is measured from the edge the text starts at: the right edge on a right-to-left sheet.
            if (style.Indent > 0)
            {
                format.ApplyAlignment = true;
                format.Append(new Alignment
                {
                    Horizontal = style.RightToLeft ? HorizontalAlignmentValues.Right : HorizontalAlignmentValues.Left,
                    Indent = (uint)style.Indent,
                });
            }

            return format;
        }

        /// <summary>A font: Calibri 11, bold or italic as asked.</summary>
        /// <param name="bold">Whether bold.</param>
        /// <param name="italic">Whether italic.</param>
        /// <returns>The font.</returns>
        private static Font MakeFont(bool bold, bool italic)
        {
            // The schema orders the font's children: weight and slant before size, name and family.
            Font font = new();
            if (bold)
            {
                font.Append(new Bold());
            }

            if (italic)
            {
                font.Append(new Italic());
            }

            font.Append(new FontSize { Val = 11 }, new FontName { Val = "Calibri" }, new FontFamilyNumbering { Val = 2 });
            return font;
        }

        /// <summary>A border with an optional top line.</summary>
        /// <param name="top">The top line's style; null for none.</param>
        /// <returns>The border.</returns>
        private static Border MakeBorder(BorderStyleValues? top)
        {
            TopBorder topBorder = new();
            if (top is BorderStyleValues style)
            {
                topBorder.Style = style;
                topBorder.Append(new Color { Auto = true });
            }

            return new Border(new LeftBorder(), new RightBorder(), topBorder, new BottomBorder(), new DiagonalBorder());
        }
    }
}
