// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Text;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Semantics
{
    /// <summary>One month of a table calendar, as the host's month table holds it.</summary>
    /// <param name="Year">The calendar's year.</param>
    /// <param name="Month">The calendar's month, from 1.</param>
    /// <param name="StartDate">The month's first Gregorian day.</param>
    /// <param name="EndDate">The day after the month's last.</param>
    public sealed record CalendarMonth(int Year, int Month, DateOnly StartDate, DateOnly EndDate);

    /// <summary>
    ///     The Umm al-Qura months the fixture's month table holds, and the statements that create
    ///     and fill it on a server.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every month the platform's own calendar knows, AH 1318 to 1500, read off the
    ///         runtime's Umm al-Qura calendar — the same source the host seeds its table from. The
    ///         reference reading of the language converts with that calendar directly, so the
    ///         table and the reading agree by construction, and what a differential run compares is
    ///         the engine's use of the table.
    ///     </para>
    ///     <para>
    ///         The table is declared the way the host declares it: a key on the calendar, year and
    ///         month, and a unique index on the calendar and first day, which is what a lookup by
    ///         date seeks on.
    ///     </para>
    /// </remarks>
    public static class CalendarMonths
    {
        /// <summary>The code the months are stored under.</summary>
        public const string UmAlQura = "uq";

        /// <summary>The first year the table holds.</summary>
        public const int FirstYear = 1318;

        /// <summary>The last year the table holds.</summary>
        public const int LastYear = 1500;

        /// <summary>The calendar the months are read from.</summary>
        private static readonly UmAlQuraCalendar Calendar = new();

        /// <summary>Every month, in order.</summary>
        public static IReadOnlyList<CalendarMonth> All { get; } = Build();

        /// <summary>The statement that creates the month table.</summary>
        /// <returns>The script.</returns>
        public static string Create()
        {
            return "CREATE TABLE " + LedgerFixture.CalendarMonthsSource + " (\n"
                + "    [Calendar] varchar(2) COLLATE " + LedgerDdl.Collation + " NOT NULL,\n"
                + "    [Year] int NOT NULL,\n"
                + "    [Month] int NOT NULL,\n"
                + "    [StartDate] date NOT NULL,\n"
                + "    [EndDate] date NOT NULL,\n"
                + "    CONSTRAINT [PK_CalendarMonths] PRIMARY KEY ([Calendar], [Year], [Month]),\n"
                + "    CONSTRAINT [UX_CalendarMonths_StartDate] UNIQUE ([Calendar], [StartDate])\n"
                + ");\n";
        }

        /// <summary>The statements that fill the month table.</summary>
        /// <returns>The script.</returns>
        public static string Insert()
        {
            StringBuilder script = new();

            // In runs well under the backend's limit on the rows one value list may hold.
            const int Run = 500;
            for (int start = 0; start < All.Count; start += Run)
            {
                script.Append("INSERT INTO ").Append(LedgerFixture.CalendarMonthsSource)
                    .Append(" ([Calendar], [Year], [Month], [StartDate], [EndDate]) VALUES\n");

                int end = Math.Min(start + Run, All.Count);
                for (int index = start; index < end; index++)
                {
                    CalendarMonth month = All[index];
                    script.Append(CultureInfo.InvariantCulture, $"('{UmAlQura}', {month.Year}, {month.Month}, ")
                        .Append(CultureInfo.InvariantCulture, $"'{month.StartDate:yyyyMMdd}', '{month.EndDate:yyyyMMdd}')")
                        .Append(index < end - 1 ? ",\n" : ";\n");
                }
            }

            return script.ToString();
        }

        /// <summary>Reads every month off the calendar.</summary>
        /// <returns>The months, in order.</returns>
        private static List<CalendarMonth> Build()
        {
            List<CalendarMonth> months = [];
            for (int year = FirstYear; year <= LastYear; year++)
            {
                for (int month = 1; month <= 12; month++)
                {
                    var start = DateOnly.FromDateTime(Calendar.ToDateTime(year, month, 1, 0, 0, 0, 0));
                    months.Add(new CalendarMonth(
                        year,
                        month,
                        start,
                        start.AddDays(Calendar.GetDaysInMonth(year, month))));
                }
            }

            return months;
        }
    }
}
