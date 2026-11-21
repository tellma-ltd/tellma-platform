// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Excel
{
    /// <content>Decoding: a stored cell to a value of the mapped property's type.</content>
    internal static partial class CellCodec
    {
        /// <summary>The largest integer a double holds exactly: beyond it a number cell has lost digits.</summary>
        private const double MaxExactInteger = 9_007_199_254_740_992d;

        /// <summary>The ISO 8601 forms a date and time without an offset is read in.</summary>
        private static readonly string[] IsoDateTimeForms =
        [
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm",
            "yyyy-MM-dd HH:mm:ss.FFFFFFF",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd HH:mm",
            "yyyy-MM-dd",
        ];

        /// <summary>The ISO 8601 forms an instant with an offset is read in.</summary>
        private static readonly string[] IsoOffsetForms =
        [
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFFzzz",
            "yyyy-MM-dd'T'HH:mm:sszzz",
            "yyyy-MM-dd'T'HH:mmzzz",
            "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'",
            "yyyy-MM-dd'T'HH:mm:ss'Z'",
            "yyyy-MM-dd'T'HH:mm'Z'",
        ];

        /// <summary>The forms a time of day is read in.</summary>
        private static readonly string[] TimeForms = ["H:mm", "H:mm:ss", "H:mm:ss.FFFFFFF"];

        /// <summary>The words a boolean is read from, whatever the culture.</summary>
        private static readonly Dictionary<string, bool> BooleanWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["true"] = true,
            ["false"] = false,
            ["yes"] = true,
            ["no"] = false,
            ["1"] = true,
            ["0"] = false,
        };

        /// <summary>Decodes a stored cell into a value of the target's type.</summary>
        /// <param name="cell">The cell, read.</param>
        /// <param name="target">What the cell decodes into.</param>
        /// <param name="context">The workbook and request facts.</param>
        /// <returns>The value (null for a blank cell), or why the cell was refused.</returns>
        public static CellDecodeResult Decode(ExcelRawCell cell, CellDecodeTarget target, CellDecodeContext context)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(context);
            Type type = Nullable.GetUnderlyingType(target.ClrType) ?? target.ClrType;

            // --- Blank and error cells ---
            if (cell.Kind == ExcelCellKind.Blank)
            {
                return CellDecodeResult.Of(null);
            }

            if (cell.Kind == ExcelCellKind.Error)
            {
                return CellDecodeResult.Refused(ExpectedOf(type, target), cell.Text);
            }

            // --- Text keeps its white space only where the property asks; any other type reads trimmed text ---
            if (type == typeof(string) && target.Choices is null)
            {
                string text = AsText(cell);
                text = target.PreserveWhitespace ? text : text.Trim();
                return CellDecodeResult.Of(text.Length == 0 ? null : text);
            }

            if (cell.Kind is ExcelCellKind.Text or ExcelCellKind.Date && string.IsNullOrWhiteSpace(cell.Text))
            {
                return CellDecodeResult.Of(null);
            }

            // --- Each type its own reading ---
            object? value = type switch
            {
                _ when type.IsEnum || target.Choices is not null => DecodeChoice(cell, type, target),
                _ when type == typeof(bool) => DecodeBoolean(cell, target),
                _ when type == typeof(decimal) => DecodeDecimal(cell, target),
                _ when type == typeof(double) => DecodeDouble(cell),
                _ when type == typeof(float) => DecodeDouble(cell) is double d && Math.Abs(d) <= float.MaxValue ? (float)d : null,
                _ when IsInteger(type) => DecodeInteger(cell, type),
                _ when type == typeof(DateOnly) => DecodeDate(cell, target, context),
                _ when type == typeof(DateTime) => DecodeDateTime(cell, target, context),
                _ when type == typeof(DateTimeOffset) => DecodeDateTimeOffset(cell, target, context),
                _ when type == typeof(TimeOnly) => DecodeTime(cell),
                _ when type == typeof(TimeSpan) => DecodeTime(cell) is TimeOnly t ? t.ToTimeSpan() : null,
                _ when type == typeof(Guid) => DecodeGuid(cell),
                _ => null,
            };

            return value is null ? CellDecodeResult.Refused(ExpectedOf(type, target), cell.Text) : CellDecodeResult.Of(value);
        }

        /// <summary>A cell's text as a string column reads it: a number in its invariant form, a boolean as <c>TRUE</c> or <c>FALSE</c>.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The text.</returns>
        public static string AsText(ExcelRawCell cell)
        {
            return cell.Kind switch
            {
                ExcelCellKind.Number => RenderNumber(cell.Text),
                ExcelCellKind.Boolean => IsTrue(cell.Text) ? "TRUE" : "FALSE",
                ExcelCellKind.Blank or ExcelCellKind.Text or ExcelCellKind.Date or ExcelCellKind.Error => cell.Text,
                _ => cell.Text,
            };
        }

        /// <summary>The expected-value name of a type.</summary>
        /// <param name="type">The type, nullable unwrapped.</param>
        /// <param name="target">The target.</param>
        /// <returns>The name.</returns>
        private static string ExpectedOf(Type type, CellDecodeTarget target)
        {
            return type switch
            {
                _ when type.IsEnum || target.Choices is not null => ExpectedValues.Choice,
                _ when type == typeof(bool) => ExpectedValues.Bool,
                _ when type == typeof(decimal) || type == typeof(double) || type == typeof(float) => ExpectedValues.Numeric,
                _ when IsInteger(type) => ExpectedValues.Integer,
                _ when type == typeof(DateOnly) => ExpectedValues.Date,
                _ when type == typeof(DateTime) => ExpectedValues.DateTime,
                _ when type == typeof(DateTimeOffset) => ExpectedValues.DateTimeOffset,
                _ when type == typeof(TimeOnly) || type == typeof(TimeSpan) => ExpectedValues.Time,
                _ when type == typeof(Guid) => ExpectedValues.Guid,
                _ => ExpectedValues.String,
            };
        }

        /// <summary>Whether a type is one of the integer types.</summary>
        /// <param name="type">The type.</param>
        /// <returns>True for the eight integer types.</returns>
        private static bool IsInteger(Type type)
        {
            return type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
                || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong);
        }

        /// <summary>
        ///     Reads a choice: its stored value ignoring case, else any of its labels. A number cell is
        ///     read as its text.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="type">The property's type: an enum, or a string for a value set.</param>
        /// <param name="target">The target.</param>
        /// <returns>The enum member or stored string; null when nothing matches.</returns>
        private static object? DecodeChoice(ExcelRawCell cell, Type type, CellDecodeTarget target)
        {
            string text = AsText(cell).Trim();
            IEnumerable<string> stored = type.IsEnum ? Enum.GetNames(type) : target.Choices ?? [];
            string? match = stored.FirstOrDefault(value => string.Equals(value, text, StringComparison.OrdinalIgnoreCase));
            if (match is null && target.ChoiceLabels is not null)
            {
                match = target.ChoiceLabels.FirstOrDefault(entry => string.Equals(entry.Key, text, StringComparison.OrdinalIgnoreCase)).Value;
            }

            return match is null ? null : type.IsEnum ? Enum.Parse(type, match) : match;
        }

        /// <summary>Reads a boolean: a boolean cell, 0 or 1, or the words true, false, yes, no, 1, 0 and any localized label.</summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <returns>The boolean; null when the cell holds none.</returns>
        private static object? DecodeBoolean(ExcelRawCell cell, CellDecodeTarget target)
        {
            switch (cell.Kind)
            {
                case ExcelCellKind.Boolean:
                    return IsTrue(cell.Text);
                case ExcelCellKind.Number:
                    return ParseDouble(cell.Text) switch
                    {
                        1d => true,
                        0d => false,
                        _ => null,
                    };
                case ExcelCellKind.Blank:
                case ExcelCellKind.Text:
                case ExcelCellKind.Date:
                case ExcelCellKind.Error:
                default:
                    string text = cell.Text.Trim();
                    if (BooleanWords.TryGetValue(text, out bool word))
                    {
                        return word;
                    }

                    return target.BooleanLabels?.FirstOrDefault(entry => string.Equals(entry.Key, text, StringComparison.OrdinalIgnoreCase)) is { Key: not null } label
                        ? label.Value
                        : null;
            }
        }

        /// <summary>
        ///     Reads a decimal: a number cell rounded to the 15 significant digits it holds, or text read
        ///     invariant without thousands separators; more decimal places than the scale are refused.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <returns>The decimal; null when the cell holds none.</returns>
        private static decimal? DecodeDecimal(ExcelRawCell cell, CellDecodeTarget target)
        {
            decimal value;
            if (cell.Kind == ExcelCellKind.Number)
            {
                if (ParseDouble(cell.Text) is not double number || Math.Abs(number) >= (double)decimal.MaxValue)
                {
                    return null;
                }

                // The conversion keeps 15 significant digits, the most a number cell holds, so the
                // binary noise of a stored 0.1 never reads as a sixteenth digit.
                value = Normalize((decimal)number);
            }
            else if (cell.Kind == ExcelCellKind.Text && decimal.TryParse(
                cell.Text.Trim(),
                NumberStyles.Number & ~NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out decimal parsed))
            {
                value = Normalize(parsed);
            }
            else
            {
                return null;
            }

            return target.Scale is int scale && value.Scale > scale ? null : value;
        }

        /// <summary>Reads a binary floating-point number from a number cell or invariant text.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The number; null when the cell holds none.</returns>
        private static double? DecodeDouble(ExcelRawCell cell)
        {
            return cell.Kind is ExcelCellKind.Number or ExcelCellKind.Text ? ParseDouble(cell.Text.Trim()) : null;
        }

        /// <summary>
        ///     Reads an integer: a number cell holding a whole number a double keeps exactly, or integral
        ///     text read invariant; either within the type's range.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="type">The integer type.</param>
        /// <returns>The integer, boxed as the type; null when the cell holds none.</returns>
        private static object? DecodeInteger(ExcelRawCell cell, Type type)
        {
            decimal value;
            if (cell.Kind == ExcelCellKind.Number)
            {
                if (ParseDouble(cell.Text) is not double number || number != Math.Floor(number) || Math.Abs(number) > MaxExactInteger)
                {
                    return null;
                }

                value = (decimal)number;
            }
            else if (cell.Kind == ExcelCellKind.Text && decimal.TryParse(cell.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out decimal parsed))
            {
                value = parsed;
            }
            else
            {
                return null;
            }

            // The range check is the conversion's: an overflow is a value the type cannot hold.
            try
            {
                return Convert.ChangeType(value, type, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Reads a date: a whole serial, ISO 8601 text (a date and time at midnight included), text
        ///     in the column's manifest calendar and culture, else text in the request calendar's short
        ///     pattern.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <param name="context">The context.</param>
        /// <returns>The date; null when the cell holds none.</returns>
        private static object? DecodeDate(ExcelRawCell cell, CellDecodeTarget target, CellDecodeContext context)
        {
            if (cell.Kind == ExcelCellKind.Number)
            {
                return ParseDouble(cell.Text) is double serial && ExcelSerial.TryReadDate(serial, context.Date1904, out DateOnly date) ? date : null;
            }

            // An ISO date, or an ISO date and time at midnight as a t="d" cell stores a date.
            string text = cell.Text.Trim();
            return DateOnly.TryParseExact(text, IsoDatePattern, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly iso)
                ? iso
                : System.DateTime.TryParseExact(text, IsoDateTimeForms, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime midnight)
                    && midnight.TimeOfDay == TimeSpan.Zero
                ? DateOnly.FromDateTime(midnight)
                : (object?)CalendarDate(text, target, context);
        }

        /// <summary>
        ///     Reads a date and time: a serial with its fraction, ISO 8601 text without an offset, or a
        ///     calendar date at midnight.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <param name="context">The context.</param>
        /// <returns>The date and time, of unspecified kind; null when the cell holds none.</returns>
        private static DateTime? DecodeDateTime(ExcelRawCell cell, CellDecodeTarget target, CellDecodeContext context)
        {
            if (cell.Kind == ExcelCellKind.Number)
            {
                return ParseDouble(cell.Text) is double serial && ExcelSerial.TryReadDateTime(serial, context.Date1904, out DateTime value) ? value : null;
            }

            string text = cell.Text.Trim();
            return System.DateTime.TryParseExact(text, IsoDateTimeForms, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime iso)
                ? iso
                : CalendarDate(text, target, context) is DateOnly date ? date.ToDateTime(TimeOnly.MinValue) : null;
        }

        /// <summary>
        ///     Reads an instant: ISO 8601 text with an offset; otherwise a wall clock in the display zone,
        ///     from a serial, ISO text without an offset, or a calendar date at midnight. A wall clock the
        ///     zone skips is refused.
        /// </summary>
        /// <param name="cell">The cell.</param>
        /// <param name="target">The target.</param>
        /// <param name="context">The context.</param>
        /// <returns>The instant; null when the cell holds none.</returns>
        private static object? DecodeDateTimeOffset(ExcelRawCell cell, CellDecodeTarget target, CellDecodeContext context)
        {
            string text = cell.Text.Trim();
            return cell.Kind != ExcelCellKind.Number && System.DateTimeOffset.TryParseExact(
                text,
                IsoOffsetForms,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal,
                out DateTimeOffset withOffset)
                ? withOffset
                : (object?)(DecodeDateTime(cell, target, context) is DateTime wallClock ? InZone(wallClock, context.TimeZone) : null);
        }

        /// <summary>Reads a time of day: a fraction of a day, or <c>HH:mm[:ss]</c> text.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The time; null when the cell holds none.</returns>
        private static TimeOnly? DecodeTime(ExcelRawCell cell)
        {
            return cell.Kind == ExcelCellKind.Number
                ? ParseDouble(cell.Text) is double serial && ExcelSerial.TryReadTime(serial, out TimeOnly time) ? time : null
                : TimeOnly.TryParseExact(cell.Text.Trim(), TimeForms, CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly parsed) ? parsed : null;
        }

        /// <summary>Reads a GUID in its <c>D</c> form, with or without braces.</summary>
        /// <param name="cell">The cell.</param>
        /// <returns>The GUID; null when the cell holds none.</returns>
        private static Guid? DecodeGuid(ExcelRawCell cell)
        {
            string text = cell.Text.Trim();
            return cell.Kind == ExcelCellKind.Text
                && (Guid.TryParseExact(text, "D", out Guid guid) || Guid.TryParseExact(text, "B", out guid))
                ? guid
                : null;
        }

        /// <summary>Reads text as a date: in the column's manifest calendar and culture, else in the request's.</summary>
        /// <param name="text">The text.</param>
        /// <param name="target">The target.</param>
        /// <param name="context">The context.</param>
        /// <returns>The date; null when the text is none.</returns>
        private static DateOnly? CalendarDate(string text, CellDecodeTarget target, CellDecodeContext context)
        {
            return target.TextCalendar is { } calendar && calendar.TryParse(text, target.TextCulture ?? context.Culture) is DateOnly manifestDate
                ? manifestDate
                : context.Calendar.TryParse(text, context.Culture);
        }

        /// <summary>A wall clock in a zone as an instant; null for a wall clock the zone skips.</summary>
        /// <param name="wallClock">The wall clock.</param>
        /// <param name="zone">The zone.</param>
        /// <returns>The instant, or null.</returns>
        private static DateTimeOffset? InZone(DateTime wallClock, TimeZoneInfo zone)
        {
            var unspecified = System.DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified);
            return zone.IsInvalidTime(unspecified) ? null : new System.DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified));
        }

        /// <summary>Reads an invariant number.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The number; null when the text is none.</returns>
        private static double? ParseDouble(string text)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value) ? value : null;
        }

        /// <summary>Whether a boolean cell's text is true.</summary>
        /// <param name="text">The text: <c>1</c>, or <c>true</c> as some producers write it.</param>
        /// <returns>True when it is.</returns>
        private static bool IsTrue(string text)
        {
            return text.Trim() is "1" || string.Equals(text.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A number cell's text as a string column reads it: a whole number plain, any other its shortest round-trip form.</summary>
        /// <param name="text">The stored number.</param>
        /// <returns>The text; the stored text when it is no number.</returns>
        private static string RenderNumber(string text)
        {
            return ParseDouble(text) is not double number
                ? text
                : number == Math.Floor(number) && Math.Abs(number) <= MaxExactInteger
                ? ((long)number).ToString(CultureInfo.InvariantCulture)
                : number.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
