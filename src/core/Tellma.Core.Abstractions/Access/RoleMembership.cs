// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A user's membership of a role, saved with the user and read-only from the role's side. The
    ///     platform's default: a distribution extends it by inheritance and registers its class as the
    ///     leaf.
    /// </summary>
    /// <remarks>
    ///     A membership grants its role during the half-open window [<see cref="StartsAt" />,
    ///     <see cref="EndsAt" />), a null bound being unbounded, so a membership with both bounds null is
    ///     permanent. One user may hold one role several times in windows that never overlap. An ended
    ///     membership stays until an administrator removes it, so the row and its history tell who held
    ///     a role when. Each write of a membership moves its user's permissions tag.
    /// </remarks>
    [Table("RoleMemberships", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsUserVersionTag(UserVersionTagNames.Permissions, nameof(UserId))]
    public class RoleMembership : ChildEntity
    {
        /// <summary>The member, the owning user.</summary>
        [ParentKey]
        public int UserId { get; set; }

        /// <summary>The role held.</summary>
        public int RoleId { get; set; }

        /// <summary>When the membership starts granting, inclusive, <c>datetimeoffset(3)</c>; null for since always.</summary>
        public DateTimeOffset? StartsAt { get; set; }

        /// <summary>When the membership stops granting, exclusive, <c>datetimeoffset(3)</c>; null for no end.</summary>
        public DateTimeOffset? EndsAt { get; set; }

        /// <summary>Why the membership exists, for an access review, <c>nvarchar(1024)</c>.</summary>
        [MaxLength(1024)]
        public string? Notes { get; set; }
    }
}
