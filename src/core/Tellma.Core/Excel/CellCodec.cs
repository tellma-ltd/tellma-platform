// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Encodes values into cells by their column's type, and decodes cells into values by the
    ///     mapped property's type. A column is homogeneous: its type, never the
    ///     value, decides how a cell is written, and a cell's number format never decides how it is
    ///     read.
    /// </summary>
    internal static partial class CellCodec
    {
        /// <summary>The most characters an Excel cell holds.</summary>
        public const int MaxCellLength = 32_767;

        /// <summary>The most significant digits a number cell keeps.</summary>
        public const int MaxNumberDigits = 15;

        /// <summary>The ISO 8601 pattern of an editable date and time without an offset.</summary>
        public const string IsoDateTimePattern = "yyyy-MM-ddTHH:mm:ss.FFFFFFF";

        /// <summary>The ISO 8601 pattern of an editable instant, with its offset.</summary>
        public const string IsoDateTimeOffsetPattern = "yyyy-MM-ddTHH:mm:ss.FFFFFFFzzz";

        /// <summary>The ISO 8601 pattern of a date.</summary>
        public const string IsoDatePattern = "yyyy-MM-dd";

        /// <summary>The wire pattern of a concurrency stamp: seven fractional digits and the offset, always.</summary>
        public const string StampPattern = "yyyy-MM-ddTHH:mm:ss.fffffffzzz";

        /// <summary>Encodes a value for its column.</summary>
        /// <param name="value">The value the row source read; null for a blank cell.</param>
        /// <param name="column">The column's encoding.</param>
        /// <returns>The cell.</returns>
        /// <exception cref="LimitExceededException">An editable export's text is longer than a cell holds.</exception>
        public static EncodedCell Encode(object? value, ColumnEncoding column)
        {
            ArgumentNullException.ThrowIfNull(column);
            return value is null or DBNull ? EncodedCell.Blank : EncodeValue(value, column);
        }

        /// <summary>Encodes a value that is not null by its column's kind.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column's encoding.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell EncodeValue(object value, ColumnEncoding column)
        {
            return column.Kind switch
            {
                CellValueKind.Boolean => value is bool b ? EncodedCell.OfBoolean(b) : Text(Invariant(value), column),
                CellValueKind.Integer or CellValueKind.Decimal or CellValueKind.Double => Number(value, column),
                CellValueKind.IntegerText => EncodedCell.OfText(Invariant(value)),
                CellValueKind.Choice => Text(Choice(value, column), column),
                CellValueKind.Date => Date(value, column),
                CellValueKind.DateTime => DateTime(value, column),
                CellValueKind.DateTimeOffset => DateTimeOffset(value, column),
                CellValueKind.Time => Time(value, column),
                CellValueKind.Guid => value is Guid guid ? EncodedCell.OfText(guid.ToString("D")) : Text(Invariant(value), column),
                CellValueKind.Stamp => EncodedCell.OfText(value is DateTimeOffset stamp
                    ? stamp.ToString(StampPattern, CultureInfo.InvariantCulture)
                    : Invariant(value)),
                CellValueKind.Binary => EncodedCell.Blank,
                CellValueKind.Text or CellValueKind.Opaque => Text(Invariant(value), column),
                _ => throw new ArgumentOutOfRangeException(nameof(column), column.Kind, "The column's value kind is not one the codec writes."),
            };
        }

        /// <summary>The significant digits of a decimal: those of its value with trailing fractional zeros dropped.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The digit count; 1 for zero.</returns>
        public static int SignificantDigits(decimal value)
        {
            // The normalised text holds the digits once each: no exponent, no trailing zeros after the point.
            string text = Normalize(value).ToString(CultureInfo.InvariantCulture);
            int digits = 0;
            bool leading = true;
            foreach (char c in text)
            {
                if (c is >= '0' and <= '9')
                {
                    if (leading && c == '0')
                    {
                        continue;
                    }

                    leading = false;
                    digits++;
                }
            }

            return Math.Max(digits, 1);
        }

        /// <summary>A decimal with its trailing fractional zeros dropped.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The same value at the smallest scale that holds it.</returns>
        public static decimal Normalize(decimal value)
        {
            // Dividing by one at the largest scale strips the trailing zeros the scale carries.
            return value / 1.0000000000000000000000000000m;
        }

        /// <summary>Encodes a number: a number cell, or text where a number cell would lose digits an editable export must keep.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell Number(object value, ColumnEncoding column)
        {
            return value switch
            {
                decimal d when column.Editable && SignificantDigits(d) > MaxNumberDigits => EncodedCell.OfText(Normalize(d).ToString(CultureInfo.InvariantCulture)),
                decimal d => EncodedCell.OfNumber(Normalize(d).ToString(CultureInfo.InvariantCulture)),
                double d => Finite(d, column),
                float f => Finite(f, column),
                byte or sbyte or short or ushort or int or uint or long or ulong => EncodedCell.OfNumber(Invariant(value)),
                _ => Text(Invariant(value), column),
            };
        }

        /// <summary>Encodes a binary floating-point number; Excel has no cell for an infinity or a NaN, which are written as text.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell Finite(double value, ColumnEncoding column)
        {
            return double.IsFinite(value)
                ? EncodedCell.OfNumber(value.ToString("R", CultureInfo.InvariantCulture))
                : Text(value.ToString(CultureInfo.InvariantCulture), column);
        }

        /// <summary>The text of a choice: its label in the request culture in a display export, its stored value in an editable one.</summary>
        /// <param name="value">The value: an enum member or a stored string.</param>
        /// <param name="column">The column.</param>
        /// <returns>The text.</returns>
        private static string Choice(object value, ColumnEncoding column)
        {
            string stored = value is Enum member ? member.ToString() : Invariant(value);
            if (column.Editable)
            {
                return stored;
            }

            // A value outside its set is shown as stored, which the label provider does for a value set.
            return column.EnumType is { } enumType
                ? column.Context.Labels.EnumValueLabel(enumType, stored)
                : column.Column.Property?.ValueSet is { } set ? column.Context.Labels.ValueLabel(set, stored) : stored;
        }

        /// <summary>Encodes a date column's value: a date, or the date part of a date and time; anything else as text.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell Date(object value, ColumnEncoding column)
        {
            return value switch
            {
                DateOnly d => Date(d, column),
                DateTime dt => Date(DateOnly.FromDateTime(dt), column),
                DateTimeOffset dto => Date(DateOnly.FromDateTime(dto.DateTime), column),
                _ => Text(Invariant(value), column),
            };
        }

        /// <summary>Encodes a date: a serial, calendar text, or ISO text for a date before 1 March 1900.</summary>
        /// <param name="date">The date.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell Date(DateOnly date, ColumnEncoding column)
        {
            return column.DatesAsText
                ? EncodedCell.OfText(column.Context.Calendar.Format(date, column.Context.Culture, DateStyle.Short))
                : ExcelSerial.TryFrom(date, out int serial)
                ? EncodedCell.OfNumber(serial.ToString(CultureInfo.InvariantCulture))
                : EncodedCell.OfText(date.ToString(IsoDatePattern, CultureInfo.InvariantCulture));
        }

        /// <summary>Encodes a date and time: a serial with its fraction, calendar text, or ISO text.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell DateTime(object value, ColumnEncoding column)
        {
            if (value is not DateTime dateTime)
            {
                return value is DateOnly date ? DateTime(date.ToDateTime(TimeOnly.MinValue), column) : Text(Invariant(value), column);
            }

            if (column.DatesAsText)
            {
                // Editable text is ISO 8601, which round-trips; display text is the calendar's rendering.
                return column.Editable
                    ? EncodedCell.OfText(dateTime.ToString(IsoDateTimePattern, CultureInfo.InvariantCulture))
                    : EncodedCell.OfText(column.Context.Calendar.Format(
                        new DateTimeOffset(System.DateTime.SpecifyKind(dateTime, DateTimeKind.Unspecified), TimeSpan.Zero),
                        TimeZoneInfo.Utc,
                        column.Context.Culture,
                        DateStyle.Short));
            }

            return ExcelSerial.TryFrom(dateTime, out double serial)
                ? EncodedCell.OfNumber(serial.ToString("R", CultureInfo.InvariantCulture))
                : EncodedCell.OfText(dateTime.ToString(IsoDateTimePattern, CultureInfo.InvariantCulture));
        }

        /// <summary>
        ///     Encodes an instant: ISO 8601 text with its offset in an editable export; otherwise the wall
        ///     clock of the display zone, as a serial or calendar text.
        /// </summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell DateTimeOffset(object value, ColumnEncoding column)
        {
            if (value is not DateTimeOffset instant)
            {
                return Text(Invariant(value), column);
            }

            if (column.Editable)
            {
                return EncodedCell.OfText(instant.ToString(IsoDateTimeOffsetPattern, CultureInfo.InvariantCulture));
            }

            if (column.DatesAsText)
            {
                return EncodedCell.OfText(column.Context.Calendar.Format(instant, column.Context.TimeZone, column.Context.Culture, DateStyle.Short));
            }

            DateTime wallClock = TimeZoneInfo.ConvertTime(instant, column.Context.TimeZone).DateTime;
            return ExcelSerial.TryFrom(wallClock, out double serial)
                ? EncodedCell.OfNumber(serial.ToString("R", CultureInfo.InvariantCulture))
                : EncodedCell.OfText(wallClock.ToString(IsoDateTimePattern, CultureInfo.InvariantCulture));
        }

        /// <summary>Encodes a time of day as the fraction of the day.</summary>
        /// <param name="value">The value.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        private static EncodedCell Time(object value, ColumnEncoding column)
        {
            return value switch
            {
                TimeOnly time => EncodedCell.OfNumber(ExcelSerial.From(time).ToString("R", CultureInfo.InvariantCulture)),
                TimeSpan span when span >= TimeSpan.Zero && span < TimeSpan.FromDays(1) =>
                    EncodedCell.OfNumber(ExcelSerial.From(TimeOnly.FromTimeSpan(span)).ToString("R", CultureInfo.InvariantCulture)),
                _ => Text(Invariant(value), column),
            };
        }

        /// <summary>Encodes text, which a cell holds up to 32,767 characters of.</summary>
        /// <param name="text">The text.</param>
        /// <param name="column">The column.</param>
        /// <returns>The cell.</returns>
        /// <exception cref="LimitExceededException">An editable export's text is longer than a cell holds.</exception>
        private static EncodedCell Text(string text, ColumnEncoding column)
        {
            if (text.Length <= MaxCellLength)
            {
                return EncodedCell.OfText(text);
            }

            // An editable export must round-trip, so it never truncates; a display export shows what fits.
            return column.Editable
                ? throw new LimitExceededException(ExcelErrorCodes.ExportCellTooLong, text.Length, MaxCellLength)
                : EncodedCell.OfText(Cut(text, MaxCellLength));
        }

        /// <summary>A value's invariant text.</summary>
        /// <param name="value">The value.</param>
        /// <returns>The text.</returns>
        private static string Invariant(object value)
        {
            return value switch
            {
                string s => s,
                IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
                _ => value.ToString() ?? string.Empty,
            };
        }

        /// <summary>Cuts text to a length without splitting a surrogate pair.</summary>
        /// <param name="text">The text.</param>
        /// <param name="length">The most characters kept.</param>
        /// <returns>The text, cut.</returns>
        private static string Cut(string text, int length)
        {
            return text.Length <= length ? text : char.IsHighSurrogate(text[length - 1]) ? text[..(length - 1)] : text[..length];
        }
    }
}
