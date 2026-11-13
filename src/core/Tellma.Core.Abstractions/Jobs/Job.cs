// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     A row of <c>core.Jobs</c>: one durable unit of background work, run by the handler its
    ///     <see cref="HandlerKey" /> names. System-written: only the job machinery's own statements
    ///     insert and update it, never a save, and its stack serves reads alone.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Every member is server-owned. The table also holds three columns the class does not
    ///         carry — the handler's checkpoint, the lease token and the confirmed warning keys — so
    ///         none of them ever reaches a client or a query; the worker reads them from its claim.
    ///     </para>
    ///     <para>
    ///         A business row processed in the background references its job through
    ///         <see cref="IJobEntity.JobId" />; the job never references the row. A job's display text
    ///         is rendered by its reader: <see cref="LabelKey" /> with its arguments when set, else the
    ///         handler's label resource (<see cref="JobHandlerKeys.LabelKeyOf(string)" />), else the
    ///         handler key itself.
    ///     </para>
    /// </remarks>
    [Table("Jobs", Schema = "core")]
    [TableType]
    [Stack(Operations = StackOperations.Query | StackOperations.Details)]
    [DefaultSelect("Id,HandlerKey,LabelKey,LabelArgumentsJson,Status,DueAt,Attempts,LeaseExpiresAt,StartedAt,CompletedAt,CancelRequestedAt,ProgressPercent,ProgressKey,ProgressArgumentsJson,ErrorCode,ErrorArgumentsJson,ErrorTraceId,RunAsUserId,RequestedById,ScheduleId,ScheduledFor,CreatedAt")]
    [RelatedSelect("Id,HandlerKey,Status,ProgressPercent,LabelKey,LabelArgumentsJson,CompletedAt")]
    public sealed class Job : Entity<int>
    {
        /// <summary>The key of the handler that runs the job, <c>nvarchar(64)</c>, in the handler key grammar.</summary>
        [ServerOwned]
        public string HandlerKey { get; set; } = null!;

        /// <summary>
        ///     The resource key of the job's display label, <c>nvarchar(64)</c>, set at enqueue; null for
        ///     the handler's own label.
        /// </summary>
        [ServerOwned]
        public string? LabelKey { get; set; }

        /// <summary>The arguments of <see cref="LabelKey" /> as a JSON object, <c>nvarchar(4000)</c>; null for none.</summary>
        [ServerOwned]
        public string? LabelArgumentsJson { get; set; }

        /// <summary>Where the job is in its life.</summary>
        [ServerOwned]
        public JobStatus Status { get; set; }

        /// <summary>
        ///     The earliest time a claim may take the job, <c>datetimeoffset(3)</c>, moved by a retry's
        ///     backoff and by the administrative actions that queue the job again; while the job is
        ///     <see cref="JobStatus.PendingConfirmation" />, the time it began waiting.
        /// </summary>
        [ServerOwned]
        public DateTimeOffset DueAt { get; set; }

        /// <summary>
        ///     How many times the job has been claimed: the claim adds one, a graceful release takes it
        ///     back, and an administrator's retry or a confirmation resets it to zero.
        /// </summary>
        [ServerOwned]
        public int Attempts { get; set; }

        /// <summary>When the running job's lease lapses, <c>datetimeoffset(3)</c>; null unless <see cref="JobStatus.Running" />.</summary>
        [ServerOwned]
        public DateTimeOffset? LeaseExpiresAt { get; set; }

        /// <summary>
        ///     The instance running the job, <c>nvarchar(128)</c>: its instance id and process id. For
        ///     diagnosis only; null unless <see cref="JobStatus.Running" />.
        /// </summary>
        [ServerOwned]
        public string? LeaseOwner { get; set; }

        /// <summary>When the job was first claimed, <c>datetimeoffset(3)</c>; null until then.</summary>
        [ServerOwned]
        public DateTimeOffset? StartedAt { get; set; }

        /// <summary>When the job ended, <c>datetimeoffset(3)</c>; null unless it is in a terminal state.</summary>
        [ServerOwned]
        public DateTimeOffset? CompletedAt { get; set; }

        /// <summary>
        ///     When someone asked to stop the job while it was running or waiting with a checkpoint,
        ///     <c>datetimeoffset(3)</c>; the handler sees the request through its item's cancellation.
        ///     Null otherwise.
        /// </summary>
        [ServerOwned]
        public DateTimeOffset? CancelRequestedAt { get; set; }

        /// <summary>The handler's arguments as JSON, <c>nvarchar(max)</c> of at most 64 KB; null for none.</summary>
        [ServerOwned]
        public string? ArgumentsJson { get; set; }

        /// <summary>The last progress the handler reported, from 0 to 100, <c>tinyint</c>; null for none.</summary>
        [ServerOwned]
        public byte? ProgressPercent { get; set; }

        /// <summary>The resource key of the last progress message, <c>nvarchar(64)</c>; null for none.</summary>
        [ServerOwned]
        public string? ProgressKey { get; set; }

        /// <summary>The arguments of <see cref="ProgressKey" /> as a JSON object, <c>nvarchar(4000)</c>; null for none.</summary>
        [ServerOwned]
        public string? ProgressArgumentsJson { get; set; }

        /// <summary>
        ///     The resource key of the error the job last recorded, <c>nvarchar(64)</c>: one of the
        ///     platform's <c>Jobs.*</c> codes, a handler's own code or a validation code. Null for none.
        /// </summary>
        /// <remarks>
        ///     The code, <see cref="ErrorArgumentsJson" /> and <see cref="ErrorTraceId" /> are one group:
        ///     they are written and kept together.
        /// </remarks>
        [ServerOwned]
        public string? ErrorCode { get; set; }

        /// <summary>The arguments of <see cref="ErrorCode" /> as a JSON object, <c>nvarchar(4000)</c>; null for none.</summary>
        [ServerOwned]
        public string? ErrorArgumentsJson { get; set; }

        /// <summary>
        ///     The W3C trace id of the attempt that recorded <see cref="ErrorCode" />, <c>varchar(32)</c>:
        ///     the identifier a requester quotes in a support ticket. Null when no attempt recorded it.
        /// </summary>
        [ServerOwned]
        public string? ErrorTraceId { get; set; }

        /// <summary>
        ///     The warnings the job is waiting to have confirmed, as JSON, <c>nvarchar(max)</c>; set
        ///     exactly while the job is <see cref="JobStatus.PendingConfirmation" />.
        /// </summary>
        [ServerOwned]
        public string? PendingConfirmationsJson { get; set; }

        /// <summary>
        ///     The W3C <c>traceparent</c> of the activity that enqueued the job, <c>nvarchar(55)</c>,
        ///     which the run links to; null when there was none.
        /// </summary>
        [ServerOwned]
        public string? TraceParent { get; set; }

        /// <summary>The user the job runs as, whose permissions its handler is held to at run time.</summary>
        [ServerOwned]
        public int RunAsUserId { get; set; }

        /// <summary>
        ///     The user who asked for the job and is told about its failure; null for work nobody
        ///     requested, about which the administrators are told instead.
        /// </summary>
        [ServerOwned]
        public int? RequestedById { get; set; }

        /// <summary>The schedule that created the job, by firing or by run-now; null for a job enqueued directly, and once the schedule is deleted.</summary>
        [ServerOwned]
        public int? ScheduleId { get; set; }

        /// <summary>
        ///     The cron occurrence the job was created for, <c>datetimeoffset(3)</c>; null for a run-now
        ///     job and for a job no schedule created.
        /// </summary>
        [ServerOwned]
        public DateTimeOffset? ScheduledFor { get; set; }

        /// <summary>When the job was enqueued, <c>datetimeoffset(3)</c>.</summary>
        [ServerOwned]
        public DateTimeOffset CreatedAt { get; set; }
    }
}
