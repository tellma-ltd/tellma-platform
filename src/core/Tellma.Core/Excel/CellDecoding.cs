// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Internal;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The kinds of value a cell is expected to hold, as an invalid cell's <c>expected</c> argument
    ///     names them.
    /// </summary>
    internal static class ExpectedValues
    {
        /// <summary>A boolean.</summary>
        public const string Bool = "Bool";

        /// <summary>A whole number.</summary>
        public const string Integer = "Integer";

        /// <summary>A number.</summary>
        public const string Numeric = "Numeric";

        /// <summary>Text.</summary>
        public const string String = "String";

        /// <summary>One of a column's choices: an enum member or a value set's value.</summary>
        public const string Choice = "Choice";

        /// <summary>A date.</summary>
        public const string Date = "Date";

        /// <summary>A date and time.</summary>
        public const string DateTime = "DateTime";

        /// <summary>An instant with an offset.</summary>
        public const string DateTimeOffset = "DateTimeOffset";

        /// <summary>A time of day.</summary>
        public const string Time = "Time";

        /// <summary>A GUID.</summary>
        public const string Guid = "Guid";
    }

    /// <summary>
    ///     What a cell is decoded into: the mapped property's CLR type, with what that type needs to
    ///     read text — the scale, the choices and their labels, the boolean labels, and a text date
    ///     column's calendar and culture from the manifest.
    /// </summary>
    /// <param name="ClrType">The property's CLR type; a nullable value type decodes as its underlying type.</param>
    internal sealed record CellDecodeTarget(Type ClrType)
    {
        /// <summary>A decimal's scale; a value with more decimal places is refused. Null for no check.</summary>
        public int? Scale { get; init; }

        /// <summary>The stored values of a value-set column; an enum's come from its type. Null for none.</summary>
        public IReadOnlyList<string>? Choices { get; init; }

        /// <summary>The labels a choice is also accepted by (any tenant language, English), each mapped to its stored value. Null for none.</summary>
        public IReadOnlyDictionary<string, string>? ChoiceLabels { get; init; }

        /// <summary>The localized words a boolean is also accepted by, each mapped to its value. Null for none.</summary>
        public IReadOnlyDictionary<string, bool>? BooleanLabels { get; init; }

        /// <summary>Whether text keeps its leading and trailing white space.</summary>
        public bool PreserveWhitespace { get; init; }

        /// <summary>The calendar a text date column was written in, per the manifest; null to use the request's.</summary>
        public ICalendarSystem? TextCalendar { get; init; }

        /// <summary>The culture a text date column was written in, per the manifest; null to use the request's.</summary>
        public CultureInfo? TextCulture { get; init; }
    }

    /// <summary>The workbook and request facts every cell decodes under.</summary>
    /// <param name="Date1904">Whether the workbook uses the 1904 date system.</param>
    /// <param name="Culture">The request culture, whose short date pattern text dates are read in.</param>
    /// <param name="Calendar">The request calendar.</param>
    /// <param name="TimeZone">The display zone an instant without an offset is read in.</param>
    internal sealed record CellDecodeContext(bool Date1904, CultureInfo Culture, ICalendarSystem Calendar, TimeZoneInfo TimeZone);

    /// <summary>A decoded cell: its value, or why it could not be decoded.</summary>
    /// <param name="Success">Whether the cell decoded.</param>
    /// <param name="Value">The value, of the target's type; null for a blank cell or a failure.</param>
    /// <param name="Expected">On failure, the kind of value expected (<see cref="ExpectedValues" />).</param>
    /// <param name="Text">On failure, the cell's text cut to 100 characters.</param>
    internal readonly record struct CellDecodeResult(bool Success, object? Value, string? Expected, string? Text)
    {
        /// <summary>The most characters of a refused cell's text an error carries.</summary>
        public const int MaxErrorTextLength = 100;

        /// <summary>A decoded value.</summary>
        /// <param name="value">The value; null for a blank cell.</param>
        /// <returns>The result.</returns>
        public static CellDecodeResult Of(object? value)
        {
            return new(true, value, null, null);
        }

        /// <summary>A refused cell.</summary>
        /// <param name="expected">The kind of value expected.</param>
        /// <param name="text">The cell's text.</param>
        /// <returns>The result.</returns>
        public static CellDecodeResult Refused(string expected, string text)
        {
            string cut = text.Length <= MaxErrorTextLength
                ? text
                : char.IsHighSurrogate(text[MaxErrorTextLength - 1]) ? text[..(MaxErrorTextLength - 1)] : text[..MaxErrorTextLength];
            return new(false, null, expected, cut);
        }

        /// <summary>The arguments of the invalid-cell error this failure reports.</summary>
        /// <returns><c>expected</c> and <c>value</c>.</returns>
        /// <exception cref="InvalidOperationException">The cell decoded.</exception>
        public IReadOnlyDictionary<string, object?> ErrorArguments()
        {
            return Success
                ? throw new InvalidOperationException("A decoded cell reports no error.")
                : ReadOnlyCopies.Of(new Dictionary<string, object?>(StringComparer.Ordinal) { ["expected"] = Expected, ["value"] = Text });
        }
    }
}
