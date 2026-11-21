// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Calendars;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Calendars
{
    /// <summary>
    ///     The calendar registry: lookup by code without regard to case, composition order, the first
    ///     of two systems with one code winning; and its composition, with the platform's three systems
    ///     first and a feature's calendar after them.
    /// </summary>
    public class CalendarRegistryTests
    {
        [Fact]
        public void Systems_are_found_by_code_without_regard_to_case_in_composition_order()
        {
            GregorianCalendarSystem gregorian = new();
            EthiopianCalendarSystem ethiopian = new();
            CalendarRegistry registry = new([gregorian, ethiopian]);

            Assert.Equal(["gc", "et"], registry.Codes);
            Assert.Same(ethiopian, registry["ET"]);
            Assert.Same(gregorian, registry.TryGet("Gc"));
            Assert.Null(registry.TryGet("uq"));
            Assert.Null(registry.TryGet(null!));
            Assert.Throws<KeyNotFoundException>(() => registry["uq"]);
            Assert.Throws<ArgumentNullException>(() => registry[null!]);
        }

        [Fact]
        public void The_first_system_with_a_code_wins_and_a_system_without_one_is_skipped()
        {
            GregorianCalendarSystem first = new();
            CalendarRegistry registry = new([first, new TestCalendar("GC"), new TestCalendar(string.Empty), new UmAlQuraCalendarSystem()]);

            Assert.Equal(["gc", "uq"], registry.Codes);
            Assert.Same(first, registry["gc"]);
        }

        [Fact]
        public void The_composition_registers_the_platforms_three_calendars_first()
        {
            using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider();

            ICalendarRegistry registry = provider.GetRequiredService<ICalendarRegistry>();
            Assert.Equal([CalendarCodes.Gregorian, CalendarCodes.UmAlQura, CalendarCodes.Ethiopian], registry.Codes);
            Assert.IsType<GregorianCalendarSystem>(registry[CalendarCodes.Gregorian]);
            Assert.IsType<UmAlQuraCalendarSystem>(registry[CalendarCodes.UmAlQura]);
            Assert.IsType<EthiopianCalendarSystem>(registry[CalendarCodes.Ethiopian]);
            Assert.Same(registry, provider.GetRequiredService<ICalendarRegistry>());
        }

        [Fact]
        public void A_features_calendar_joins_after_the_platforms_and_a_repeated_class_is_registered_once()
        {
            using ServiceProvider provider = TestComposition.Compose(tellma => tellma
                    .AddFeature(new TestFeature<Tag.A>("persian", contribute: static c => c
                        .Calendar<PersianCalendarSystem>()
                        .Calendar<PersianCalendarSystem>()
                        .Calendar<GregorianCalendarSystem>())))
                .BuildServiceProvider();

            Assert.Equal(["gc", "uq", "et", "fa"], provider.GetRequiredService<ICalendarRegistry>().Codes);
            Assert.Equal(4, provider.GetServices<ICalendarSystem>().Count());
        }

        [Theory]
        [InlineData(typeof(string))]
        [InlineData(typeof(ICalendarSystem))]
        [InlineData(typeof(TestCalendar))]
        public void A_contributed_calendar_that_cannot_be_created_is_a_composition_problem(Type calendarType)
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("calendars", contribute: c => c.Add(new CalendarContributionItem(calendarType)))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("'calendars'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("calendar system", problem.Problem, StringComparison.Ordinal);
            Assert.NotNull(problem.Fix);
        }

        /// <summary>A distribution's own calendar system: the Persian calendar, over the framework's.</summary>
        private sealed class PersianCalendarSystem : ICalendarSystem
        {
            /// <inheritdoc />
            public string Code => "fa";

            /// <inheritdoc />
            public Calendar Calendar { get; } = new PersianCalendar();

            /// <inheritdoc />
            public int MonthCount => 12;

            /// <inheritdoc />
            public DateOnly MinSupported => DateOnly.FromDateTime(Calendar.MinSupportedDateTime);

            /// <inheritdoc />
            public DateOnly MaxSupported => DateOnly.MaxValue;

            /// <inheritdoc />
            public (int Year, int Month, int Day) Decompose(DateOnly date)
            {
                var time = date.ToDateTime(TimeOnly.MinValue);
                return (Calendar.GetYear(time), Calendar.GetMonth(time), Calendar.GetDayOfMonth(time));
            }

            /// <inheritdoc />
            public string Format(DateOnly date, CultureInfo culture, DateStyle style)
            {
                (int year, int month, int day) = Decompose(date);
                return $"{year}/{month}/{day}";
            }

            /// <inheritdoc />
            public string Format(DateTimeOffset instant, TimeZoneInfo zone, CultureInfo culture, DateStyle style, bool includeTime = true)
            {
                return Format(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime), culture, style);
            }

            /// <inheritdoc />
            public string MonthName(int month, CultureInfo culture, bool abbreviated)
            {
                return month.ToString(CultureInfo.InvariantCulture);
            }

            /// <inheritdoc />
            public DateOnly? TryParse(string text, CultureInfo culture)
            {
                return null;
            }
        }
    }
}
