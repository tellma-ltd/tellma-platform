// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Queryex;

namespace Tellma.Core.Excel
{
    /// <summary>What a column's values are, as the codec encodes and decodes them.</summary>
    internal enum CellValueKind
    {
        /// <summary>A boolean.</summary>
        Boolean,

        /// <summary>An integer written as a number.</summary>
        Integer,

        /// <summary>A 64-bit integer an editable export writes as text, since a number cell keeps 15 digits.</summary>
        IntegerText,

        /// <summary>A decimal.</summary>
        Decimal,

        /// <summary>A binary floating-point number.</summary>
        Double,

        /// <summary>Text.</summary>
        Text,

        /// <summary>A member of an enum or a value set: its label in a display export, its stored value in an editable one.</summary>
        Choice,

        /// <summary>A date.</summary>
        Date,

        /// <summary>A date and time of day without an offset.</summary>
        DateTime,

        /// <summary>An instant with an offset.</summary>
        DateTimeOffset,

        /// <summary>A time of day.</summary>
        Time,

        /// <summary>A GUID, written as text.</summary>
        Guid,

        /// <summary>A value only a display export carries, as its text: a hierarchy path, a geography, JSON.</summary>
        Opaque,

        /// <summary>Binary data, which no export carries: the cell is blank.</summary>
        Binary,

        /// <summary>A row's concurrency stamp, written as its wire string.</summary>
        Stamp,
    }

    /// <summary>
    ///     How one column encodes: what its values are, the shape and context it is written in, its
    ///     number format, its width and validation list, and what the manifest records of its dates.
    ///     Resolved once per column, before the first row.
    /// </summary>
    internal sealed class ColumnEncoding
    {
        /// <summary>The longest list a validation formula carries.</summary>
        private const int MaxListLength = 255;

        /// <summary>The widest a column is made, in characters.</summary>
        private const double MaxWidth = 60;

        /// <summary>The narrowest a text column is made, in characters.</summary>
        private const double MinTextWidth = 12;

        /// <summary>Resolves a column's encoding.</summary>
        /// <param name="column">The column.</param>
        /// <param name="shape">The export the column is written in.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="numberFormat">A number format the plan imposes; null for the one the column's type takes.</param>
        /// <param name="headerLength">The longest header text the column carries, in characters.</param>
        private ColumnEncoding(ExcelColumnSpec column, ExportKind shape, ExcelContext context, string? numberFormat, int headerLength)
        {
            Column = column;
            Context = context;
            Editable = shape == ExportKind.ForImport;
            Kind = KindOf(column, Editable);
            Scale = column.Property?.Scale ?? column.Scale;
            EnumType = EnumTypeOf(column);

            // A calendar without a Windows calendar id cannot render serials, so its dates are text.
            DatesAsText = !NumberFormatBuilder.HasWindowsCalendar(context.Calendar.Code);

            // --- The number format, and what the manifest records of a date column ---
            NumberFormat = numberFormat ?? DefaultFormat();
            (DateEncoding, DatePattern) = DateManifest();

            // --- Width: by type, never narrower than the header ---
            Width = Math.Max(TypeWidth(), Math.Min(MaxWidth, headerLength + 2));

            // --- The validation lists of an editable export ---
            ListValues = Editable ? ListOf() : null;
            MaxLength = Editable && Kind == CellValueKind.Text ? column.Property?.MaxLength : null;
        }

        /// <summary>The column.</summary>
        public ExcelColumnSpec Column { get; }

        /// <summary>The request's context.</summary>
        public ExcelContext Context { get; }

        /// <summary>Whether the column belongs to an editable export, whose cells round-trip exactly.</summary>
        public bool Editable { get; }

        /// <summary>What the column's values are.</summary>
        public CellValueKind Kind { get; }

        /// <summary>The scale of a decimal column; null when unknown, which takes the general format.</summary>
        public int? Scale { get; }

        /// <summary>The enum type of an enum column; null otherwise.</summary>
        public Type? EnumType { get; }

        /// <summary>Whether dates and times are written as calendar text rather than serials.</summary>
        public bool DatesAsText { get; }

        /// <summary>The number format of the column's data cells.</summary>
        public string NumberFormat { get; }

        /// <summary>How a date column's cells carry their values; null for any other column.</summary>
        public ExcelDateEncoding? DateEncoding { get; }

        /// <summary>The pattern of a date column's cells, as the manifest records it; null when none applies.</summary>
        public string? DatePattern { get; }

        /// <summary>The column's width, in characters.</summary>
        public double Width { get; }

        /// <summary>The values of the column's validation list; null for none.</summary>
        public IReadOnlyList<string>? ListValues { get; }

        /// <summary>The longest text the column's validation accepts; null for no check.</summary>
        public int? MaxLength { get; }

        /// <summary>Whether the column is hidden: the concurrency stamp.</summary>
        public bool Hidden => Column.Role == ExcelColumnRole.Stamp;

        /// <summary>Resolves a column's encoding.</summary>
        /// <param name="column">The column.</param>
        /// <param name="shape">The export the column is written in.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="numberFormat">A number format the plan imposes; null for the one the column's type takes.</param>
        /// <param name="headerLength">The longest header text the column carries, in characters.</param>
        /// <returns>The encoding.</returns>
        public static ColumnEncoding For(ExcelColumnSpec column, ExportKind shape, ExcelContext context, string? numberFormat, int headerLength)
        {
            ArgumentNullException.ThrowIfNull(column);
            ArgumentNullException.ThrowIfNull(context);
            return new ColumnEncoding(column, shape, context, numberFormat, headerLength);
        }

        /// <summary>What a column's values are: by its property's CLR type when it has one, else by its Queryex type.</summary>
        /// <param name="column">The column.</param>
        /// <param name="editable">Whether the export is editable.</param>
        /// <returns>The kind.</returns>
        private static CellValueKind KindOf(ExcelColumnSpec column, bool editable)
        {
            if (column.Role == ExcelColumnRole.Stamp)
            {
                return CellValueKind.Stamp;
            }

            if (column.Property is { } property)
            {
                // Enums and value sets are choices whatever their storage.
                if (property.EnumValues is not null || property.ValueSet is not null || column.Choices is not null)
                {
                    return CellValueKind.Choice;
                }

                Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                return type switch
                {
                    _ when type == typeof(bool) => CellValueKind.Boolean,
                    _ when type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort) || type == typeof(int) => CellValueKind.Integer,
                    _ when type == typeof(long) || type == typeof(uint) || type == typeof(ulong) => editable ? CellValueKind.IntegerText : CellValueKind.Integer,
                    _ when type == typeof(decimal) => CellValueKind.Decimal,
                    _ when type == typeof(double) || type == typeof(float) => CellValueKind.Double,
                    _ when type == typeof(string) => CellValueKind.Text,
                    _ when type == typeof(DateOnly) => CellValueKind.Date,
                    _ when type == typeof(DateTime) => CellValueKind.DateTime,
                    _ when type == typeof(DateTimeOffset) => CellValueKind.DateTimeOffset,
                    _ when type == typeof(TimeOnly) || type == typeof(TimeSpan) => CellValueKind.Time,
                    _ when type == typeof(Guid) => CellValueKind.Guid,
                    _ when type == typeof(byte[]) => CellValueKind.Binary,
                    _ => CellValueKind.Opaque,
                };
            }

            if (column.Choices is not null)
            {
                return CellValueKind.Choice;
            }

            // A computed column is read tolerantly by its Queryex type.
            return column.Type switch
            {
                QueryexType.QxBool => CellValueKind.Boolean,
                QueryexType.QxNumeric => CellValueKind.Decimal,
                QueryexType.QxString => CellValueKind.Text,
                QueryexType.QxGuid => CellValueKind.Guid,
                QueryexType.QxDate => CellValueKind.Date,
                QueryexType.QxDateTime => CellValueKind.DateTime,
                QueryexType.QxDateTimeOffset => CellValueKind.DateTimeOffset,
                QueryexType.QxHierarchyId or QueryexType.QxGeography => CellValueKind.Opaque,
                _ => CellValueKind.Opaque,
            };
        }

        /// <summary>The enum type behind a column, when its property is an enum.</summary>
        /// <param name="column">The column.</param>
        /// <returns>The enum type, or null.</returns>
        private static Type? EnumTypeOf(ExcelColumnSpec column)
        {
            if (column.Property is not { } property)
            {
                return null;
            }

            Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
            return type.IsEnum ? type : null;
        }

        /// <summary>The number format the column's type takes.</summary>
        /// <returns>The format code.</returns>
        private string DefaultFormat()
        {
            return Kind switch
            {
                CellValueKind.Integer => NumberFormatBuilder.Integer(Editable),
                CellValueKind.Decimal => Scale is int scale ? NumberFormatBuilder.Decimal(scale, Editable) : NumberFormatBuilder.General,
                CellValueKind.Double or CellValueKind.Boolean or CellValueKind.Binary => NumberFormatBuilder.General,
                CellValueKind.Date => DatesAsText ? NumberFormatBuilder.Text : NumberFormatBuilder.DateFormat(Context.Culture, Context.Calendar.Code),
                CellValueKind.DateTime => DatesAsText ? NumberFormatBuilder.Text : NumberFormatBuilder.DateTimeFormat(Context.Culture, Context.Calendar.Code),
                CellValueKind.DateTimeOffset => Editable || DatesAsText
                    ? NumberFormatBuilder.Text
                    : NumberFormatBuilder.DateTimeFormat(Context.Culture, Context.Calendar.Code),
                CellValueKind.Time => NumberFormatBuilder.TimeOfDay,
                CellValueKind.IntegerText or CellValueKind.Text or CellValueKind.Choice or CellValueKind.Guid
                    or CellValueKind.Opaque or CellValueKind.Stamp => NumberFormatBuilder.Text,
                _ => NumberFormatBuilder.Text,
            };
        }

        /// <summary>What the manifest records of a date column: its encoding and its pattern.</summary>
        /// <returns>The encoding and pattern; both null for any other column.</returns>
        private (ExcelDateEncoding? Encoding, string? Pattern) DateManifest()
        {
            return Kind switch
            {
                // A text date is the calendar's short rendering, which the importer parses back under the same culture.
                CellValueKind.Date when DatesAsText => (ExcelDateEncoding.Text, ShortTextPattern()),
                CellValueKind.Date => (ExcelDateEncoding.Serial, NumberFormatBuilder.DateCode(Context.Culture)),

                // An editable date and time in a text calendar is ISO 8601, exact whatever the calendar.
                CellValueKind.DateTime when DatesAsText && Editable => (ExcelDateEncoding.Text, CellCodec.IsoDateTimePattern),
                CellValueKind.DateTime when DatesAsText => (ExcelDateEncoding.Text, null),
                CellValueKind.DateTime => (ExcelDateEncoding.Serial, NumberFormatBuilder.DateTimeCode(Context.Culture)),

                // An editable instant is always ISO 8601 with its offset.
                CellValueKind.DateTimeOffset when Editable => (ExcelDateEncoding.Text, null),
                CellValueKind.DateTimeOffset when DatesAsText => (ExcelDateEncoding.Text, null),
                CellValueKind.DateTimeOffset => (ExcelDateEncoding.Serial, NumberFormatBuilder.DateTimeCode(Context.Culture)),
                CellValueKind.Boolean or CellValueKind.Integer or CellValueKind.IntegerText or CellValueKind.Decimal
                    or CellValueKind.Double or CellValueKind.Text or CellValueKind.Choice or CellValueKind.Time
                    or CellValueKind.Guid or CellValueKind.Opaque or CellValueKind.Binary or CellValueKind.Stamp => (null, null),
                _ => (null, null),
            };
        }

        /// <summary>The culture's short date pattern, as the calendar systems render it.</summary>
        /// <returns>The pattern, in the framework's custom format grammar.</returns>
        private string ShortTextPattern()
        {
            return Calendars.CalendarCultureData.For(Context.Culture).Short.ToString();
        }

        /// <summary>The width a column's type takes.</summary>
        /// <returns>The width, in characters.</returns>
        private double TypeWidth()
        {
            return Kind switch
            {
                CellValueKind.Date => 12,
                CellValueKind.DateTime or CellValueKind.DateTimeOffset => 18,
                CellValueKind.Time => 10,
                CellValueKind.Integer or CellValueKind.IntegerText or CellValueKind.Decimal or CellValueKind.Double => 14,
                CellValueKind.Boolean => 10,
                CellValueKind.Guid => 38,
                CellValueKind.Stamp => 34,

                // Text is half its declared length, between 12 and 60; text without one takes a middle width.
                CellValueKind.Text or CellValueKind.Choice or CellValueKind.Opaque or CellValueKind.Binary => TextWidth(),
                _ => TextWidth(),
            };
        }

        /// <summary>The width of a text column: half its declared length, between 12 and 60, or a middle width without one.</summary>
        /// <returns>The width, in characters.</returns>
        private double TextWidth()
        {
            return Column.Property?.MaxLength is int length ? Math.Min(MaxWidth, Math.Max(MinTextWidth, length / 2d)) : 30;
        }

        /// <summary>
        ///     The validation list of an editable column: <c>TRUE,FALSE</c> for a boolean, the stored
        ///     values of a choice when they fit a list formula.
        /// </summary>
        /// <returns>The values, or null for no list.</returns>
        private IReadOnlyList<string>? ListOf()
        {
            if (Kind == CellValueKind.Boolean)
            {
                return ["TRUE", "FALSE"];
            }

            if (Kind != CellValueKind.Choice)
            {
                return null;
            }

            IReadOnlyList<string>? values = Column.Choices ?? Column.Property?.EnumValues;

            // A list formula is the values joined by commas inside quotes: a value holding either breaks it.
            return values is { Count: > 0 }
                && string.Join(',', values).Length <= MaxListLength
                && !values.Any(static v => v.Contains(',', StringComparison.Ordinal) || v.Contains('"', StringComparison.Ordinal))
                ? values
                : null;
        }
    }
}
