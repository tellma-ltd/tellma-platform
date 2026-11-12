// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A named set of permissions that users hold through their memberships. The platform's default:
    ///     a distribution extends it by inheritance and registers its class as the leaf.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A public role grants its permissions to every active user of the tenant and has no
    ///         members. A deactivated role grants nothing.
    ///     </para>
    ///     <para>
    ///         The seeded Administrator role (<see cref="WellKnownIds.AdministratorRoleId" />) is the one
    ///         role whose unfiltered wildcard grant is guaranteed: it cannot be made public, deactivated
    ///         or deleted. Every write of a role or its permissions moves the tenant's permissions tag.
    ///     </para>
    /// </remarks>
    [Table("Roles", Schema = "core")]
    [TableType]
    [Temporal]
    [BumpsVersionTag(VersionTagNames.Permissions)]
    [ApiResource]
    public class Role : TopLevelEntity, IActivatable
    {
        /// <summary>The name in the tenant's primary language, <c>nvarchar(255)</c>, unique.</summary>
        [MaxLength(255)]
        [Multilingual]
        [Unique]
        [Searchable]
        public string Name { get; set; } = null!;

        /// <summary>The name in the tenant's secondary language, unique when present.</summary>
        [Unique]
        public string? Name2 { get; set; }

        /// <summary>The name in the tenant's tertiary language, unique when present.</summary>
        [Unique]
        public string? Name3 { get; set; }

        /// <summary>The natural key seeds and imports find the role by, <c>nvarchar(50)</c>, unique when present.</summary>
        [MaxLength(50)]
        [NaturalKey]
        public string? Code { get; set; }

        /// <summary>Whether the role's permissions apply to every active user of the tenant; a public role has no members.</summary>
        public bool IsPublic { get; set; }

        /// <summary>
        ///     Whether the role grants anything; server-owned. Every role is created active, and the
        ///     activate and deactivate actions alone change it.
        /// </summary>
        [ServerOwned]
        public bool IsActive { get; set; } = true;

        /// <summary>
        ///     The role's permissions, saved with the role: null leaves them untouched, an empty list
        ///     deletes them all, and a list replaces them.
        /// </summary>
        [NotMapped]
        public IReadOnlyList<Permission>? Permissions { get; set; }
    }
}
