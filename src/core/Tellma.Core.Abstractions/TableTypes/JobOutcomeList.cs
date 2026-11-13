// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the completion statement's table-valued parameter: the outcome of one leased job.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. The worker
    ///     binds the list before the handler runs and fills it once the handler has returned; the rows
    ///     are read when the completion executes.
    /// </remarks>
    [TableType]
    public class JobOutcomeList
    {
        /// <summary>The job's id, <c>int</c>; the key.</summary>
        [Key]
        public int Id { get; set; }

        /// <summary>The status the job takes, stored by name, <c>varchar(19)</c>.</summary>
        public JobStatus Status { get; set; }

        /// <summary>Whether the job is released untouched, which takes back the claim's attempt, <c>bit</c>.</summary>
        public bool Released { get; set; }

        /// <summary>How many seconds from now the job is due again, <c>int</c>; null to keep its due time.</summary>
        public int? RetryAfterSeconds { get; set; }

        /// <summary>The error's code, <c>nvarchar(64)</c>; null for none.</summary>
        [MaxLength(64)]
        public string? ErrorCode { get; set; }

        /// <summary>The error's arguments as a JSON object, <c>nvarchar(4000)</c>; null for none.</summary>
        [MaxLength(4000)]
        public string? ErrorArgumentsJson { get; set; }

        /// <summary>The W3C trace id of the attempt that recorded the error, <c>varchar(32)</c>; null for none.</summary>
        [Column(TypeName = "varchar(32)")]
        public string? ErrorTraceId { get; set; }

        /// <summary>The job's last checkpoint as JSON, <c>nvarchar(max)</c>; null to keep the stored one.</summary>
        public string? StateJson { get; set; }

        /// <summary>The warnings awaiting confirmation as JSON, <c>nvarchar(max)</c>; null unless the job waits for confirmation.</summary>
        public string? PendingConfirmationsJson { get; set; }
    }
}
