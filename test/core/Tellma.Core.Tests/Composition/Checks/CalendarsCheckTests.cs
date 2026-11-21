// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Calendars;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.calendars</c>: every composed calendar system has a well-formed code, no two share
    ///     one, and the platform's three are composed.
    /// </summary>
    public class CalendarsCheckTests
    {
        private const string Check = "core.calendars";

        [Fact]
        public async Task The_platforms_own_calendars_pass()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task A_distributions_calendar_with_a_code_of_its_own_passes()
        {
            Assert.Empty(await RunAsync(new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem(), new TestCalendar("persian"), new TestCalendar("a1")));
        }

        [Theory]
        [InlineData("FA")]
        [InlineData("Persian")]
        [InlineData("1st")]
        [InlineData("fa-ir")]
        [InlineData("fa_ir")]
        [InlineData("fa ")]
        [InlineData("فا")]
        [InlineData("abcdefghijklmnopq")]
        [InlineData("")]
        public async Task A_code_that_is_not_short_lower_case_ascii_is_reported(string code)
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem(), new TestCalendar(code));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(Check, problem.Source);
            Assert.Equal(
                $"The calendar system Tellma.Core.Tests.Infrastructure.TestCalendar has the code '{code}', which is not 1 to 16 lower-case ASCII letters and digits starting with a letter.",
                problem.Problem);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
        }

        [Fact]
        public async Task A_system_without_a_code_is_reported_and_not_thrown()
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem(), new TestCalendar(null!));

            Assert.Contains("has the code ''", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("abcdefghijklmnop", true)]
        [InlineData("a", true)]
        [InlineData("gc2", true)]
        [InlineData("2gc", false)]
        [InlineData("Gc", false)]
        [InlineData(null, false)]
        public void A_code_is_one_to_sixteen_lower_case_letters_and_digits_starting_with_a_letter(string? code, bool expected)
        {
            Assert.Equal(expected, CalendarsCheck.IsWellFormed(code));
        }

        [Fact]
        public async Task Two_systems_under_one_code_are_reported_once()
        {
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem(), new TestCalendar("uq"), new TestCalendar("fa"), new TestCalendar("fa"));

            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, static problem => problem.Problem ==
                "The calendar systems Tellma.Core.Calendars.UmAlQuraCalendarSystem and Tellma.Core.Tests.Infrastructure.TestCalendar share the code 'uq'.");
            Assert.Contains(problems, static problem => problem.Problem ==
                "The calendar systems Tellma.Core.Tests.Infrastructure.TestCalendar and Tellma.Core.Tests.Infrastructure.TestCalendar share the code 'fa'.");
        }

        [Fact]
        public async Task Codes_that_differ_only_in_case_clash()
        {
            // The registry finds a system without regard to case, so 'GC' would be unreachable; it
            // is also malformed, and both are said.
            IReadOnlyList<CompositionProblem> problems = await RunAsync(
                new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem(), new TestCalendar("GC"));

            Assert.Equal(2, problems.Count);
            Assert.Contains(problems, static problem => problem.Problem.Contains("has the code 'GC'", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("share the code 'gc'", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("gc")]
        [InlineData("uq")]
        [InlineData("et")]
        public async Task A_missing_platform_calendar_is_reported(string missing)
        {
            ICalendarSystem[] platform = [new GregorianCalendarSystem(), new UmAlQuraCalendarSystem(), new EthiopianCalendarSystem()];

            IReadOnlyList<CompositionProblem> problems = await RunAsync([.. platform.Where(system => system.Code != missing)]);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal($"No composed calendar system has the code '{missing}', one of the three the platform and its query language rely on.", problem.Problem);
        }

        [Fact]
        public async Task A_host_level_registration_that_clashes_with_a_platform_calendar_fails_the_gate()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static tellma => tellma.Services.AddSingleton<ICalendarSystem>(new TestCalendar("et")));

            Assert.Contains("share the code 'et'", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        /// <summary>Runs the check over a provider holding exactly the given calendar systems.</summary>
        /// <param name="systems">The systems, in composition order.</param>
        /// <returns>The problems.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> RunAsync(params ICalendarSystem[] systems)
        {
            ServiceCollection services = new();
            foreach (ICalendarSystem system in systems)
            {
                _ = services.AddSingleton(system);
            }

            CalendarsCheck check = new();
            Assert.False(check.RequiresInfrastructure);
            await using ServiceProvider provider = services.BuildServiceProvider();
            return await check.CheckAsync(provider, TestContext.Current.CancellationToken);
        }
    }
}
