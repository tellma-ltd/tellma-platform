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
using Tellma.Core.EntityFrameworkCore.LockEscalation;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.Jobs
{
    /// <summary>
    ///     The model configuration of <c>core.Jobs</c>: the column widths the class does not carry, the
    ///     machinery's three model-only columns, the users and the schedule a job references, the
    ///     state checks, the filtered indexes the statements seek, and no lock escalation.
    /// </summary>
    /// <remarks>
    ///     The conventions already give the table its sequence <c>core.sq_Jobs</c>, its positive-key
    ///     check, its <c>datetimeoffset(3)</c> timestamps, the status as <c>varchar(19)</c> text, and its
    ///     table type over every column but the three model-only ones excluded here.
    /// </remarks>
    internal sealed class JobConfiguration : IEntityTypeConfiguration<Job>
    {
        /// <summary>The handler's checkpoint, <c>nvarchar(max)</c>: a model-only column the class does not carry.</summary>
        public const string StateJson = "StateJson";

        /// <summary>The lease's fencing token, <c>uniqueidentifier</c>: a model-only column the class does not carry.</summary>
        public const string LeaseToken = "LeaseToken";

        /// <summary>
        ///     The warning keys the job may pass, comma-separated, <c>varchar(2000)</c>: a model-only
        ///     column the class does not carry.
        /// </summary>
        public const string ConfirmedKeys = "ConfirmedKeys";

        /// <summary>The most characters <see cref="ConfirmedKeys" /> holds.</summary>
        public const int ConfirmedKeysLength = 2000;

        /// <summary>The characters of a W3C <c>traceparent</c>, the width of <see cref="Job.TraceParent" />.</summary>
        public const int TraceParentLength = 55;

        /// <summary>The characters of a W3C trace id, the width of <see cref="Job.ErrorTraceId" />.</summary>
        public const int TraceIdLength = 32;

        /// <summary>The width of <see cref="Job.LeaseOwner" />, to which a longer owner is truncated.</summary>
        public const int LeaseOwnerLength = 128;

        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Job> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Column widths: keys, their arguments and the diagnostics ---
            _ = builder.Property(static job => job.HandlerKey).HasMaxLength(JobLimits.MaxKeyLength);
            _ = builder.Property(static job => job.LabelKey).HasMaxLength(JobLimits.MaxKeyLength);
            _ = builder.Property(static job => job.LabelArgumentsJson).HasMaxLength(JobLimits.MaxArgumentsLength);
            _ = builder.Property(static job => job.ProgressKey).HasMaxLength(JobLimits.MaxKeyLength);
            _ = builder.Property(static job => job.ProgressArgumentsJson).HasMaxLength(JobLimits.MaxArgumentsLength);
            _ = builder.Property(static job => job.ErrorCode).HasMaxLength(JobLimits.MaxKeyLength);
            _ = builder.Property(static job => job.ErrorArgumentsJson).HasMaxLength(JobLimits.MaxArgumentsLength);
            _ = builder.Property(static job => job.ErrorTraceId).HasMaxLength(TraceIdLength).IsUnicode(false);
            _ = builder.Property(static job => job.TraceParent).HasMaxLength(TraceParentLength);
            _ = builder.Property(static job => job.LeaseOwner).HasMaxLength(LeaseOwnerLength);
            _ = builder.Property(static job => job.Attempts).HasDefaultValue(0);

            // --- The machinery's columns: shadow properties, absent from the class, the queries and the table type ---
            // spec 0020 §2.3: the worker reads them from the claim's result set.
            _ = builder.Property<string>(StateJson).ExcludeFromTableType();
            _ = builder.Property<Guid?>(LeaseToken).ExcludeFromTableType();
            _ = builder.Property<string>(ConfirmedKeys).HasMaxLength(ConfirmedKeysLength).IsUnicode(false).ExcludeFromTableType();

            // --- The users a job runs as and notifies, whichever leaf the distribution registers ---
            IReadOnlyEntityType user = ConventionModel.Leaf(builder.Metadata.Model, typeof(User), "The run-as user of core.Jobs");
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Job.RunAsUserId))
                .OnDelete(DeleteBehavior.NoAction);
            _ = builder.HasOne(user.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Job.RequestedById))
                .OnDelete(DeleteBehavior.NoAction);

            // --- The schedule that fired the job; deleting the schedule keeps its jobs ---
            IReadOnlyEntityType schedule = ConventionModel.Leaf(builder.Metadata.Model, typeof(Schedule), "The schedule of core.Jobs");
            _ = builder.HasOne(schedule.ClrType)
                .WithMany()
                .HasForeignKey(nameof(Job.ScheduleId))
                .OnDelete(DeleteBehavior.SetNull);

            // --- Checks: a lease exactly while running, the progress bound, warnings exactly while pending confirmation ---
            string running = Quoted(JobStatus.Running);
            string pendingConfirmation = Quoted(JobStatus.PendingConfirmation);
            _ = builder.ToTable(table =>
            {
                _ = table.HasCheckConstraint(
                    "CK_Jobs_Lease",
                    $"([Status] = {running} AND [LeaseToken] IS NOT NULL AND [LeaseExpiresAt] IS NOT NULL) OR ([Status] <> {running} AND [LeaseToken] IS NULL AND [LeaseExpiresAt] IS NULL)");
                _ = table.HasCheckConstraint("CK_Jobs_Progress", $"[ProgressPercent] <= {JobLimits.MaxProgressPercent}");
                _ = table.HasCheckConstraint(
                    "CK_Jobs_PendingConfirmations",
                    $"([Status] = {pendingConfirmation} AND [PendingConfirmationsJson] IS NOT NULL) OR ([Status] <> {pendingConfirmation} AND [PendingConfirmationsJson] IS NULL)");
            });

            // --- The filtered indexes, each the seek of one statement ---
            // spec 0020 §2.1: every statement that seeks one repeats its filter literals verbatim.
            string available = In(JobStatus.Pending, JobStatus.Running);
            string active = In(JobStatus.Pending, JobStatus.Running, JobStatus.Held, JobStatus.PendingConfirmation);
            string terminal = In(JobStatus.Succeeded, JobStatus.Failed, JobStatus.Cancelled);

            // The claim: due rows of one key, in order, with the lease to test.
            _ = builder.HasIndex(static job => new { job.HandlerKey, job.DueAt, job.Id })
                .HasDatabaseName("IX_Jobs_Available")
                .HasFilter($"[Status] {available}")
                .IncludeProperties(static job => job.LeaseExpiresAt);

            // Renewal and completion: the rows of one lease token.
            _ = builder.HasIndex(LeaseToken)
                .HasDatabaseName("IX_Jobs_Lease")
                .HasFilter("[LeaseToken] IS NOT NULL")
                .IncludeProperties(nameof(Job.Status));

            // The tick's overlap guard: a schedule's active jobs.
            _ = builder.HasIndex(static job => job.ScheduleId)
                .HasDatabaseName("IX_Jobs_ScheduleActive")
                .HasFilter($"[Status] {active} AND [ScheduleId] IS NOT NULL");

            // "My jobs": one requester's jobs, newest first.
            _ = builder.HasIndex(static job => new { job.RequestedById, job.CreatedAt })
                .HasDatabaseName("IX_Jobs_RequestedBy")
                .IsDescending(false, true)
                .HasFilter("[RequestedById] IS NOT NULL");

            // Retention: ended rows by status and age.
            _ = builder.HasIndex(static job => new { job.Status, job.CompletedAt })
                .HasDatabaseName("IX_Jobs_Retention")
                .HasFilter($"[Status] {terminal}");

            // The gap hold's per-requester counts.
            _ = builder.HasIndex(static job => job.RequestedById)
                .HasDatabaseName("IX_Jobs_Held")
                .HasFilter($"[Status] = {Quoted(JobStatus.Held)}");

            // Retention's stop of rows pending confirmation for too long.
            _ = builder.HasIndex(static job => job.DueAt)
                .HasDatabaseName("IX_Jobs_PendingConfirmation")
                .HasFilter($"[Status] = {pendingConfirmation}");

            // The inbox's running-jobs read: one requester's active jobs, newest first.
            _ = builder.HasIndex(static job => new { job.RequestedById, job.CreatedAt, job.Id })
                .HasDatabaseName("IX_Jobs_RequestedActive")
                .IsDescending(false, true, true)
                .HasFilter($"[Status] {active} AND [RequestedById] IS NOT NULL")
                .IncludeProperties(static job => job.ScheduledFor);

            // --- Statements touching many jobs at once never escalate to a table lock ---
            _ = builder.HasLockEscalation(LockEscalationMode.Disable);
        }

        /// <summary>A status as the SQL literal its column stores.</summary>
        /// <param name="status">The status.</param>
        /// <returns>The quoted name.</returns>
        public static string Quoted(JobStatus status)
        {
            return $"'{status}'";
        }

        /// <summary>An <c>IN</c> list of statuses as SQL literals.</summary>
        /// <param name="statuses">The statuses.</param>
        /// <returns><c>IN ('A', 'B')</c>.</returns>
        public static string In(params JobStatus[] statuses)
        {
            return $"IN ({string.Join(", ", statuses.Select(Quoted))})";
        }
    }
}
