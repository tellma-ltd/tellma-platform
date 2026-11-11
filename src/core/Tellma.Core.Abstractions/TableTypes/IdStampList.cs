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
    ///     A row of the <c>[IdStampList]</c> table type: an <c>int</c> key and the modification stamp the
    ///     caller expects the row to carry, which a delete by ids checks before it deletes anything.
    /// </summary>
    [TableType]
    public class IdStampList
    {
        /// <summary>The key value.</summary>
        [Key]
        public int Id { get; set; }

        /// <summary>The row's expected modification stamp, <c>datetimeoffset(7)</c> like the audit column it is compared with.</summary>
        [Column(TypeName = "datetimeoffset(7)")]
        public DateTimeOffset ModifiedAt { get; set; }
    }
}
