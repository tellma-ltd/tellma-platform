// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Core.Queryex.IntegrationTests
{
    /// <summary>
    ///     A month table that starts partway through a year, as a host's table may: it need only be
    ///     contiguous, not hold whole years.
    /// </summary>
    /// <remarks>
    ///     The fixture's table and the interpreter's both hold whole years, so a difference whose
    ///     correction reads a month before the table's first is reachable only here. Such a month has
    ///     no value in the calendar, and neither has any answer computed from it.
    /// </remarks>
    /// <param name="server">The server the fixture put the months on.</param>
    [Collection(SqlServerGroup.Name)]
    [Trait("Category", "Integration")]
    public sealed class PartialMonthTableTests(SqlServerFixture server)
    {
        /// <summary>The partial table, filled from the fixture's own.</summary>
        private const string Source = "[core].[CalendarMonthsFromMidYear]";

        /// <summary>The year the partial table starts in.</summary>
        private const int FirstYear = 1445;

        /// <summary>The month of <see cref="FirstYear" /> the partial table starts with.</summary>
        private const int FirstMonth = 6;

        /// <summary>The calendar the dates are written in.</summary>
        private static readonly UmAlQuraCalendar Hijri = new();

        /// <summary>The server the fixture put the months on.</summary>
        private readonly SqlServerFixture _server = server;

        /// <summary>
        ///     Whole years between two dates, where moving the first into the second's year lands in a
        ///     month the table does not hold, are absent; where it lands in one the table holds, they
        ///     are counted and corrected as usual.
        /// </summary>
        /// <param name="first">The first date, as Hijri year, month and day.</param>
        /// <param name="second">The second date, as Hijri year, month and day.</param>
        /// <param name="years">The whole years expected, or null for absent.</param>
        /// <returns>A task that completes when the answer has been compared.</returns>
        [Theory]
        [InlineData(new[] { 1446, 3, 15 }, new[] { 1445, 7, 10 }, null)]
        [InlineData(new[] { 1446, 8, 15 }, new[] { 1445, 7, 10 }, -1)]
        [InlineData(new[] { 1445, 8, 15 }, new[] { 1446, 9, 1 }, 1)]
        [InlineData(new[] { 1445, 8, 15 }, new[] { 1446, 8, 1 }, 0)]
        public async Task DiffYears_IntoAMonthBeforeTheTable_IsAbsent(int[] first, int[] second, int? years)
        {
            await EnsureTable();

            QueryexSchema schema = new QueryexSchemaBuilder().CalendarMap("uq", Source).Build();
            string item = "diffYears(" + Literal(first) + ", " + Literal(second) + ", 'uq')";
            QueryexResult<CompiledQuery> result = new QueryexEngine().EvaluateConstant(
                [new ConstantItem(item, QueryexType.QxNumeric)],
                new ConstantCompilationOptions
                {
                    Schema = schema,
                    QueryexVersion = QueryexLanguage.Version,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            IReadOnlyList<QxValue> row = Assert.Single(
                await DifferentialTests.ExecuteOn(_server, result.Value, InterpreterContext.Fixed));

            QxValue answer = Assert.Single(row);
            if (years is null)
            {
                Assert.True(answer.IsAbsent, item + " answered " + answer);
            }
            else
            {
                Assert.False(answer.IsAbsent, item + " was absent");
                Assert.Equal(years.Value, (int)answer.AsNumber.Value);
            }
        }

        /// <summary>Writes a Hijri date as a date literal of the language.</summary>
        /// <param name="date">The Hijri year, month and day.</param>
        /// <returns>The literal.</returns>
        private static string Literal(int[] date)
        {
            var gregorian = Hijri.ToDateTime(date[0], date[1], date[2], 0, 0, 0, 0);
            return "cast('" + gregorian.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + "', 'date')";
        }

        /// <summary>Creates the partial table once, from the months the fixture's table holds.</summary>
        /// <returns>A task that completes when the table exists.</returns>
        private async Task EnsureTable()
        {
            await using SqlConnection connection = await _server.OpenAsync();
            await using SqlCommand command = connection.CreateCommand();

            // Created by whichever test gets here first; the others find it in place.
            command.CommandText = string.Create(
                CultureInfo.InvariantCulture,
                $"""
                IF OBJECT_ID(N'core.CalendarMonthsFromMidYear', N'U') IS NULL
                BEGIN
                    SELECT [Calendar], [Year], [Month], [StartDate], [EndDate]
                    INTO {Source}
                    FROM {LedgerFixture.CalendarMonthsSource}
                    WHERE [Year] > {FirstYear} OR ([Year] = {FirstYear} AND [Month] >= {FirstMonth});
                END;
                """);

            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
    }
}
