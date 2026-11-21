// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data.Common;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    /// <summary>One job a claim leased, with the machinery's columns the entity does not carry.</summary>
    /// <param name="Job">The job's row.</param>
    /// <param name="StateJson">The handler's last checkpoint; null when none.</param>
    /// <param name="ConfirmedKeys">The warning keys the job may pass, comma-separated; null when none.</param>
    /// <param name="LeaseToken">The token the job is leased under.</param>
    internal sealed record ClaimedJob(Job Job, string? StateJson, string? ConfirmedKeys, Guid LeaseToken);

    /// <summary>One job a renewal extended: still running under its lease.</summary>
    /// <param name="Id">The job's id.</param>
    /// <param name="LeaseToken">The token it is leased under.</param>
    /// <param name="CancelRequestedAt">When a cancel was requested; null when none was.</param>
    internal sealed record RenewedJob(int Id, Guid LeaseToken, DateTimeOffset? CancelRequestedAt);

    /// <summary>One job a completion ended or queued again.</summary>
    /// <param name="Id">The job's id.</param>
    /// <param name="Status">Its status after the completion.</param>
    /// <param name="RequestedById">The user notified of the job; null for administrators.</param>
    /// <param name="Attempts">Its claim count after the completion.</param>
    internal sealed record CompletedJob(int Id, JobStatus Status, int? RequestedById, int Attempts);

    /// <summary>
    ///     Reads the rows of <c>core.Jobs</c> a claim returns as <c>[j].*</c>, by column name, so the
    ///     order the table declares its columns in never matters.
    /// </summary>
    internal sealed class JobRowReader
    {
        /// <summary>The ordinals of the columns, by name.</summary>
        private readonly Dictionary<string, int> _ordinals;

        /// <summary>Maps the columns of a result set.</summary>
        /// <param name="reader">The reader positioned on the result set.</param>
        public JobRowReader(DbDataReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);
            _ordinals = new(StringComparer.OrdinalIgnoreCase);
            for (int ordinal = 0; ordinal < reader.FieldCount; ordinal++)
            {
                _ordinals[reader.GetName(ordinal)] = ordinal;
            }
        }

        /// <summary>Reads the current row.</summary>
        /// <param name="reader">The reader positioned on a row.</param>
        /// <returns>The claimed job.</returns>
        /// <exception cref="InvalidOperationException">The row's token is null, or a required column is missing.</exception>
        public ClaimedJob Read(DbDataReader reader)
        {
            ArgumentNullException.ThrowIfNull(reader);

            // --- The entity's members ---
            Job job = new()
            {
                Id = reader.GetInt32(Ordinal(nameof(Job.Id))),
                HandlerKey = reader.GetString(Ordinal(nameof(Job.HandlerKey))),
                LabelKey = Text(reader, nameof(Job.LabelKey)),
                LabelArgumentsJson = Text(reader, nameof(Job.LabelArgumentsJson)),
                Status = Enum.Parse<JobStatus>(reader.GetString(Ordinal(nameof(Job.Status))), ignoreCase: false),
                DueAt = reader.GetFieldValue<DateTimeOffset>(Ordinal(nameof(Job.DueAt))),
                Attempts = reader.GetInt32(Ordinal(nameof(Job.Attempts))),
                LeaseExpiresAt = Instant(reader, nameof(Job.LeaseExpiresAt)),
                LeaseOwner = Text(reader, nameof(Job.LeaseOwner)),
                StartedAt = Instant(reader, nameof(Job.StartedAt)),
                CompletedAt = Instant(reader, nameof(Job.CompletedAt)),
                CancelRequestedAt = Instant(reader, nameof(Job.CancelRequestedAt)),
                ArgumentsJson = Text(reader, nameof(Job.ArgumentsJson)),
                ProgressPercent = reader.IsDBNull(Ordinal(nameof(Job.ProgressPercent))) ? null : reader.GetByte(Ordinal(nameof(Job.ProgressPercent))),
                ProgressKey = Text(reader, nameof(Job.ProgressKey)),
                ProgressArgumentsJson = Text(reader, nameof(Job.ProgressArgumentsJson)),
                ErrorCode = Text(reader, nameof(Job.ErrorCode)),
                ErrorArgumentsJson = Text(reader, nameof(Job.ErrorArgumentsJson)),
                ErrorTraceId = Text(reader, nameof(Job.ErrorTraceId)),
                PendingConfirmationsJson = Text(reader, nameof(Job.PendingConfirmationsJson)),
                TraceParent = Text(reader, nameof(Job.TraceParent)),
                RunAsUserId = reader.GetInt32(Ordinal(nameof(Job.RunAsUserId))),
                RequestedById = Integer(reader, nameof(Job.RequestedById)),
                ScheduleId = Integer(reader, nameof(Job.ScheduleId)),
                ScheduledFor = Instant(reader, nameof(Job.ScheduledFor)),
                CreatedAt = reader.GetFieldValue<DateTimeOffset>(Ordinal(nameof(Job.CreatedAt))),
            };

            // --- The machinery's columns, which the model maps as shadow properties ---
            int token = Ordinal(JobConfiguration.LeaseToken);
            return reader.IsDBNull(token)
                ? throw new InvalidOperationException($"The claimed job {job.Id} carries no lease token.")
                : new ClaimedJob(job, Text(reader, JobConfiguration.StateJson), Text(reader, JobConfiguration.ConfirmedKeys), reader.GetGuid(token));
        }

        /// <summary>The ordinal of a column.</summary>
        /// <param name="name">The column's name.</param>
        /// <returns>The ordinal.</returns>
        /// <exception cref="InvalidOperationException">The result set has no such column.</exception>
        private int Ordinal(string name)
        {
            return _ordinals.TryGetValue(name, out int ordinal)
                ? ordinal
                : throw new InvalidOperationException($"The claim's result set has no column '{name}'.");
        }

        /// <summary>A nullable text column.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="name">The column's name.</param>
        /// <returns>The value; null for SQL <c>NULL</c>.</returns>
        private string? Text(DbDataReader reader, string name)
        {
            int ordinal = Ordinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
        }

        /// <summary>A nullable <c>int</c> column.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="name">The column's name.</param>
        /// <returns>The value; null for SQL <c>NULL</c>.</returns>
        private int? Integer(DbDataReader reader, string name)
        {
            int ordinal = Ordinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetInt32(ordinal);
        }

        /// <summary>A nullable <c>datetimeoffset</c> column.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="name">The column's name.</param>
        /// <returns>The value; null for SQL <c>NULL</c>.</returns>
        private DateTimeOffset? Instant(DbDataReader reader, string name)
        {
            int ordinal = Ordinal(name);
            return reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateTimeOffset>(ordinal);
        }
    }
}
