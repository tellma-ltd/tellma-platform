// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The job contributions at the composition gate: handlers, built-in schedules and retired
    ///     keys, each held to its rules, and the problems only the whole composition shows.
    /// </summary>
    public class JobContributionTests
    {
        // --- Sound contributions ---

        [Fact]
        public void Sound_handlers_built_ins_and_retired_keys_reach_the_registry()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution
                .JobHandler<RecomputeHandler>()
                .JobHandler<ShipHandler>()
                .BuiltInSchedule(500, "acme.recompute-balances", "0 1 * * *", new { Depth = 2 }, "Asia/Kolkata")
                .BuiltInSchedule(100, "acme.elsewhere", "@daily")
                .RetiredJobKey("acme.legacy")
                .RetiredJobKey("acme.legacy"))));

            using ServiceProvider provider = services.BuildServiceProvider();
            JobHandlerRegistry registry = provider.GetRequiredService<JobHandlerRegistry>();

            Assert.Equal(["acme.recompute-balances", "acme.ship"], registry.Handlers.Select(static handler => handler.Key));
            Assert.True(registry.TryFind("acme.ship", out JobHandlerRegistration? ship));
            Assert.Equal((typeof(ShipHandler), typeof(Shipment)), (ship.HandlerType, ship.EntityType));
            Assert.True(registry.TryFind("acme.recompute-balances", out JobHandlerRegistration? recompute));
            Assert.Null(recompute.EntityType);
            Assert.True(recompute.Options.Schedulable);
            Assert.False(registry.TryFind("acme.unknown", out _));

            // A key retired twice is retired once; the built-ins keep their declaration order.
            Assert.Equal(["acme.legacy"], registry.RetiredKeys);
            Assert.True(registry.IsRetired("acme.legacy"));
            Assert.Equal(
                [
                    new BuiltInScheduleSeed(500, "acme.recompute-balances", "0 1 * * *", /*lang=json,strict*/ """{"depth":2}""", "Asia/Kolkata"),
                    new BuiltInScheduleSeed(100, "acme.elsewhere", "@daily", null, null),
                ],
                registry.BuiltInSchedules);

            // Each handler resolves per invocation from the job's scope.
            Assert.Equal(ServiceLifetime.Scoped, services.Single(static descriptor => descriptor.ServiceType == typeof(ShipHandler)).Lifetime);
        }

        [Fact]
        public void The_queue_is_scoped_and_the_registry_and_wake_are_singletons()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });

            Assert.Equal(ServiceLifetime.Scoped, services.Single(static descriptor => descriptor.ServiceType == typeof(IJobQueue)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.Single(static descriptor => descriptor.ServiceType == typeof(JobHandlerRegistry)).Lifetime);
            Assert.Equal(ServiceLifetime.Singleton, services.Single(static descriptor => descriptor.ServiceType == typeof(TenantPollerWake)).Lifetime);
        }

        [Fact]
        public void The_built_ins_are_seeded_into_the_schedules_with_their_states()
        {
            using AccessModel model = new(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution
                .BuiltInSchedule(500, "acme.recompute-balances", "0 1 * * *", new { Depth = 2 }, "Asia/Kolkata"))));

            // --- The schedule, authored by the system user, its key standing in for its code and name ---
            IDictionary<string, object?> schedule = Assert.Single(model.Table("Schedules").GetSeedData());
            Assert.Equal(500, schedule["Id"]);
            Assert.Equal(("acme.recompute-balances", "acme.recompute-balances", "acme.recompute-balances"), (schedule["HandlerKey"], schedule["Code"], schedule["Name"]));
            Assert.Equal(("0 1 * * *", "Asia/Kolkata", /*lang=json,strict*/ """{"depth":2}"""), (schedule["CronExpression"], schedule["TimeZoneId"], schedule["ArgumentsJson"]));
            Assert.Equal((MissedPolicy.Coalesce, OverlapPolicy.Skip, 1440), (schedule["MissedPolicy"], schedule["OverlapPolicy"], schedule["CatchUpWindowMinutes"]));
            Assert.Equal((WellKnownIds.SystemUserId, true), (schedule["RunAsUserId"], schedule["IsActive"]));
            Assert.Equal((WellKnownIds.SystemUserId, WellKnownIds.SystemUserId), (schedule["CreatedById"], schedule["ModifiedById"]));

            // --- Its state, with nothing scheduled until the first tick ---
            IDictionary<string, object?> state = Assert.Single(model.Table("ScheduleStates").GetSeedData());
            Assert.Equal(500, state["ScheduleId"]);
            Assert.Null(state["NextDueAt"]);
            Assert.Null(state["LastJobId"]);
        }

        // --- Handlers ---

        [Fact]
        public void A_handler_without_the_attribute_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<UnattributedHandler>()));

            Assert.Contains("carries no [JobHandler] attribute", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_abstract_handler_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<AbstractHandler>()));

            Assert.Contains("which is not a concrete, closed class", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_class_with_neither_shape_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<ShapelessHandler>()));

            Assert.Contains("implements neither; a handler implements exactly one of IJobHandler and IEntityJobHandler<TEntity>", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_class_with_both_shapes_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<BothShapesHandler>()));

            Assert.Contains("implements IJobHandler and IEntityJobHandler<Shipment>", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_key_outside_the_grammar_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<MalformedKeyHandler>()));

            Assert.Contains("is declared out of range: The key 'Acme_Bad' is not two or more dot-separated segments", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_option_out_of_range_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<OversizedHandler>()));

            Assert.Contains("is declared out of range", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("BatchSize", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_platform_prefix_is_refused_to_a_feature()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.JobHandler<PlatformKeyHandler>()));

            Assert.Contains("has the key 'core.impostor', but the prefix 'core.' is reserved for the platform", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_key_registered_by_two_features_is_refused_naming_both()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.JobHandler<RecomputeHandler>()))
                .AddFeature(new TestFeature<Tag.B>("gl", contribute: static contribution => contribution.JobHandler<DuplicateRecomputeHandler>())));

            CompositionProblem problem = Assert.Single(problems);
            Assert.StartsWith("The job handler key 'acme.recompute-balances' is registered more than once, by ", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("of 'acme'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("of 'gl'", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_key_both_registered_and_retired_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution
                .JobHandler<RecomputeHandler>()
                .RetiredJobKey("acme.recompute-balances")));

            Assert.Contains("registers the key 'acme.recompute-balances', which a feature retired", problem.Problem, StringComparison.Ordinal);
        }

        // --- Built-in schedules ---

        [Theory]
        [InlineData(0)]
        [InlineData(-5)]
        [InlineData(1000)]
        public void A_built_in_outside_the_reserved_band_is_refused(int id)
        {
            CompositionProblem problem = Assert.Single(Problems(contribution => contribution.BuiltInSchedule(id, "acme.digest", "0 3 * * *")));

            Assert.Contains($"is outside the reserved band of 1 to {WellKnownIds.ReservedIdBandEnd}", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(99)]
        public void The_platforms_share_of_the_band_is_refused_to_a_feature(int id)
        {
            CompositionProblem problem = Assert.Single(Problems(contribution => contribution.BuiltInSchedule(id, "acme.digest", "0 3 * * *")));

            Assert.Contains("uses an id from 1 to 99, which are reserved for the platform's own built-ins", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(100)]
        [InlineData(499)]
        [InlineData(500)]
        [InlineData(999)]
        public void A_feature_may_use_the_pack_and_distribution_bands(int id)
        {
            _ = TestComposition.Compose(tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: contribution => contribution.BuiltInSchedule(id, "acme.digest", "0 3 * * *"))));
        }

        [Fact]
        public void A_built_in_key_outside_the_grammar_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.BuiltInSchedule(500, "digest", "0 3 * * *")));

            Assert.Contains("names the handler key 'digest', which is not two or more dot-separated segments", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void A_built_in_without_an_expression_is_refused(string cron)
        {
            CompositionProblem problem = Assert.Single(Problems(contribution => contribution.BuiltInSchedule(500, "acme.digest", cron)));

            Assert.Contains("has no cron expression, or one longer than 128 characters", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_built_in_expression_wider_than_its_column_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.BuiltInSchedule(500, "acme.digest", "0 3 * * " + new string('1', 121))));

            Assert.Contains("has no cron expression, or one longer than 128 characters", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_built_in_zone_outside_the_set_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.BuiltInSchedule(500, "acme.digest", "0 3 * * *", timeZoneId: "Mars/Olympus")));

            Assert.Equal("The built-in schedule 500 of the feature 'acme' names the time zone 'Mars/Olympus', which is not in the platform's time-zone set.", problem.Problem);
        }

        [Fact]
        public void A_built_in_zone_alias_is_refused_naming_its_canonical_zone()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.BuiltInSchedule(500, "acme.digest", "0 3 * * *", timeZoneId: "Asia/Calcutta")));

            Assert.EndsWith("It is an alias of 'Asia/Kolkata'; name that instead.", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Built_in_arguments_over_64_KB_are_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.BuiltInSchedule(500, "acme.digest", "0 3 * * *", new { Text = new string('x', JobLimits.MaxHandlerArgumentsBytes) })));

            Assert.Contains($"has arguments of more than {JobLimits.MaxHandlerArgumentsBytes} bytes once serialised", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_built_in_id_declared_twice_is_refused_naming_both_features()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static contribution => contribution.BuiltInSchedule(500, "acme.digest", "0 3 * * *")))
                .AddFeature(new TestFeature<Tag.B>("gl", contribute: static contribution => contribution.BuiltInSchedule(500, "gl.close", "0 4 * * *"))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("The built-in schedule id 500 is declared more than once, by 'acme' and 'gl'.", problem.Problem);
        }

        [Fact]
        public void A_built_in_key_declared_twice_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution
                .BuiltInSchedule(500, "acme.digest", "0 3 * * *")
                .BuiltInSchedule(501, "acme.digest", "0 4 * * *")));

            Assert.Equal("The handler key 'acme.digest' has more than one built-in schedule, declared by 'acme'.", problem.Problem);
        }

        [Fact]
        public void A_built_in_naming_a_retired_key_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution
                .BuiltInSchedule(500, "acme.legacy", "0 3 * * *")
                .RetiredJobKey("acme.legacy")));

            Assert.Equal("The built-in schedule 500 of the feature 'acme' names the key 'acme.legacy', which a feature retired.", problem.Problem);
        }

        // --- Retired keys ---

        [Fact]
        public void A_retired_key_outside_the_grammar_is_refused()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.RetiredJobKey("Legacy")));

            Assert.Contains("retires the job handler key 'Legacy', which is not two or more dot-separated segments", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_feature_may_not_retire_a_platform_key()
        {
            CompositionProblem problem = Assert.Single(Problems(static contribution => contribution.RetiredJobKey("core.job-retention")));

            Assert.Contains("but the prefix 'core.' is reserved for the platform, which alone retires its keys", problem.Problem, StringComparison.Ordinal);
        }

        /// <summary>Composes one feature named <c>acme</c>, expecting refusal, and returns the problems.</summary>
        /// <param name="contribute">What the feature contributes.</param>
        /// <returns>The problems.</returns>
        private static IReadOnlyList<CompositionProblem> Problems(Action<FeatureContribution> contribute)
        {
            return TestComposition.Problems(tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: contribute)));
        }
    }
}
