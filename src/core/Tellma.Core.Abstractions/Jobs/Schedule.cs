// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Jobs
{
    /// <summary>
    ///     A row of <c>core.Schedules</c>: a cron definition that enqueues a job of its handler each
    ///     time it comes due. An ordinary editable entity; the scheduler's own bookkeeping lives in a
    ///     separate table, so a tick never writes this row or its history.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A schedule is built in exactly when its id lies in the reserved band, at or below
    ///         <see cref="Access.WellKnownIds.ReservedIdBandEnd" />: the features declare those rows
    ///         and the migrations seed them, with <see cref="Code" /> and <see cref="Name" /> equal to
    ///         the handler key. A tenant may change a built-in's expression, time zone and names,
    ///         nothing else.
    ///     </para>
    ///     <para>
    ///         <see cref="CronExpression" /> is a five-field Unix cron expression (minute, hour, day of
    ///         month, month, day of week) evaluated in <see cref="TimeZoneId" />, or in the tenant's
    ///         zone at fire time when that is null.
    ///     </para>
    /// </remarks>
    [Table("Schedules", Schema = "core")]
    [TableType]
    [Temporal]
    [Stack]
    [DefaultSelect("Id,Code,Name,Name2,Name3,HandlerKey,CronExpression,TimeZoneId,MissedPolicy,OverlapPolicy,CatchUpWindowMinutes,RunAsUserId,PausedReason,IsActive,CreatedAt,CreatedById,ModifiedAt,ModifiedById")]
    public class Schedule : TopLevelEntity, IActivatable
    {
        /// <summary>The schedule's code, <c>nvarchar(64)</c>, unique when set; the handler key on a built-in.</summary>
        [MaxLength(JobLimits.MaxKeyLength)]
        [Unique]
        [Searchable]
        public string? Code { get; set; }

        /// <summary>The schedule's name in the primary content language, <c>nvarchar(255)</c>.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second content language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third content language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The key of the handler whose jobs the schedule enqueues, <c>nvarchar(64)</c>; set on create only.</summary>
        [MaxLength(JobLimits.MaxKeyLength)]
        [WriteOnce]
        public string HandlerKey { get; set; } = null!;

        /// <summary>The arguments each job receives, a JSON document of at most 64 KB; null for none.</summary>
        [JsonColumn]
        public string? ArgumentsJson { get; set; }

        /// <summary>The five-field Unix cron expression, <c>nvarchar(128)</c>.</summary>
        [MaxLength(JobLimits.MaxCronExpressionLength)]
        public string CronExpression { get; set; } = null!;

        /// <summary>The zone the expression is evaluated in, <c>varchar(64)</c>; null for the tenant's zone at fire time.</summary>
        [ValueSet(CoreValueSets.TimeZone)]
        [Column(TypeName = "varchar(64)")]
        public string? TimeZoneId { get; set; }

        /// <summary>What the schedule does with the occurrences it missed.</summary>
        public MissedPolicy MissedPolicy { get; set; } = MissedPolicy.Coalesce;

        /// <summary>Whether the schedule fires while its previous job is still active.</summary>
        public OverlapPolicy OverlapPolicy { get; set; } = OverlapPolicy.Skip;

        /// <summary>
        ///     How far back, in minutes, <see cref="MissedPolicy.ReplayAll" /> replays missed
        ///     occurrences; greater than zero.
        /// </summary>
        public int CatchUpWindowMinutes { get; set; } = 1440;

        /// <summary>
        ///     The user the schedule's jobs run as: its creator, re-stamped only by the take-over
        ///     action; the system user on a built-in.
        /// </summary>
        [ServerOwned]
        public int RunAsUserId { get; set; }

        /// <summary>Why the scheduler paused the schedule; null when it is not paused.</summary>
        [ServerOwned]
        public SchedulePausedReason? PausedReason { get; set; }

        /// <inheritdoc />
        [ServerOwned]
        public bool IsActive { get; set; } = true;
    }
}
