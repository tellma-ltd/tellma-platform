// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Sharing
{
    /// <summary>
    ///     One grant of a shareable row, a row of the share entity beside it: to a user, to a role, or,
    ///     when it names neither, to everyone.
    /// </summary>
    /// <remarks>
    ///     The share entity is an <c>Entity&lt;int&gt;</c> with exactly one foreign key to the shareable
    ///     entity, and is neither a stack nor a child collection: only the <c>share</c> and
    ///     <c>publish</c> actions write it, and deleting the row deletes its grants.
    /// </remarks>
    public interface IShareRow
    {
        /// <summary>The user granted; null for a role grant or the Everyone grant.</summary>
        int? UserId { get; set; }

        /// <summary>The role granted; null for a user grant or the Everyone grant.</summary>
        int? RoleId { get; set; }

        /// <summary>What the grant lets its grantee do; the Everyone grant is <see cref="ShareAccess.View" /> only.</summary>
        ShareAccess Access { get; set; }

        /// <summary>The user who made the grant.</summary>
        int SharedById { get; set; }

        /// <summary>When the grant was made, <c>datetimeoffset(3)</c>.</summary>
        DateTimeOffset SharedAt { get; set; }
    }
}
