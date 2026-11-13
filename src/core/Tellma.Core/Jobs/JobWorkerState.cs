// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The single row of <c>core.JobWorkerState</c>: when a worker last finished a poll of the
    ///     tenant, and which instance did. A poll that finds the stamp older than the gap threshold
    ///     holds the stale backlog for an administrator. Mapped for the migrations only.
    /// </summary>
    internal sealed class JobWorkerState
    {
        /// <summary>The id of the one row.</summary>
        public const int RowId = 1;

        /// <summary>The row's id, always <see cref="RowId" />.</summary>
        public int Id { get; set; }

        /// <summary>When a poll last stamped the row; null until the first poll, so a new tenant sees no gap.</summary>
        public DateTimeOffset? LastTickAt { get; set; }

        /// <summary>The lease owner of the poll that last stamped the row.</summary>
        public string? LastTickOwner { get; set; }
    }

    /// <summary>The model configuration of <c>core.JobWorkerState</c>: one row, seeded with no stamp.</summary>
    internal sealed class JobWorkerStateConfiguration : IEntityTypeConfiguration<JobWorkerState>
    {
        /// <summary>The table's name.</summary>
        public const string Table = "JobWorkerState";

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<JobWorkerState> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The table, keyed by a constant id: the positive key every keyed table carries, and the single row ---
            _ = builder.ToTable(Table, "core", static table =>
            {
                _ = table.HasCheckConstraint(ConstraintNames.Check(Table, PositiveKeyConvention.ConstraintName), "[Id] > 0");
                _ = table.HasCheckConstraint(ConstraintNames.Check(Table, "SingleRow"), $"[Id] = {JobWorkerState.RowId}");
            });
            _ = builder.HasKey(static state => state.Id);
            _ = builder.Property(static state => state.Id).ValueGeneratedNever();
            _ = builder.Property(static state => state.LastTickOwner).HasMaxLength(JobConfiguration.LeaseOwnerLength);

            // --- The row, unstamped: the first poll of a new tenant sees no gap ---
            _ = builder.HasData(new JobWorkerState { Id = JobWorkerState.RowId });
        }
    }
}
