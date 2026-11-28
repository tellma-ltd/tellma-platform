// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     A row of <c>core.ScheduleStates</c>: the scheduler's bookkeeping for one schedule, kept off
    ///     the schedule's own row so that a tick writes no history and never touches the row's
    ///     concurrency stamp. Mapped for the migrations only; the scheduler's statements read and write
    ///     it, and it is no entity, so no query reaches it.
    /// </summary>
    internal sealed class ScheduleState
    {
        /// <summary>The schedule, the key; the row is deleted with it.</summary>
        public int ScheduleId { get; set; }

        /// <summary>
        ///     When the schedule next comes due; null when nothing is scheduled: an inactive or paused
        ///     schedule, or a built-in the first tick has not initialised yet.
        /// </summary>
        public DateTimeOffset? NextDueAt { get; set; }

        /// <summary>When the schedule last fired a job.</summary>
        public DateTimeOffset? LastFiredAt { get; set; }

        /// <summary>The occurrence the schedule last fired for.</summary>
        public DateTimeOffset? LastScheduledFor { get; set; }

        /// <summary>When the schedule last skipped an occurrence because its previous job was still active.</summary>
        public DateTimeOffset? LastSkippedAt { get; set; }

        /// <summary>The job the schedule last fired; null once retention deleted it.</summary>
        public int? LastJobId { get; set; }

        /// <summary>The token of the tick that leased the row; null when no tick holds it.</summary>
        public Guid? LeaseToken { get; set; }

        /// <summary>When the tick's lease lapses.</summary>
        public DateTimeOffset? LeaseExpiresAt { get; set; }
    }

    /// <summary>
    ///     The model configuration of <c>core.ScheduleStates</c>: keyed by its schedule and deleted with
    ///     it, its last job cleared when retention deletes the job, and the two seeks of the tick.
    /// </summary>
    internal sealed class ScheduleStateConfiguration : IEntityTypeConfiguration<ScheduleState>
    {
        /// <summary>The table's name.</summary>
        public const string Table = "ScheduleStates";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<ScheduleState> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table and its key: one row per schedule ---
            _ = builder.ToTable(Table, "core");
            _ = builder.HasKey(static state => state.ScheduleId);
            _ = builder.Property(static state => state.ScheduleId).ValueGeneratedNever();

            // --- The schedule, whichever leaf the distribution registers; the row goes with it ---
            IReadOnlyEntityType schedule = ConventionModel.Leaf(builder.Metadata.Model, typeof(Schedule), "The schedule of core.ScheduleStates");
            _ = builder.HasOne(schedule.ClrType)
                .WithOne()
                .HasForeignKey(typeof(ScheduleState), nameof(ScheduleState.ScheduleId))
                .OnDelete(DeleteBehavior.Cascade);

            // --- The last job fired; retention deleting it clears the reference ---
            _ = builder.HasOne<Job>()
                .WithMany()
                .HasForeignKey(static state => state.LastJobId)
                .OnDelete(DeleteBehavior.SetNull);

            // --- The tick's seeks: the due rows, and the rows one tick token leased ---
            _ = builder.HasIndex(static state => state.NextDueAt)
                .HasDatabaseName("IX_ScheduleStates_Due")
                .HasFilter("[NextDueAt] IS NOT NULL");
            _ = builder.HasIndex(static state => state.LeaseToken)
                .HasDatabaseName("IX_ScheduleStates_Lease")
                .HasFilter("[LeaseToken] IS NOT NULL");
        }
    }
}
