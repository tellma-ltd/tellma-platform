// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     Every calendar function, in every calendar, compared against the second implementation
    ///     day by day over two centuries.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The corpus checks the shapes a calendar call takes on a handful of rows; this checks
    ///         what it computes on every day, which is where a calendar's arithmetic goes wrong: on
    ///         the leap days, the short months, the year boundaries, and the days a move into a
    ///         shorter month has to pull back. The Ethiopian reading is itself checked against ICU,
    ///         and the Umm al-Qura month table is the runtime's own, so a disagreement here is the
    ///         engine's.
    ///     </para>
    ///     <para>
    ///         Each row's second day lies up to a year either side of its first, so the differences
    ///         count forward and backward across every kind of boundary. The days run past both
    ///         ends of the month table, where every Umm al-Qura answer is absent.
    ///     </para>
    /// </remarks>
    /// <param name="server">The server the fixture put the days on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class CalendarSweepTests(LedgerDatabase server)
    {
        /// <summary>The functions, each with the calendar written where the placeholder is.</summary>
        private static readonly string[] Functions =
        [
            "year(Date, {0})",
            "quarter(Date, {0})",
            "month(Date, {0})",
            "day(Date, {0})",
            "week(Date, {0})",
            "startOfYear(Date, {0})",
            "startOfQuarter(Date, {0})",
            "startOfMonth(Date, {0})",
            "addYears(Date, 1, {0})",
            "addMonths(Date, 1, {0})",
            "addMonths(Moment, -13, {0})",
            "addYears(Moment, -3, {0})",
            "diffYears(Date, Other, {0})",
            "diffMonths(Date, Other, {0})",
        ];

        /// <summary>The server the fixture put the days on.</summary>
        private readonly LedgerDatabase _server = server;

        /// <summary>The engine under test.</summary>
        private readonly QueryexEngine _engine = new();

        /// <summary>Every function answers the same on the server as in memory, on every day of two centuries.</summary>
        /// <param name="calendar">The calendar's code.</param>
        /// <returns>A task that completes when every day has been checked.</returns>
        /// <remarks>
        ///     About twenty seconds a calendar, nearly all of it the interpreter's, which is well
        ///     inside what a pull request's integration run allows.
        /// </remarks>
        [Theory]
        [InlineData("gc")]
        [InlineData("et")]
        [InlineData("uq")]
        public async Task EveryFunction_AnswersTheSameOnEveryDay(string calendar)
        {
            // The key first, so a failure names the day it is about.
            string code = "'" + calendar + "'";
            QuerySpec spec = new()
            {
                Root = CalendarDays.Day,
                Select = "Id, " + string.Join(
                    ", ",
                    Functions.Select(function => string.Format(CultureInfo.InvariantCulture, function, code))),
                OrderBy = "Id",
            };

            QueryexResult<CompiledQuery> result = _engine.CompileQuery(
                spec,
                new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = CalendarDays.Schema,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            InterpreterContext context = InterpreterContext.Fixed;
            IReadOnlyList<IReadOnlyList<QxValue>> expected =
                ReferenceQuery.Run(spec, context, CalendarDays.Schema, CalendarDays.Rows);

            IReadOnlyList<IReadOnlyList<QxValue>> actual =
                await DifferentialTests.ExecuteOn(_server, result.Value, context);

            Assert.Equal(CalendarDays.Rows.Count, expected.Count);
            Assert.Equal(expected.Count, actual.Count);

            for (int row = 0; row < expected.Count; row++)
            {
                // The two answers hold the same day in each position before anything on it is compared.
                DifferentialTests.AssertSame(expected[row][0], actual[row][0], calendar + " row " + row.ToString(CultureInfo.InvariantCulture));
                DateOnly day = CalendarDays.First.AddDays((int)actual[row][0].AsNumber.Value);
                for (int column = 1; column < expected[row].Count; column++)
                {
                    DifferentialTests.AssertSame(
                        expected[row][column],
                        actual[row][column],
                        calendar + " " + day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " "
                            + string.Format(CultureInfo.InvariantCulture, Functions[column - 1], code));
                }
            }
        }
    }
}
