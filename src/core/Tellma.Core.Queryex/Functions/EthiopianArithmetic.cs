// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Functions
{
    /// <summary>
    ///     The emission patterns of the Ethiopian calendar, Amete Mihret, as inline integer
    ///     arithmetic.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Twelve months of thirty days and then Pagume, of five days or six in a year whose
    ///         number leaves 3 when divided by 4. The calendar's first day, 1 Meskerem 1, is the
    ///         proleptic Gregorian 0008-08-27.
    ///     </para>
    ///     <para>
    ///         Everything is read off one day count, <c>c</c>: the days since that first day plus
    ///         two four-year cycles, 2,922 days. The two cycles keep <c>c</c> non-negative for every
    ///         date the backend can hold, which is what makes its integer division floor rather
    ///         than truncate toward zero. From it, <c>y = (4c + 1463) / 1461</c> is the year counted
    ///         from eight years before the first, a year <c>y</c> begins on day
    ///         <c>365(y − 1) + y / 4</c>, and <c>k</c>, the day of the year from zero, is what is
    ///         left. The Ethiopian year is <c>y − 8</c>, so it is a leap year exactly when <c>y</c>
    ///         leaves 3 too.
    ///     </para>
    ///     <para>
    ///         The date operand appears many times. Lowering gives it a name first wherever it is not
    ///         a column or a parameter, so it is still computed once. A date with a time of day is
    ///         counted by its date — the backend's day count ignores the time — and the two additions
    ///         shift the operand itself by whole days, which keeps its time of day.
    ///     </para>
    /// </remarks>
    internal static class EthiopianArithmetic
    {
        /// <summary>The first day of the calendar, 1 Meskerem 1.</summary>
        private const string Epoch = "CONVERT(date, '00080827')";

        /// <summary>The year: <c>y − 8</c>.</summary>
        internal static string Year { get; } = $"((4 * {Count("{0}")} + 1463) / 1461 - 8)";

        /// <summary>The month, Pagume being the thirteenth.</summary>
        internal static string Month { get; } = $"({DayOfYear("{0}")} / 30 + 1)";

        /// <summary>The day of the month.</summary>
        internal static string Day { get; } = $"({DayOfYear("{0}")} % 30 + 1)";

        /// <summary>The quarter, Pagume being in the fourth: <c>min(4, (month + 2) / 3)</c>.</summary>
        internal static string Quarter { get; } = $"(LEAST({DayOfYear("{0}")} / 90, 3) + 1)";

        /// <summary>The week of the year, counted in sevens from the year's first day.</summary>
        internal static string Week { get; } = $"({DayOfYear("{0}")} / 7 + 1)";

        /// <summary>The first day of the year: the date, less the days the year has run.</summary>
        internal static string StartOfYear { get; } =
            $"DATEADD(DAY, {DaysSinceEpoch("{0}")} - {DayOfYear("{0}")}, {Epoch})";

        /// <summary>The first day of the quarter: the year's first day, plus whole quarters of 90 days.</summary>
        internal static string StartOfQuarter { get; } =
            $"DATEADD(DAY, {DaysSinceEpoch("{0}")} - {DayOfYear("{0}")} + 90 * LEAST({DayOfYear("{0}")} / 90, 3), {Epoch})";

        /// <summary>The first day of the month: the date, less the days the month has run.</summary>
        internal static string StartOfMonth { get; } =
            $"DATEADD(DAY, {DaysSinceEpoch("{0}")} - {DayOfYear("{0}")} % 30, {Epoch})";

        /// <summary>
        ///     A whole number of years later: the difference between the two years' first days, less
        ///     a day where 6 Pagume lands in a year with five.
        /// </summary>
        internal static string AddYears { get; } = BuildAddYears();

        /// <summary>
        ///     A whole number of months later: the target month's first day, plus the date's day in
        ///     its own month clamped to the target month's last.
        /// </summary>
        internal static string AddMonths { get; } = BuildAddMonths();

        /// <summary>The whole years from one date to another, as the Gregorian count corrects its crossings.</summary>
        internal static string DiffYears { get; } = BuildDiffYears();

        /// <summary>The whole months from one date to another, as the Gregorian count corrects its crossings.</summary>
        internal static string DiffMonths { get; } = BuildDiffMonths();

        /// <summary>The days from the calendar's first day to a date.</summary>
        /// <param name="date">The date's placeholder.</param>
        /// <returns>The SQL.</returns>
        private static string DaysSinceEpoch(string date)
        {
            return $"DATEDIFF(DAY, {Epoch}, {date})";
        }

        /// <summary>The day count <c>c</c>.</summary>
        /// <param name="date">The date's placeholder.</param>
        /// <returns>The SQL.</returns>
        private static string Count(string date)
        {
            return $"({DaysSinceEpoch(date)} + 2922)";
        }

        /// <summary>The shifted year <c>y</c>, eight more than the Ethiopian year.</summary>
        /// <param name="date">The date's placeholder.</param>
        /// <returns>The SQL.</returns>
        private static string ShiftedYear(string date)
        {
            return $"((4 * {Count(date)} + 1463) / 1461)";
        }

        /// <summary>The day of the year <c>k</c>, from zero.</summary>
        /// <param name="date">The date's placeholder.</param>
        /// <returns>The SQL.</returns>
        private static string DayOfYear(string date)
        {
            string year = ShiftedYear(date);
            return $"({Count(date)} - (365 * ({year} - 1) + {year} / 4))";
        }

        /// <summary>The last day of a month, from zero: 29, or 4 or 5 for Pagume.</summary>
        /// <param name="shiftedYear">The month's shifted year.</param>
        /// <param name="month">The month, from zero.</param>
        /// <returns>The SQL.</returns>
        /// <remarks>
        ///     <c>month / 12</c> is 1 for Pagume alone, and <c>y % 4 / 3</c> is 1 in a leap year
        ///     alone, so the thirty-day answer loses 25 days in Pagume and gets one of them back in
        ///     a leap year.
        /// </remarks>
        private static string LastDay(string shiftedYear, string month)
        {
            return $"(29 - {month} / 12 * (25 - {shiftedYear} % 4 / 3))";
        }

        /// <summary>The whole number <c>n</c>, truncated as the Gregorian additions truncate it.</summary>
        /// <returns>The SQL.</returns>
        private static string Whole()
        {
            return "CAST({1} AS int)";
        }

        /// <summary>Builds <see cref="AddYears" />.</summary>
        /// <returns>The pattern.</returns>
        private static string BuildAddYears()
        {
            string year = ShiftedYear("{0}");
            string target = $"({year} + {Whole()})";
            string pagumeSix = $"{DayOfYear("{0}")} / 365";
            return $"DATEADD(DAY, 365 * {Whole()} + {target} / 4 - {year} / 4 - {pagumeSix} * (1 - {target} % 4 / 3), {{0}})";
        }

        /// <summary>Builds <see cref="AddMonths" />.</summary>
        /// <returns>The pattern.</returns>
        private static string BuildAddMonths()
        {
            // The months are counted from zero across years, thirteen to a year; the target's year
            // and month come back out of the count.
            string dayOfYear = DayOfYear("{0}");
            string months = $"({ShiftedYear("{0}")} * 13 + {dayOfYear} / 30 + {Whole()})";
            string year = $"({months} / 13)";
            string month = $"({months} % 13)";
            string first = $"365 * ({year} - 1) + {year} / 4 + 30 * {month}";
            return $"DATEADD(DAY, {first} + LEAST({dayOfYear} % 30, {LastDay(year, month)}) - {Count("{0}")}, {{0}})";
        }

        /// <summary>Builds <see cref="DiffYears" />.</summary>
        /// <returns>The pattern.</returns>
        private static string BuildDiffYears()
        {
            string from = ShiftedYear("{0}");
            string to = ShiftedYear("{1}");
            string crossings = $"({to} - {from})";

            // The first date moved by the crossings, which lands in the second date's year.
            string pagumeSix = $"{DayOfYear("{0}")} / 365";
            string moved = $"DATEADD(DAY, 365 * {crossings} + {to} / 4 - {from} / 4 - {pagumeSix} * (1 - {to} % 4 / 3), {{0}})";
            return Corrected(crossings, moved);
        }

        /// <summary>Builds <see cref="DiffMonths" />.</summary>
        /// <returns>The pattern.</returns>
        private static string BuildDiffMonths()
        {
            string fromDay = DayOfYear("{0}");
            string toDay = DayOfYear("{1}");
            string crossings = $"(({ShiftedYear("{1}")} * 13 + {toDay} / 30) - ({ShiftedYear("{0}")} * 13 + {fromDay} / 30))";

            // The first date moved by the crossings, which lands in the second date's month: that
            // month's first day, plus the first date's day clamped to the month's last.
            string lastDay = LastDay(ShiftedYear("{1}"), $"({toDay} / 30)");
            string moved = $"DATEADD(DAY, DATEDIFF(DAY, {{0}}, {{1}}) - {toDay} % 30 + LEAST({fromDay} % 30, {lastDay}), {{0}})";
            return Corrected(crossings, moved);
        }

        /// <summary>Corrects a count of crossed boundaries to a count of completed units.</summary>
        /// <param name="crossings">The boundaries crossed.</param>
        /// <param name="moved">The first date moved by that many units.</param>
        /// <returns>The pattern.</returns>
        /// <remarks>
        ///     The Gregorian rule exactly: a unit counts once the first date moved by it has not
        ///     overshot the second.
        /// </remarks>
        private static string Corrected(string crossings, string moved)
        {
            return $"({crossings} + CASE"
                + $" WHEN {crossings} > 0 AND {moved} > {{1}} THEN -1"
                + $" WHEN {crossings} < 0 AND {moved} < {{1}} THEN 1"
                + " ELSE 0 END)";
        }
    }
}
