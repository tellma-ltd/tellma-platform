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
    ///     A row of the table-valued parameter a sharing action merges grants through: one grant of one
    ///     shareable row, to a user, a role or, with neither, everyone.
    /// </summary>
    /// <remarks>
    ///     A standalone table-type shape, mapped in schema <c>dbo</c> under its class name and keyed by
    ///     the row's position, since a grantee's two columns are nullable.
    /// </remarks>
    [TableType]
    public class ShareGrantList
    {
        /// <summary>The row's position in the parameter, its key.</summary>
        [Key]
        public int Index { get; set; }

        /// <summary>The shareable row the grant belongs to.</summary>
        public int RowId { get; set; }

        /// <summary>The user granted; null for a role grant or the Everyone grant.</summary>
        public int? UserId { get; set; }

        /// <summary>The role granted; null for a user grant or the Everyone grant.</summary>
        public int? RoleId { get; set; }

        /// <summary>What the grant lets its grantee do, <c>View</c> or <c>Edit</c>; <c>varchar(8)</c>.</summary>
        [MaxLength(8)]
        [Column(TypeName = "varchar(8)")]
        public string Access { get; set; } = null!;
    }
}
