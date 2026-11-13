// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Access;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Jobs
{
    /// <summary>One built-in schedule as the migrations seed it.</summary>
    /// <param name="Id">The row's id, in the reserved band.</param>
    /// <param name="HandlerKey">The handler key, also the row's code and name.</param>
    /// <param name="Cron">The cron expression.</param>
    /// <param name="ArgumentsJson">The arguments as JSON; null for none.</param>
    /// <param name="TimeZoneId">The zone; null for the tenant's.</param>
    internal sealed record BuiltInScheduleSeed(int Id, string HandlerKey, string Cron, string? ArgumentsJson, string? TimeZoneId);

    /// <summary>
    ///     Seeds the built-in schedules the features declared: a <c>core.Schedules</c> row each, run by
    ///     the system user, active, coalescing missed occurrences and skipping overlaps, plus its
    ///     <c>core.ScheduleStates</c> row with nothing scheduled, which the first tick initialises
    ///     without firing.
    /// </summary>
    internal static class BuiltInScheduleSeeds
    {
        /// <summary>Adds the seed rows to the model being built; nothing when there are none.</summary>
        /// <param name="modelBuilder">The model builder, with the schedule's leaf and its state already mapped.</param>
        /// <param name="seeds">The built-in schedules, in declaration order.</param>
        public static void Apply(ModelBuilder modelBuilder, IReadOnlyList<BuiltInScheduleSeed> seeds)
        {
            ArgumentNullException.ThrowIfNull(modelBuilder);
            ArgumentNullException.ThrowIfNull(seeds);
            if (seeds.Count == 0)
            {
                return;
            }

            // --- The schedules, as the leaf the distribution registers; the system user authored them ---
            // spec 0020 §7.9. Seeded values are never changed for a shipped built-in: an UpdateData
            // migration would overwrite a tenant's edits.
            IReadOnlyEntityType schedule = ConventionModel.Leaf(modelBuilder.Model, typeof(Schedule), "The built-in schedules");
            _ = modelBuilder.Entity(schedule.ClrType).HasData([.. seeds.Select(static seed => (object)new
            {
                seed.Id,
                Code = seed.HandlerKey,
                Name = seed.HandlerKey,
                seed.HandlerKey,
                seed.ArgumentsJson,
                CronExpression = seed.Cron,
                seed.TimeZoneId,
                MissedPolicy = MissedPolicy.Coalesce,
                OverlapPolicy = OverlapPolicy.Skip,
                CatchUpWindowMinutes = ScheduleConfiguration<Schedule>.DefaultCatchUpWindowMinutes,
                RunAsUserId = WellKnownIds.SystemUserId,
                IsActive = true,
                CreatedAt = AccessSeeds.SeededAt,
                CreatedById = WellKnownIds.SystemUserId,
                ModifiedAt = AccessSeeds.SeededAt,
                ModifiedById = WellKnownIds.SystemUserId,
            })]);

            // --- Their states, with nothing scheduled until the first tick ---
            _ = modelBuilder.Entity<ScheduleState>().HasData([.. seeds.Select(static seed => new ScheduleState { ScheduleId = seed.Id })]);
        }
    }
}
