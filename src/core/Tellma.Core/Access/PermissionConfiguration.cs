// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The model of <c>core.Permissions</c> beyond what the annotations and conventions give it: the
    ///     wildcard and filter-version checks, the covering index that loads a role's permissions, and
    ///     the Administrator role's unfiltered wildcard permission. Closed over the distribution's leaf.
    /// </summary>
    /// <typeparam name="TPermission">The leaf registered for <see cref="Permission" />.</typeparam>
    internal sealed class PermissionConfiguration<TPermission> : IEntityTypeConfiguration<TPermission>
        where TPermission : Permission
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TPermission> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- Checks: a wildcard resource takes no filter, and a filter always carries its version ---
            _ = builder.ToTable(static table =>
            {
                _ = table.HasCheckConstraint(
                    "CK_Permissions_WildcardHasNoFilter",
                    $"[Resource] <> '{CoreResources.Wildcard}' OR [Filter] IS NULL");
                _ = table.HasCheckConstraint(
                    "CK_Permissions_FilterVersion",
                    "([Filter] IS NULL AND [FilterQueryexVersion] IS NULL) OR ([Filter] IS NOT NULL AND [FilterQueryexVersion] IS NOT NULL)");
            });

            // --- The parent key's index covers the load by role, replacing the convention's INCLUDE (Id) ---
            // spec 0014 §3.7: no unique index, since a filter can exceed the index key limit.
            _ = builder.HasIndex(static permission => permission.RoleId)
                .HasDatabaseName("IX_Permissions_RoleId")
                .IncludeProperties(static permission => new
                {
                    permission.Resource,
                    permission.Action,
                    permission.Filter,
                    permission.FilterQueryexVersion,
                });

            // --- The Administrator role's unfiltered grant of every action on every resource ---
            _ = builder.HasData(new
            {
                Id = WellKnownIds.AdministratorPermissionId,
                RoleId = WellKnownIds.AdministratorRoleId,
                Resource = CoreResources.Wildcard,
                Action = AccessActions.Wildcard,
            });
        }
    }
}
