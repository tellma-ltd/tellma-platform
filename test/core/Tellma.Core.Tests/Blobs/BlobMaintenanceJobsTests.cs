// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Blobs;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The sweep and reconcile jobs as Core contributes them: their handler options, their
    ///     built-in schedules and the schedule rows those seed, the object names a row stands for, and
    ///     the state tag the sweep meters by.
    /// </summary>
    public sealed class BlobMaintenanceJobsTests
    {
        [Fact]
        public void The_sweep_and_the_reconcile_run_one_at_a_time_with_their_leases_and_on_a_read_only_tenant()
        {
            using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider();
            JobHandlerRegistry registry = provider.GetRequiredService<JobHandlerRegistry>();

            Assert.True(registry.TryFind(BlobSweepHandler.Key, out JobHandlerRegistration? sweep));
            Assert.True(registry.TryFind(BlobReconcileHandler.Key, out JobHandlerRegistration? reconcile));
            Assert.Equal(("core.blob-sweep", "core.blob-reconcile"), (sweep.Key, reconcile.Key));
            Assert.Equal((1, 600, 3, 1, true, false), Options(sweep.Options));
            Assert.Equal((1, 900, 3, 1, true, false), Options(reconcile.Options));
            Assert.Null(sweep.EntityType);
            Assert.Null(reconcile.EntityType);
        }

        [Fact]
        public void Both_built_ins_come_with_every_composition_in_the_platforms_band()
        {
            using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider();
            IReadOnlyList<BuiltInScheduleSeed> seeds = provider.GetRequiredService<JobHandlerRegistry>().BuiltInSchedules;

            Assert.Contains(new BuiltInScheduleSeed(4, "core.blob-sweep", "*/15 * * * *", null, null), seeds);
            Assert.Contains(new BuiltInScheduleSeed(5, "core.blob-reconcile", "0 2 * * 6", null, null), seeds);
        }

        [Fact]
        public void Both_built_ins_seed_schedule_rows_run_by_the_system_user_that_skip_overlaps_and_coalesce_missed_runs()
        {
            using AccessModel model = new();
            IReadOnlyList<IDictionary<string, object?>> schedules = [.. model.Table("Schedules").GetSeedData()];

            foreach ((int id, string key, string cron) in new[] { (4, "core.blob-sweep", "*/15 * * * *"), (5, "core.blob-reconcile", "0 2 * * 6") })
            {
                IDictionary<string, object?> row = Assert.Single(schedules, row => Equals(row["Id"], id));
                Assert.Equal((key, key, key, cron), (row["HandlerKey"], row["Code"], row["Name"], row["CronExpression"]));
                Assert.Equal((MissedPolicy.Coalesce, OverlapPolicy.Skip, WellKnownIds.SystemUserId, true), (row["MissedPolicy"], row["OverlapPolicy"], row["RunAsUserId"], row["IsActive"]));
                _ = Assert.Single(model.Table("ScheduleStates").GetSeedData(), state => Equals(state["ScheduleId"], id));
            }
        }

        [Theory]
        [InlineData("bm-scan/ab/ab0123456789abcdef0123456789abcd", "", new[] { "bm-scan/ab/ab0123456789abcdef0123456789abcd" })]
        [InlineData("bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb", new[] { "bm-scan/ab/ab0123456789abcdef0123456789abcd", "bm-scan/ab/ab0123456789abcdef0123456789abcd.thumb" })]
        public void A_rows_object_names_are_its_primary_then_each_listed_variant(string storageKey, string variants, string[] expected)
        {
            Assert.Equal(expected, BlobObjectNames.Of(storageKey, variants));
        }

        [Theory]
        [InlineData("bm-scan/ab/AB0123456789abcdef0123456789abcd", "")]
        [InlineData("bm-scan/ab/../etc", "")]
        [InlineData("bm-scan/ab/ab0123456789abcdef0123456789abcd", "Thumb")]
        [InlineData("bm-scan/ab/ab0123456789abcdef0123456789abcd", "thumb,")]
        public void A_row_outside_the_storage_grammar_names_no_objects(string storageKey, string variants)
        {
            Assert.Null(BlobObjectNames.Of(storageKey, variants));
        }

        [Fact]
        public void The_sweep_meters_each_state_it_claims_from_by_its_spelled_out_value()
        {
            Assert.Equal(
                [BlobTelemetryTagValues.StateStaged, BlobTelemetryTagValues.StateReleased, BlobTelemetryTagValues.StateDeleting],
                new[] { BlobState.Staged, BlobState.Released, BlobState.Deleting }.Select(BlobMaintenanceMetrics.StateValue));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => BlobMaintenanceMetrics.StateValue(BlobState.Committed));
        }

        /// <summary>The options the sweep and reconcile pin.</summary>
        /// <param name="options">A handler's options.</param>
        /// <returns>Its batch size, lease, attempts, concurrency, read-only flag and schedulable flag.</returns>
        private static (int, int, int, int, bool, bool) Options(JobHandlerAttribute options)
        {
            return (options.BatchSize, options.LeaseSeconds, options.MaxAttempts, options.MaxConcurrency, options.RunsWhenReadOnly, options.Schedulable);
        }
    }
}
