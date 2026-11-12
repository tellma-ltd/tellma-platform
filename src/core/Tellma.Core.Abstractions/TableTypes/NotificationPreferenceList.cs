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
    ///     A row of the table-valued parameter that writes one user's notification preferences: whether
    ///     a channel delivers a type to them.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name, keyed by the
    ///     type and the channel together.
    /// </remarks>
    [TableType]
    public class NotificationPreferenceList
    {
        /// <summary>The notification type's key, <c>nvarchar(64)</c>; part of the key.</summary>
        [Key]
        [MaxLength(64)]
        public string Type { get; set; } = null!;

        /// <summary>The channel's key, <c>varchar(16)</c>; part of the key.</summary>
        [Key]
        [Column(TypeName = "varchar(16)")]
        public string Channel { get; set; } = null!;

        /// <summary>Whether the channel delivers the type to the user, <c>bit</c>.</summary>
        public bool Enabled { get; set; }
    }
}
