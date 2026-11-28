// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The model configuration of <c>core.Schedules</c>, closed over the leaf the distribution
    ///     registers for <see cref="Schedule" />: the policies' defaults, the catch-up bound, the
    ///     run-as user and the handler key's index.
    /// </summary>
    /// <remarks>
    ///     The conventions already make the table system-versioned (<c>core.SchedulesHistory</c>), give
    ///     it its sequence <c>core.sq_Schedules</c>, its audit columns, its table type, the unique code
    ///     and the active flag's default; the built-in rows are seeded by <see cref="BuiltInScheduleSeeds" />.
    /// </remarks>
    /// <typeparam name="TSchedule">The leaf.</typeparam>
    internal sealed class ScheduleConfiguration<TSchedule> : IEntityTypeConfiguration<TSchedule>
        where TSchedule : Schedule
    {
        /// <summary>The default of <see cref="Schedule.CatchUpWindowMinutes" />: one day.</summary>
        public const int DefaultCatchUpWindowMinutes = 1440;

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TSchedule> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Defaults: the policies and the catch-up window a new row takes when it names none ---
            _ = builder.Property(static schedule => schedule.MissedPolicy).HasDefaultValue(MissedPolicy.Coalesce);
            _ = builder.Property(static schedule => schedule.OverlapPolicy).HasDefaultValue(OverlapPolicy.Skip);
            _ = builder.Property(static schedule => schedule.CatchUpWindowMinutes).HasDefaultValue(DefaultCatchUpWindowMinutes);

            // --- The user the schedule's jobs run as, whichever leaf the distribution registers ---
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The run-as user of core.Schedules");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Schedule.RunAsUserId))
                .OnDelete(DeleteBehavior.NoAction);

            // --- The catch-up window is a positive span ---
            _ = builder.ToTable(static table => table.HasCheckConstraint("CK_Schedules_CatchUp", "[CatchUpWindowMinutes] > 0"));

            // --- The schedules of a handler key: the retired-key pause and the handler's schedules ---
            _ = builder.HasIndex(static schedule => schedule.HandlerKey).HasDatabaseName("IX_Schedules_HandlerKey");
        }
    }
}
