// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Calendars;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.time-zones</c>: a zone of the time-zone set this host cannot resolve is a warning,
    ///     once per zone, and never a startup failure.
    /// </summary>
    public class TimeZonesCheckTests
    {
        private const string Check = "core.time-zones";

        private const string Category = "Tellma.Core.Composition.Checks.TimeZonesCheck";

        [Fact]
        public async Task A_zone_the_host_cannot_resolve_is_one_warning_and_never_a_problem()
        {
            (IReadOnlyList<CompositionProblem> problems, IReadOnlyList<FakeLogRecord> logs) = await RunAsync(
                static id => id is not ("Asia/Kolkata" or "Europe/Kyiv"));

            Assert.Empty(problems);
            Assert.Equal(2, logs.Count);
            Assert.All(logs, static record =>
            {
                Assert.Equal(LogLevel.Warning, record.Level);
                Assert.Equal("TimeZoneUnresolvable", record.Id.Name);
            });
            Assert.Equal(
                ["This host's time-zone data cannot resolve the zone Asia/Kolkata; update the image's tzdata.", "This host's time-zone data cannot resolve the zone Europe/Kyiv; update the image's tzdata."],
                logs.Select(static record => record.Message));
        }

        [Fact]
        public async Task A_host_that_resolves_every_zone_logs_nothing()
        {
            (IReadOnlyList<CompositionProblem> problems, IReadOnlyList<FakeLogRecord> logs) = await RunAsync(static _ => true);

            Assert.Empty(problems);
            Assert.Empty(logs);
        }

        [Fact]
        public async Task A_host_that_resolves_no_zone_warns_once_per_zone_and_still_starts()
        {
            (IReadOnlyList<CompositionProblem> problems, IReadOnlyList<FakeLogRecord> logs) = await RunAsync(static _ => false);

            Assert.Empty(problems);
            Assert.Equal(QueryexTimeZones.Ids.Count, logs.Count);
        }

        [Fact]
        public async Task Only_the_sets_values_are_probed_never_its_aliases()
        {
            List<string> probed = [];
            _ = await RunAsync(id =>
            {
                probed.Add(id);
                return true;
            });

            Assert.Equal(QueryexTimeZones.Ids, probed);
        }

        [Fact]
        public async Task The_check_runs_without_a_logger()
        {
            TimeZonesCheck check = new(static _ => false);
            await using ServiceProvider provider = Services().BuildServiceProvider();

            Assert.Empty(await check.CheckAsync(provider, TestContext.Current.CancellationToken));
        }

        [Fact]
        public void It_reads_the_host_so_a_build_step_skips_it()
        {
            Assert.True(new TimeZonesCheck(TimeZonesCheck.HostResolves).RequiresInfrastructure);
            Assert.Equal(Check, new TimeZonesCheck(TimeZonesCheck.HostResolves).Name);
        }

        [Fact]
        public void The_real_probe_asks_the_hosts_time_zone_data()
        {
            Assert.True(TimeZonesCheck.HostResolves("UTC"));
            Assert.False(TimeZonesCheck.HostResolves("Mars/Olympus_Mons"));
        }

        [Fact]
        public async Task The_composed_check_never_fails_startup_whatever_this_hosts_time_zone_data()
        {
            // Whether this host resolves every zone depends on its tzdata; the outcome does not.
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        /// <summary>Runs the check over the platform's time-zone set with a stand-in for the host's time-zone data.</summary>
        /// <param name="resolves">Whether the host resolves a zone id.</param>
        /// <returns>The problems and what the check logged.</returns>
        private static async Task<(IReadOnlyList<CompositionProblem> Problems, IReadOnlyList<FakeLogRecord> Logs)> RunAsync(Func<string, bool> resolves)
        {
            FakeLoggerProvider logs = new();
            await using ServiceProvider provider = Services()
                .AddSingleton<ILoggerFactory>(new LoggerFactory([logs]))
                .BuildServiceProvider();

            IReadOnlyList<CompositionProblem> problems = await new TimeZonesCheck(resolves).CheckAsync(provider, TestContext.Current.CancellationToken);
            return (problems, [.. logs.Collector.GetSnapshot().Where(static record => record.Category == Category)]);
        }

        /// <summary>The services the check reads: the value-set registry over the platform's languages and calendars.</summary>
        /// <returns>The services.</returns>
        private static IServiceCollection Services()
        {
            return new ServiceCollection().AddSingleton<IValueSetRegistry>(
                new ValueSetRegistry(new LanguageCatalog(LanguageDeclaration.None), new CalendarRegistry([new GregorianCalendarSystem()]), []));
        }
    }
}
