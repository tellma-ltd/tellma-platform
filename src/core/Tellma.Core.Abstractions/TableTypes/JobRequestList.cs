// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the enqueue statement's table-valued parameter: one job to insert, numbered in
    ///     request order.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. The ordinal
    ///     offsets the job's id within the id range the statement reserves, so the new ids come back in
    ///     request order.
    /// </remarks>
    [TableType]
    public class JobRequestList
    {
        /// <summary>The request's position in the call, from 0, <c>int</c>; the key.</summary>
        [Key]
        public int Ordinal { get; set; }

        /// <summary>The handler key, <c>nvarchar(64)</c>.</summary>
        [MaxLength(64)]
        public string HandlerKey { get; set; } = null!;

        /// <summary>The label's resource key, <c>nvarchar(64)</c>; null for the handler's own label.</summary>
        [MaxLength(64)]
        public string? LabelKey { get; set; }

        /// <summary>The label's arguments as a JSON object, <c>nvarchar(4000)</c>; null for none.</summary>
        [MaxLength(4000)]
        public string? LabelArgumentsJson { get; set; }

        /// <summary>When the job becomes claimable, <c>datetimeoffset(3)</c>; null for now on the database's clock.</summary>
        public DateTimeOffset? DueAt { get; set; }

        /// <summary>The handler's arguments as JSON, <c>nvarchar(max)</c>; null for none.</summary>
        public string? ArgumentsJson { get; set; }

        /// <summary>The enqueuing activity's W3C <c>traceparent</c>, <c>nvarchar(55)</c>; null when there was none.</summary>
        [MaxLength(55)]
        public string? TraceParent { get; set; }

        /// <summary>The warning keys the enqueuing scope confirmed, comma-separated, <c>varchar(2000)</c>; null for none.</summary>
        [Column(TypeName = "varchar(2000)")]
        public string? ConfirmedKeys { get; set; }

        /// <summary>The user the job runs as, <c>int</c>.</summary>
        public int RunAsUserId { get; set; }

        /// <summary>The user who requested the job, <c>int</c>; null for nobody.</summary>
        public int? RequestedById { get; set; }

        /// <summary>The id of the row whose job id the statement sets, <c>int</c>; null for a job that names no row.</summary>
        public int? OwnerId { get; set; }
    }
}
