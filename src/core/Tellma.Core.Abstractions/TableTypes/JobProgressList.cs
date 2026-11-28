// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.TableTypes
{
    /// <summary>
    ///     A row of the progress table-valued parameter of a lease renewal or a fenced checkpoint: one
    ///     running job's progress and state.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name. A null
    ///     column keeps the job's current value.
    /// </remarks>
    [TableType]
    public class JobProgressList
    {
        /// <summary>The job's id, <c>int</c>; the key.</summary>
        [Key]
        public int Id { get; set; }

        /// <summary>The progress in percent, from 0 to 100, <c>tinyint</c>; null to keep the current one.</summary>
        public byte? ProgressPercent { get; set; }

        /// <summary>The progress message's resource key, <c>nvarchar(64)</c>; null to keep the current message.</summary>
        [MaxLength(64)]
        public string? ProgressKey { get; set; }

        /// <summary>The progress message's arguments as a JSON object, <c>nvarchar(4000)</c>; read only with a key.</summary>
        [MaxLength(4000)]
        public string? ProgressArgumentsJson { get; set; }

        /// <summary>The checkpoint as JSON, <c>nvarchar(max)</c>; null to keep the current one.</summary>
        public string? StateJson { get; set; }
    }
}
