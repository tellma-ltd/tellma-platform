// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Sharing;
using Tellma.Core.Data.Conventions;

namespace Tellma.Core.Sharing
{
    // Spec 0033 §2.1's core.ReportShares, generic over every shareable's share table: the row's
    // foreign key cascades, so a delete of the row removes its grants in the same statement; the
    // lookup index covers the access criteria's semi-join; one grant per user, per role and one
    // Everyone grant per row; a grant names at most one grantee, and the Everyone grant is View only.
    // The conventions give the rest: the positive-key check, the sequence, the history table, the
    // table type, the grantee and sharer foreign keys with their indexes, and the enum's width.
    /// <summary>
    ///     The model of a shareable entity's share table beyond its annotations: the cascading foreign
    ///     key to the shared row, the row's lookup index, the three uniqueness indexes and the two
    ///     checks.
    /// </summary>
    /// <typeparam name="TShare">The share entity.</typeparam>
    /// <typeparam name="TEntity">The shareable entity.</typeparam>
    internal sealed class ShareRowConfiguration<TShare, TEntity> : IEntityTypeConfiguration<TShare>
        where TShare : Entity<int>, IShareRow
        where TEntity : TopLevelEntity, IShareable
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<TShare> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            string table = builder.Metadata.GetTableName()
                ?? throw new InvalidOperationException($"The share entity {typeof(TShare).Name} maps no table.");

            // The startup check reports a share entity without exactly one reference to the row; the
            // model is built only for a composition that has one.
            string row = ShareableShape.RowForeignKey(typeof(TShare), typeof(TEntity))
                ?? throw new InvalidOperationException(
                    $"The share entity {typeof(TShare).Name} has no single property marked [References(typeof({typeof(TEntity).Name}))].");

            // --- The shared row, whichever leaf the composition registers for it: its grants go with it ---
            IReadOnlyEntityType shared = ConventionModel.Leaf(builder.Metadata.Model, typeof(TEntity), $"The shared row of {builder.Metadata.DisplayName()}");
            _ = builder.HasOne(shared.ClrType)
                .WithMany()
                .HasForeignKey(row)
                .OnDelete(DeleteBehavior.Cascade);

            // --- The row's grants, covering the access criteria's semi-join ---
            // The unnamed index over the key is the one the reference's convention names and keeps.
            _ = builder.HasIndex(row)
                .HasDatabaseName($"IX_{table}_{row}")
                .IncludeProperties(nameof(IShareRow.UserId), nameof(IShareRow.RoleId), nameof(IShareRow.Access));

            // --- One Everyone grant, one grant per user and one per role, on each row ---
            _ = builder.HasIndex([row], $"UX_{table}_Everyone")
                .IsUnique()
                .HasFilter("[UserId] IS NULL AND [RoleId] IS NULL");
            _ = builder.HasIndex([row, nameof(IShareRow.UserId)], $"UX_{table}_User")
                .IsUnique()
                .HasFilter("[UserId] IS NOT NULL");
            _ = builder.HasIndex([row, nameof(IShareRow.RoleId)], $"UX_{table}_Role")
                .IsUnique()
                .HasFilter("[RoleId] IS NOT NULL");

            // --- At most one grantee; the Everyone grant views only ---
            _ = builder.ToTable(checks =>
            {
                _ = checks.HasCheckConstraint($"CK_{table}_Target", "[UserId] IS NULL OR [RoleId] IS NULL");
                _ = checks.HasCheckConstraint($"CK_{table}_EveryoneView", $"[UserId] IS NOT NULL OR [RoleId] IS NOT NULL OR [Access] = '{nameof(ShareAccess.View)}'");
            });
        }
    }
}
