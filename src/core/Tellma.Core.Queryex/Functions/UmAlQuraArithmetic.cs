// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Queryex.Emit;

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     The arithmetic a table calendar's answers are composed with, around the months its
    ///     lookups find.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A table calendar has no formula for its months; the host's month table gives each one
    ///         its year, its number and its first and last Gregorian days. Everything a calendar
    ///         function answers is then read off one or two of those rows: a component is a column,
    ///         a truncation is the first day of the right row, and an addition is a day count moved
    ///         from one row to another.
    ///     </para>
    ///     <para>
    ///         Every operand here is atomic — a column of a lookup, a parameter, or a value given a
    ///         name — so the patterns may write one more than once. Every date that is counted in
    ///         days is a date without its time, while an addition shifts the operand itself, which
    ///         keeps the time of day.
    ///     </para>
    /// </remarks>
    internal static class UmAlQuraArithmetic
    {
        /// <summary>A date with a time of day, reduced to its date, which is what a month is sought by.</summary>
        internal static EmitTemplate ToDate { get; } = EmitTemplate.Parse("CAST({0} AS date)", QueryexStoreType.QxDate);

        /// <summary>The quarter, from the month: <c>(month + 2) / 3</c>.</summary>
        internal static EmitTemplate Quarter { get; } = EmitTemplate.Parse("(({0} + 2) / 3)", QueryexStoreType.QxInt);

        /// <summary>The day of the month, from the month's first day and the date.</summary>
        internal static EmitTemplate DayOfMonth { get; } =
            EmitTemplate.Parse("(DATEDIFF(DAY, {0}, {1}) + 1)", QueryexStoreType.QxInt);

        /// <summary>The week of the year, in sevens from the year's first day and the date.</summary>
        internal static EmitTemplate Week { get; } =
            EmitTemplate.Parse("(DATEDIFF(DAY, {0}, {1}) / 7 + 1)", QueryexStoreType.QxInt);

        /// <summary>The year's first month.</summary>
        internal static EmitTemplate FirstMonth { get; } = EmitTemplate.Parse("1", QueryexStoreType.QxInt);

        /// <summary>The quarter's first month, from the month.</summary>
        internal static EmitTemplate QuarterFirstMonth { get; } =
            EmitTemplate.Parse("((({0} - 1) / 3) * 3 + 1)", QueryexStoreType.QxInt);

        /// <summary>
        ///     The year a number of months later, from the year, the month and the count truncated
        ///     to a whole number.
        /// </summary>
        /// <remarks>
        ///     Months are counted from month 1 of year 0. A count that reaches below it gives a year
        ///     or a month no table holds, and so the absent value, however the backend rounds.
        /// </remarks>
        internal static EmitTemplate MonthsLaterYear { get; } =
            EmitTemplate.Parse("(({0} * 12 + {1} - 1 + CAST({2} AS int)) / 12)", QueryexStoreType.QxInt);

        /// <summary>The month a number of months later, from the year, the month and the count.</summary>
        internal static EmitTemplate MonthsLaterMonth { get; } =
            EmitTemplate.Parse("(({0} * 12 + {1} - 1 + CAST({2} AS int)) % 12 + 1)", QueryexStoreType.QxInt);

        /// <summary>The year a number of years later, from the year and the count truncated to a whole number.</summary>
        internal static EmitTemplate YearsLater { get; } =
            EmitTemplate.Parse("({0} + CAST({1} AS int))", QueryexStoreType.QxInt);

        /// <summary>
        ///     A date moved to the same day of another month, or to that month's last day when it is
        ///     shorter: from the source month's first day, the target month's first day, the date
        ///     without its time, the target month's end, and the date itself.
        /// </summary>
        /// <remarks>
        ///     The date itself is what moves, by whole days, so its time of day is kept. An absent
        ///     target month — one the table does not hold — makes the whole result absent.
        /// </remarks>
        internal static EmitTemplate Shift { get; } = EmitTemplate.Parse(ShiftText(0, 1, 2, 3, 4), follows: 4);

        /// <summary>
        ///     The whole months from one date to another: from the second date's year and month, the
        ///     first date's year and month, the first date's month's first day, the second date's
        ///     month's first day, the first date without its time, the second date's month's end, the
        ///     first date, and the second date.
        /// </summary>
        /// <remarks>
        ///     The months crossed, corrected the way the Gregorian calendar corrects them: the first
        ///     date moved by that many months lands in the second date's month, and a crossing whose
        ///     month has not run its full length by the second date is not counted.
        /// </remarks>
        internal static EmitTemplate DiffMonths { get; } = EmitTemplate.Parse(
            CompletedText("(({0} * 12 + {1}) - ({2} * 12 + {3}))", ShiftText(4, 5, 6, 7, 8), "{9}"),
            QueryexStoreType.QxInt);

        /// <summary>
        ///     The whole years from one date to another: from the second date's year, the first
        ///     date's year, the first date's month's first day, the first day of the same month in
        ///     the second date's year, the first date without its time, that month's end, the first
        ///     date, and the second date.
        /// </summary>
        /// <remarks>
        ///     The years crossed, corrected as the months are: the first date moved by that many years
        ///     lands in its own month of the second date's year.
        /// </remarks>
        internal static EmitTemplate DiffYears { get; } = EmitTemplate.Parse(
            CompletedText("({0} - {1})", ShiftText(2, 3, 4, 5, 6), "{7}"),
            QueryexStoreType.QxInt);

        /// <summary>The text of a date moved into another month, from the positions of its operands.</summary>
        /// <param name="from">The source month's first day.</param>
        /// <param name="target">The target month's first day.</param>
        /// <param name="day">The date without its time.</param>
        /// <param name="end">The target month's end.</param>
        /// <param name="date">The date itself.</param>
        /// <returns>The pattern text.</returns>
        private static string ShiftText(int from, int target, int day, int end, int date)
        {
            // The date moves by the distance between the two months' first days, which keeps its
            // day of the month, and back by however far that day runs past the target month's last.
            return string.Create(
                CultureInfo.InvariantCulture,
                $"DATEADD(DAY, DATEDIFF(DAY, {{{from}}}, {{{target}}}) + LEAST(0, DATEDIFF(DAY, {{{target}}}, {{{end}}}) - 1 - DATEDIFF(DAY, {{{from}}}, {{{day}}})), {{{date}}})");
        }

        /// <summary>The text of the units completed, from the units crossed, the moved date, and the second date.</summary>
        /// <param name="crossed">The units crossed.</param>
        /// <param name="moved">The first date moved by that many units.</param>
        /// <param name="second">The second date.</param>
        /// <returns>The pattern text.</returns>
        /// <remarks>
        ///     <para>
        ///         Written out in full rather than over named parts, as the Gregorian calendar's
        ///         pattern is: every operand is atomic, so repeating the text repeats no work the
        ///         backend cannot see is the same.
        ///     </para>
        ///     <para>
        ///         A target month the table does not hold leaves the moved date absent, and with it
        ///         the answer, as for any date beyond the table: neither correction could be decided,
        ///         and the uncorrected crossing would be off by one whenever one applied.
        ///     </para>
        /// </remarks>
        private static string CompletedText(string crossed, string moved, string second)
        {
            return $"({crossed} + CASE WHEN {moved} IS NULL THEN NULL"
                + $" WHEN {crossed} > 0 AND {moved} > {second} THEN -1"
                + $" WHEN {crossed} < 0 AND {moved} < {second} THEN 1 ELSE 0 END)";
        }
    }
}
