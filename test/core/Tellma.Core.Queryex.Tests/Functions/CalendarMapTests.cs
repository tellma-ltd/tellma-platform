// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Queryex.Tests.Functions
{
    /// <summary>
    ///     What a host is refused when it names the month table of a table calendar.
    /// </summary>
    /// <remarks>
    ///     The table's name reaches emitted SQL verbatim, so its shape is checked as an entity's
    ///     source is; the calendar is checked when the schema is built, against the calendars that
    ///     read a table and the ones already mapped.
    /// </remarks>
    public sealed class CalendarMapTests
    {
        /// <summary>A well-formed month table.</summary>
        private const string Months = "[core].[CalendarMonths]";

        /// <summary>A calendar that is computed rather than read cannot be given a table.</summary>
        /// <param name="calendar">The calendar's code, in any case.</param>
        [Theory]
        [InlineData("gc")]
        [InlineData("et")]
        [InlineData("ET")]
        [InlineData("xx")]
        public void AComputedOrUnknownCalendar_IsRefusedByBuild(string calendar)
        {
            QueryexSchemaBuilder builder = new QueryexSchemaBuilder().CalendarMap(calendar, Months);

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.Build);
            Assert.Contains("'" + calendar + "'", error.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     One calendar mapped twice is refused, however the second map spells its code, because
        ///     codes are matched ignoring case.
        /// </summary>
        /// <param name="second">The code the second map uses.</param>
        [Theory]
        [InlineData("uq")]
        [InlineData("UQ")]
        [InlineData("Uq")]
        public void ASecondMapForOneCalendar_IsRefusedByBuild(string second)
        {
            QueryexSchemaBuilder builder = new QueryexSchemaBuilder()
                .CalendarMap("uq", Months)
                .CalendarMap(second, "[core].[OtherMonths]");

            InvalidOperationException error = Assert.Throws<InvalidOperationException>(builder.Build);
            Assert.Contains("'uq'", error.Message, StringComparison.Ordinal);
        }

        /// <summary>A code spelled in another case maps the calendar under its canonical code.</summary>
        [Fact]
        public void ACodeInAnotherCase_MapsTheCalendar()
        {
            QueryexSchema schema = new QueryexSchemaBuilder().CalendarMap("UQ", Months).Build();

            QueryexResult<CompiledQuery> result = new QueryexEngine().EvaluateConstant(
                [new ConstantItem("month(today(), 'uq')", QueryexType.QxNumeric)],
                new ConstantCompilationOptions
                {
                    Schema = schema,
                    QueryexVersion = QueryexLanguage.Version,
                });

            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));
            Assert.Contains("FROM " + Months + " AS [M]", result.Value.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A month table that is not a bracket-quoted, optionally qualified name is refused at
        ///     once, before anything can reach emitted SQL.
        /// </summary>
        /// <param name="source">The malformed name.</param>
        [Theory]
        [InlineData("CalendarMonths")]
        [InlineData("[core].CalendarMonths")]
        [InlineData("[core].[CalendarMonths]; DROP TABLE [x]")]
        [InlineData("[core].[CalendarMonths] AS [M] --")]
        [InlineData("[core]..[CalendarMonths]")]
        [InlineData("[a].[b].[c].[d]")]
        public void AMalformedMonthTable_IsRefused(string source)
        {
            Assert.Throws<ArgumentException>(() => new QueryexSchemaBuilder().CalendarMap("uq", source));
        }

        /// <summary>An empty calendar or month table is refused at once.</summary>
        /// <param name="calendar">The calendar's code.</param>
        /// <param name="source">The month table.</param>
        [Theory]
        [InlineData("", Months)]
        [InlineData(" ", Months)]
        [InlineData("uq", "")]
        [InlineData("uq", " ")]
        public void AnEmptyArgument_IsRefused(string calendar, string source)
        {
            Assert.Throws<ArgumentException>(() => new QueryexSchemaBuilder().CalendarMap(calendar, source));
        }

        /// <summary>A built schema is frozen: no table can be mapped into it afterwards.</summary>
        [Fact]
        public void AMapAfterBuild_IsRefused()
        {
            QueryexSchemaBuilder builder = new();
            builder.Build();

            Assert.Throws<InvalidOperationException>(() => builder.CalendarMap("uq", Months));
        }
    }
}
