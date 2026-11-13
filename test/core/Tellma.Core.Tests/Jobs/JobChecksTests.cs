// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     <c>jobs.handlers</c> and <c>jobs.schedules</c>, invoked directly over a realised
    ///     composition: an entity handler's entity is mapped, a built-in naming a key no handler here
    ///     registers is a warning only, and every built-in's cron expression parses and occurs again.
    /// </summary>
    public class JobChecksTests
    {
        private const string Category = "Tellma.Core.Jobs";

        // --- jobs.handlers ---

        [Fact]
        public async Task Handlers_over_mapped_job_entities_and_known_built_ins_pass_silently()
        {
            (IReadOnlyList<CompositionProblem> problems, IReadOnlyList<FakeLogRecord> logs) = await RunHandlersAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => JobTestModel.Contribute(contribution)
                    .BuiltInSchedule(500, "acme.recompute-balances", "0 1 * * *"))));

            Assert.Empty(problems);
            Assert.Empty(logs);
        }

        [Fact]
        public async Task An_entity_handler_over_an_entity_the_model_does_not_map_is_a_problem()
        {
            (IReadOnlyList<CompositionProblem> problems, _) = await RunHandlersAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.JobHandler<UnmappedEntityHandler>())));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(CoreCheckNames.JobHandlers, problem.Source);
            Assert.Contains("processes Tellma.Core.Tests.Jobs.UnmappedJobEntity, which the model does not map as a job entity", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_built_in_whose_key_no_handler_here_registers_is_a_warning_and_never_a_problem()
        {
            (IReadOnlyList<CompositionProblem> problems, IReadOnlyList<FakeLogRecord> logs) = await RunHandlersAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.BuiltInSchedule(500, "acme.elsewhere", "0 1 * * *"))));

            Assert.Empty(problems);
            FakeLogRecord log = Assert.Single(logs);
            Assert.Equal((LogLevel.Warning, "SchedulerHandlerUnknown"), (log.Level, log.Id.Name));
            Assert.Equal(
                "The built-in schedule 500 names the handler key acme.elsewhere, which no handler on this host registers; its jobs wait for a host that runs the handler.",
                log.Message);
        }

        [Fact]
        public async Task The_composed_handlers_check_passes_for_core_alone()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(CoreCheckNames.JobHandlers));
        }

        // --- jobs.schedules ---

        [Theory]
        [InlineData("0 1 * * *")]
        [InlineData("*/15 9-17 * * MON-FRI")]
        [InlineData("0 0 L * *")]
        [InlineData("0 0 * * 5#3")]
        [InlineData("@daily")]
        public async Task An_expression_that_parses_and_occurs_passes(string cron)
        {
            Assert.Empty(await RunSchedulesAsync(cron, null));
        }

        [Fact]
        public async Task An_expression_in_a_zone_this_host_resolves_passes()
        {
            Assert.Empty(await RunSchedulesAsync("30 2 * * *", "Europe/Berlin"));
        }

        [Theory]
        [InlineData("0 1 * *")]
        [InlineData("0 0 1 * * *")]
        [InlineData("61 * * * *")]
        [InlineData("every day")]
        public async Task An_expression_that_does_not_parse_is_a_problem(string cron)
        {
            CompositionProblem problem = Assert.Single(await RunSchedulesAsync(cron, null));

            Assert.Equal(CoreCheckNames.JobSchedules, problem.Source);
            Assert.Equal($"The built-in schedule 500 of the feature 'acme' has the cron expression '{cron}', which does not parse as five-field Unix cron.", problem.Problem);
        }

        [Fact]
        public async Task An_expression_that_never_occurs_is_a_problem()
        {
            CompositionProblem problem = Assert.Single(await RunSchedulesAsync("0 3 30 2 *", null));

            Assert.Equal("The built-in schedule 500 of the feature 'acme' has the cron expression '0 3 30 2 *', which has no next occurrence.", problem.Problem);
        }

        [Fact]
        public async Task The_composed_schedules_check_passes_for_core_alone()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(CoreCheckNames.JobSchedules));
        }

        [Fact]
        public void Neither_check_needs_infrastructure()
        {
            TellmaCompositionRecord record = Record(static _ => { }, out ServiceProvider provider);
            using (provider)
            {
                Assert.False(new JobHandlersCheck(record).RequiresInfrastructure);
                Assert.False(new JobSchedulesCheck(record).RequiresInfrastructure);
            }
        }

        /// <summary>Realises a composition and runs <c>jobs.handlers</c> over it, collecting the job category's logs.</summary>
        /// <param name="compose">The composition method.</param>
        /// <returns>The problems and the job category's logs.</returns>
        private static async Task<(IReadOnlyList<CompositionProblem> Problems, IReadOnlyList<FakeLogRecord> Logs)> RunHandlersAsync(Action<TellmaBuilder> compose)
        {
            FakeLoggerProvider logs = new();
            IServiceCollection services = TestComposition.Compose(compose).AddSingleton<ILoggerFactory>(new LoggerFactory([logs]));
            await using ServiceProvider provider = services.BuildServiceProvider();
            TellmaCompositionRecord record = provider.GetRequiredService<TellmaCompositionRecord>();

            IReadOnlyList<CompositionProblem> problems = await new JobHandlersCheck(record).CheckAsync(provider, TestContext.Current.CancellationToken);
            return (problems, [.. logs.Collector.GetSnapshot().Where(static record => record.Category == Category)]);
        }

        /// <summary>Realises one built-in schedule and runs <c>jobs.schedules</c> over it, on a fixed clock.</summary>
        /// <param name="cron">The built-in's expression.</param>
        /// <param name="timeZoneId">The built-in's zone; null for the tenant's.</param>
        /// <returns>The problems.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> RunSchedulesAsync(string cron, string? timeZoneId)
        {
            // The composition gate holds the expression to its width only, so a malformed one reaches the check.
            TellmaCompositionRecord record = Record(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: contribution => contribution.BuiltInSchedule(500, "acme.digest", cron, timeZoneId: timeZoneId))),
                out ServiceProvider provider);
            await using (provider)
            {
                ServiceCollection clock = [];
                _ = clock.AddSingleton<TimeProvider>(new FakeTimeProvider(new DateTimeOffset(2026, 3, 29, 0, 30, 0, TimeSpan.Zero)));
                await using ServiceProvider checkServices = clock.BuildServiceProvider();
                return await new JobSchedulesCheck(record).CheckAsync(checkServices, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Realises a composition and returns its record.</summary>
        /// <param name="compose">The composition method.</param>
        /// <param name="provider">The provider the record was resolved from; the caller disposes it.</param>
        /// <returns>The record.</returns>
        private static TellmaCompositionRecord Record(Action<TellmaBuilder> compose, out ServiceProvider provider)
        {
            provider = TestComposition.Compose(compose).BuildServiceProvider();
            return provider.GetRequiredService<TellmaCompositionRecord>();
        }
    }
}
