// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The model of <c>core.RoleMemberships</c> beyond what the annotations and conventions give it:
    ///     the reference to the role, the window's check, the two lookup indexes, and the system user's
    ///     permanent Administrator membership. Closed over the distribution's leaf.
    /// </summary>
    /// <typeparam name="TMembership">The leaf registered for <see cref="RoleMembership" />.</typeparam>
    internal sealed class RoleMembershipConfiguration<TMembership> : IEntityTypeConfiguration<TMembership>
        where TMembership : RoleMembership
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TMembership> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);

            // --- The role held, whichever leaf the distribution registers for it ---
            IReadOnlyEntityType role = ConventionModel.Leaf(
                builder.Metadata.Model, typeof(Role), $"The role of {builder.Metadata.DisplayName()}");
            _ = builder.HasOne(role.ClrType)
                .WithMany()
                .HasForeignKey(nameof(RoleMembership.RoleId))
                .OnDelete(DeleteBehavior.NoAction);

            // --- The window: half-open, and a null bound passes ---
            _ = builder.ToTable(static table => table.HasCheckConstraint("CK_RoleMemberships_Window", "[EndsAt] > [StartsAt]"));

            // --- A user's roles with their windows, and a role's members ---
            _ = builder.HasIndex(static membership => new { membership.UserId, membership.RoleId })
                .HasDatabaseName("IX_RoleMemberships_UserId_RoleId")
                .IncludeProperties(static membership => new { membership.StartsAt, membership.EndsAt });
            _ = builder.HasIndex(static membership => new { membership.RoleId, membership.UserId })
                .HasDatabaseName("IX_RoleMemberships_RoleId_UserId");

            // --- The system user's permanent membership of the Administrator role ---
            _ = builder.HasData(new
            {
                Id = WellKnownIds.SystemAdministratorMembershipId,
                UserId = WellKnownIds.SystemUserId,
                RoleId = WellKnownIds.AdministratorRoleId,
            });
        }
    }
}
