// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Calendars;
using Tellma.Core.Excel;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Calendars;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     What the workbook suites share: request contexts in pinned cultures, label and catalogue
    ///     stand-ins, column builders, and a reader that opens a written workbook, validates it
    ///     against the Office schema and reads its cells back.
    /// </summary>
    internal static class ExcelTestKit
    {
        /// <summary>The instant every test request is stamped with.</summary>
        public static readonly DateTimeOffset Now = new(2026, 11, 16, 9, 30, 0, TimeSpan.Zero);

        /// <summary>The Gregorian calendar.</summary>
        public static readonly GregorianCalendarSystem Gregorian = new();

        /// <summary>The Umm al-Qura calendar.</summary>
        public static readonly UmAlQuraCalendarSystem UmAlQura = new();

        /// <summary>The Ethiopian calendar.</summary>
        public static readonly EthiopianCalendarSystem Ethiopian = new();

        /// <summary>English (United States) with its Gregorian patterns pinned, so no ICU release changes them.</summary>
        public static CultureInfo EnUs { get; } = Pin(new CultureInfo("en-US"), "M/d/yyyy", "/", "h:mm:ss tt");

        /// <summary>Arabic (Saudi Arabia), Gregorian, with its patterns pinned; its short date pattern keeps the raw data's marks.</summary>
        public static CultureInfo ArSa { get; } = Pin(PinnedCultures.ArSa, PinnedCultures.ArSa.DateTimeFormat.ShortDatePattern, PinnedCultures.ArSa.DateTimeFormat.DateSeparator, "h:mm:ss tt");

        /// <summary>Amharic (Ethiopia), Gregorian, with its patterns pinned.</summary>
        public static CultureInfo AmEt { get; } = Pin(PinnedCultures.AmEt, "dd/MM/yyyy", "/", "h:mm:ss tt");

        /// <summary>A request context.</summary>
        /// <param name="culture">The request culture; English (United States) by default.</param>
        /// <param name="calendar">The request calendar; Gregorian by default.</param>
        /// <param name="zone">The display zone; UTC by default.</param>
        /// <returns>The context.</returns>
        public static ExcelContext Context(CultureInfo? culture = null, ICalendarSystem? calendar = null, TimeZoneInfo? zone = null)
        {
            return new ExcelContext(
                TenantId: 7,
                DeploymentId: "deployment-a",
                Now,
                TenantLanguages: ["en", "ar"],
                culture ?? EnUs,
                calendar ?? Gregorian,
                zone ?? TimeZoneInfo.Utc,
                new TestLabels(),
                new UnusedCatalog());
        }

        /// <summary>A computed column of a Queryex type, with no property behind it.</summary>
        /// <param name="type">The type.</param>
        /// <param name="header">The header.</param>
        /// <param name="scale">The scale of a numeric column.</param>
        /// <returns>The column.</returns>
        public static ExcelColumnSpec Computed(QueryexType type, string header = "Value", int? scale = null)
        {
            return new ExcelColumnSpec(header, type, scale, Path: null);
        }

        /// <summary>A column with a property of a CLR type behind it.</summary>
        /// <param name="clrType">The property's type.</param>
        /// <param name="type">The column's Queryex type.</param>
        /// <param name="maxLength">The property's declared length.</param>
        /// <param name="scale">The property's scale.</param>
        /// <param name="enumValues">The stored values of an enum property.</param>
        /// <param name="valueSet">The value set of a value-set property.</param>
        /// <param name="name">The property's name.</param>
        /// <returns>The column.</returns>
        public static ExcelColumnSpec Typed(
            Type clrType,
            QueryexType type,
            int? maxLength = null,
            int? scale = null,
            IReadOnlyList<string>? enumValues = null,
            string? valueSet = null,
            string name = "Value")
        {
            PropertyMetadata property = new(
                name,
                name,
                clrType,
                PropertyOwnership.Editable,
                IsNullable: true,
                IsUnique: false,
                maxLength,
                Precision: scale is null ? null : 19,
                scale,
                enumValues,
                valueSet,
                MultilingualGroup: null,
                IsSearchable: false,
                SearchKind: null,
                ExcludedFromExcel: false,
                IsDefaulted: false,
                Getter: static _ => null,
                Setter: static (_, _) => { });
            return new ExcelColumnSpec(name, type, scale, [name]) { Property = property };
        }

        /// <summary>A column's encoding in a shape and context.</summary>
        /// <param name="column">The column.</param>
        /// <param name="shape">The export's shape.</param>
        /// <param name="context">The context; the default one when null.</param>
        /// <returns>The encoding.</returns>
        public static ColumnEncoding Encoding(ExcelColumnSpec column, ExportKind shape, ExcelContext? context = null)
        {
            return ColumnEncoding.For(column, shape, context ?? Context(), numberFormat: null, column.Header.Length);
        }

        /// <summary>A decoding context over a request's culture, calendar and zone.</summary>
        /// <param name="date1904">Whether the workbook counts from 1904.</param>
        /// <param name="culture">The request culture; English (United States) by default.</param>
        /// <param name="calendar">The request calendar; Gregorian by default.</param>
        /// <param name="zone">The display zone; UTC by default.</param>
        /// <returns>The context.</returns>
        public static CellDecodeContext Decoding(bool date1904 = false, CultureInfo? culture = null, ICalendarSystem? calendar = null, TimeZoneInfo? zone = null)
        {
            return new CellDecodeContext(date1904, culture ?? EnUs, calendar ?? Gregorian, zone ?? TimeZoneInfo.Utc);
        }

        /// <summary>Clones a culture with its calendar Gregorian and its date and time patterns fixed.</summary>
        /// <param name="source">The culture.</param>
        /// <param name="shortDate">The short date pattern.</param>
        /// <param name="separator">The date separator.</param>
        /// <param name="longTime">The long time pattern.</param>
        /// <returns>The read-only clone.</returns>
        private static CultureInfo Pin(CultureInfo source, string shortDate, string separator, string longTime)
        {
            var culture = (CultureInfo)source.Clone();

            // Setting the calendar resets the patterns, so the calendar goes first.
            culture.DateTimeFormat.Calendar = new GregorianCalendar();
            culture.DateTimeFormat.ShortDatePattern = shortDate;
            culture.DateTimeFormat.DateSeparator = separator;
            culture.DateTimeFormat.LongTimePattern = longTime;
            culture.DateTimeFormat.TimeSeparator = ":";
            return CultureInfo.ReadOnly(culture);
        }

        /// <summary>Labels the codec reads: an enum member as <c>[Member]</c>, a value of a set as <c>set:value</c>.</summary>
        private sealed class TestLabels : ILabelProvider
        {
            /// <inheritdoc />
            public string EnumValueLabel(Type enumType, string value)
            {
                return $"[{value}]";
            }

            /// <inheritdoc />
            public string ValueLabel(string set, string value)
            {
                return $"{set}:{value}";
            }

            /// <inheritdoc />
            public string EntityLabel(Type entityType, bool plural = false)
            {
                return entityType.Name;
            }

            /// <inheritdoc />
            public string PropertyLabel(Type entityType, string propertyName)
            {
                return propertyName;
            }

            /// <inheritdoc />
            public string NavigationLabel(Type entityType, string navigation)
            {
                return navigation;
            }

            /// <inheritdoc />
            public string CollectionLabel(Type entityType, string collection)
            {
                return collection;
            }

            /// <inheritdoc />
            public string ActionLabel(Type entityType, string action)
            {
                return action;
            }

            /// <inheritdoc />
            public string SettingLabel(SettingKey key)
            {
                return key.ToString();
            }

            /// <inheritdoc />
            public string? EntityLabelKey(Type entityType, bool plural = false)
            {
                return null;
            }

            /// <inheritdoc />
            public string? PropertyLabelKey(Type entityType, string propertyName)
            {
                return null;
            }

            /// <inheritdoc />
            public string? NavigationLabelKey(Type entityType, string navigation)
            {
                return null;
            }

            /// <inheritdoc />
            public string? CollectionLabelKey(Type entityType, string collection)
            {
                return null;
            }

            /// <inheritdoc />
            public string? ActionLabelKey(Type entityType, string action)
            {
                return null;
            }

            /// <inheritdoc />
            public string? SettingLabelKey(SettingKey key)
            {
                return null;
            }

            /// <inheritdoc />
            public string? EnumValueLabelKey(Type enumType, string value)
            {
                return null;
            }

            /// <inheritdoc />
            public string? ValueLabelKey(string set, string value)
            {
                return null;
            }
        }

        /// <summary>A language catalogue the writer never reads; every member refuses.</summary>
        private sealed class UnusedCatalog : ILanguageCatalog
        {
            /// <inheritdoc />
            public IReadOnlyList<LanguageInfo> Offered => throw new NotSupportedException();

            /// <inheritdoc />
            public IReadOnlyList<LanguageInfo> All => throw new NotSupportedException();

            /// <inheritdoc />
            public LanguageInfo? Find(string code)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public bool IsOffered(string code)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public LanguageFormats Formats(string language)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public bool IsRegionOffered(string region)
            {
                throw new NotSupportedException();
            }
        }
    }

    /// <summary>A written workbook, opened read-only: validated against the Office schema, its sheets and cells read back.</summary>
    /// <remarks>Opens a workbook from its bytes.</remarks>
    /// <param name="bytes">The bytes written.</param>
    internal sealed class WrittenWorkbook(byte[] bytes) : IDisposable
    {
        /// <summary>The package.</summary>
        private readonly SpreadsheetDocument _document = SpreadsheetDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

        /// <summary>The workbook part's root.</summary>
        public Workbook Workbook => _document.WorkbookPart!.Workbook!;

        /// <summary>The stylesheet.</summary>
        public Stylesheet Styles => _document.WorkbookPart!.WorkbookStylesPart!.Stylesheet!;

        /// <summary>The workbook's sheets, in order.</summary>
        public IReadOnlyList<Sheet> Sheets => [.. Workbook.Sheets!.Elements<Sheet>()];

        /// <summary>The schema errors the Office 2019 validator finds.</summary>
        /// <returns>Each error's description, with where it is.</returns>
        public IReadOnlyList<string> ValidationErrors()
        {
            OpenXmlValidator validator = new(FileFormatVersions.Office2019);
            return [.. validator.Validate(_document).Select(static error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")];
        }

        /// <summary>A worksheet by its sheet name.</summary>
        /// <param name="name">The sheet's name.</param>
        /// <returns>The worksheet.</returns>
        public Worksheet Worksheet(string name)
        {
            Sheet sheet = Sheets.Single(s => s.Name == name);
            return ((WorksheetPart)_document.WorkbookPart!.GetPartById(sheet.Id!)).Worksheet!;
        }

        /// <summary>A sheet's rows, each cell read as the importer reads it.</summary>
        /// <param name="name">The sheet's name.</param>
        /// <returns>The rows in order, each cell by its column letters.</returns>
        public IReadOnlyList<IReadOnlyDictionary<string, ExcelRawCell>> Rows(string name)
        {
            return [.. Worksheet(name).GetFirstChild<SheetData>()!.Elements<Row>().Select(static row =>
                (IReadOnlyDictionary<string, ExcelRawCell>)row.Elements<Cell>().ToDictionary(
                    static cell => new string([.. cell.CellReference!.Value!.TakeWhile(char.IsAsciiLetter)]),
                    static cell => CellReader.Read(cell, static _ => null)))];
        }

        /// <summary>A cell's number format code.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The code: a custom entry's, or the built-in id's as <c>#id</c>.</returns>
        public string FormatOf(Cell cell)
        {
            CellFormat format = Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)(cell.StyleIndex?.Value ?? 0));
            uint id = format.NumberFormatId?.Value ?? 0;
            NumberingFormat? custom = Styles.NumberingFormats?.Elements<NumberingFormat>().SingleOrDefault(f => f.NumberFormatId!.Value == id);
            return custom?.FormatCode?.Value ?? string.Create(CultureInfo.InvariantCulture, $"#{id}");
        }

        /// <summary>A cell's style.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The cell format.</returns>
        public CellFormat StyleOf(Cell cell)
        {
            return Styles.CellFormats!.Elements<CellFormat>().ElementAt((int)(cell.StyleIndex?.Value ?? 0));
        }

        /// <summary>A cell of a sheet by its reference.</summary>
        /// <param name="sheet">The sheet's name.</param>
        /// <param name="reference">The cell's reference.</param>
        /// <returns>The cell.</returns>
        public Cell Cell(string sheet, string reference)
        {
            return Worksheet(sheet).Descendants<Cell>().Single(c => c.CellReference == reference);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _document.Dispose();
        }
    }

    /// <summary>A write-only stream that cannot seek, as a response body cannot: it counts what it is given and keeps it when asked.</summary>
    /// <param name="keep">Whether the bytes are kept for reading back.</param>
    internal sealed class CountingStream(bool keep = false) : Stream
    {
        /// <summary>The bytes kept.</summary>
        private readonly MemoryStream _kept = new();

        /// <summary>The bytes written.</summary>
        private long _length;

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => _length;

        /// <inheritdoc />
        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        /// <summary>The bytes kept.</summary>
        /// <returns>A copy of them.</returns>
        public byte[] ToArray()
        {
            return keep ? _kept.ToArray() : throw new InvalidOperationException("The stream keeps no bytes.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            _length += count;
            if (keep)
            {
                _kept.Write(buffer, offset, count);
            }
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _kept.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>
    ///     A write-only stream that cannot seek and takes a budget of bytes, then refuses every write
    ///     with a new exception, as a full volume does; it records the first it threw.
    /// </summary>
    /// <param name="budget">The bytes accepted before the first refusal.</param>
    internal sealed class FailingStream(long budget) : Stream
    {
        /// <summary>The bytes accepted.</summary>
        private long _length;

        /// <summary>The first exception thrown; null until then.</summary>
        public IOException? FirstFault { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => false;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => true;

        /// <inheritdoc />
        public override long Length => _length;

        /// <inheritdoc />
        public override long Position
        {
            get => _length;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (_length + count > budget)
            {
                IOException fault = new("There is not enough space on the volume.");
                FirstFault ??= fault;
                throw fault;
            }

            _length += count;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }
    }
}
