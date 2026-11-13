// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the table-valued parameter that advances schedules: one schedule's next due time and
    ///     the last occurrence it fired, as the scheduler's tick or a schedule's save computed them.
    /// </summary>
    /// <remarks>A standalone table-type shape, mapped in schema <c>dbo</c> under its class name.</remarks>
    [TableType]
    public class ScheduleNextList
    {
        /// <summary>The schedule's id, <c>int</c>; the key.</summary>
        [Key]
        public int ScheduleId { get; set; }

        /// <summary>When the schedule is next due, <c>datetimeoffset(3)</c>; null when it will never occur again.</summary>
        public DateTimeOffset? NextDueAt { get; set; }

        /// <summary>The last occurrence the schedule fired, <c>datetimeoffset(3)</c>; null when it has fired none.</summary>
        public DateTimeOffset? LastScheduledFor { get; set; }
    }
}
