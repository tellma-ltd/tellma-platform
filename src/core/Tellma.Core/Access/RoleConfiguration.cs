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
    ///     The model of <c>core.Roles</c> beyond what the annotations and conventions give it: the
    ///     public flag's default and the public-roles probe, and the Administrator role's row. Closed over
    ///     the distribution's leaf.
    /// </summary>
    /// <typeparam name="TRole">The leaf registered for <see cref="Role" />.</typeparam>
    internal sealed class RoleConfiguration<TRole> : IEntityTypeConfiguration<TRole>
        where TRole : Role
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TRole> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The public flag: off by default, and the probe that finds the public roles ---
            _ = builder.Property(static role => role.IsPublic).HasDefaultValue(false);
            _ = builder.HasIndex(static role => role.Id)
                .HasDatabaseName("IX_Roles_IsPublic")
                .HasFilter("[IsPublic] = 1");

            // --- The Administrator role, created by the system user ---
            _ = builder.HasData(new
            {
                Id = WellKnownIds.AdministratorRoleId,
                Name = AccessSeeds.AdministratorRoleName,
                Code = AccessSeeds.AdministratorRoleName,
                IsPublic = false,
                IsActive = true,
                CreatedAt = AccessSeeds.SeededAt,
                CreatedById = WellKnownIds.SystemUserId,
                ModifiedAt = AccessSeeds.SeededAt,
                ModifiedById = WellKnownIds.SystemUserId,
            });
        }
    }
}
