// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     One grant of a role: an action on a resource, optionally narrowed by a filter. Saved with its
    ///     role. The platform's default: a distribution extends it by inheritance and registers its class
    ///     as the leaf.
    /// </summary>
    /// <remarks>
    ///     <see cref="CoreResources.Wildcard" /> in <see cref="Resource" /> and
    ///     <see cref="AccessActions.Wildcard" /> in <see cref="Action" /> match every resource and every
    ///     action of the resource. A wildcard resource takes no filter. Each write of a permission moves
    ///     the tenant's permissions tag.
    /// </remarks>
    [Table("Permissions", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsVersionTag(VersionTagNames.Permissions)]
    [SuppressMessage(
        "Naming",
        "CA1711:Identifiers should not have incorrect suffix",
        Justification = "A permission in the access-control sense, a row of core.Permissions, not a code-access-security permission; the entity's name is its table's.")]
    public class Permission : ChildEntity
    {
        /// <summary>The owning role.</summary>
        [ParentKey]
        public int RoleId { get; set; }

        /// <summary>The resource granted, or the wildcard, <c>varchar(128)</c>.</summary>
        [Column(TypeName = "varchar(128)")]
        public string Resource { get; set; } = null!;

        /// <summary>The action granted, or the wildcard, <c>varchar(32)</c>.</summary>
        [Column(TypeName = "varchar(32)")]
        public string Action { get; set; } = null!;

        /// <summary>
        ///     A query-language predicate over the securable's filter root that narrows the grant to the
        ///     rows it matches, <c>nvarchar(2048)</c>; null grants every row.
        /// </summary>
        [MaxLength(2048)]
        public string? Filter { get; set; }

        /// <summary>
        ///     The query-language version <see cref="Filter" /> was validated under; derived, stamped by
        ///     the role save and null exactly when <see cref="Filter" /> is.
        /// </summary>
        [Derived]
        public int? FilterQueryexVersion { get; set; }

        /// <summary>Why the permission exists, for an access review, <c>nvarchar(1024)</c>.</summary>
        [MaxLength(1024)]
        public string? Notes { get; set; }
    }
}
