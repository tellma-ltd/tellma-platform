// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Writes
{
    // A model whose foreign keys act on a delete: a hub with a child collection of bays; spokes whose
    // key to their hub cascades; rims whose keys to a spoke and to a bay set null; and treads whose
    // key to their rim cascades, which a hub's delete never reaches, since a rim it touches only loses
    // a reference, never its row; and seats whose key to their spoke cascades and whose rider is a
    // user with a user-level version tag, which a hub's delete reaches through the spokes' cascade.

    /// <summary>Composes the referential model into a composition.</summary>
    internal static class ReferentialModel
    {
        /// <summary>Adds the referential model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.D>("referential", contribute: static contribution => contribution
                .Entity<Hub>()
                .Entity<Spoke>()
                .Entity<Rim>()
                .Entity<Tread>()
                .Entity<Seat>()
                .Model<SpokeConfiguration>()
                .Model<RimConfiguration>()
                .Model<TreadConfiguration>()
                .Model<SeatConfiguration>()));
        }
    }

    /// <summary>The deleted entity: a top-level row with a child collection.</summary>
    [Table("Hubs", Schema = "rf")]
    [TableType]
    public class Hub : TopLevelEntity
    {
        /// <summary>The bays.</summary>
        [NotMapped]
        public List<Bay>? Bays { get; set; }
    }

    /// <summary>A hub's bay: a child whose rows a hub's delete and a hub's save remove explicitly.</summary>
    [Table("Bays", Schema = "rf")]
    [TableType]
    public class Bay : ChildEntity
    {
        /// <summary>The hub.</summary>
        [ParentKey]
        public int HubId { get; set; }

        /// <summary>The position.</summary>
        public int Position { get; set; }
    }

    /// <summary>A row whose key to its hub cascades, so a hub's delete removes it.</summary>
    [Table("Spokes", Schema = "rf")]
    [TableType]
    public class Spoke : TopLevelEntity
    {
        /// <summary>The hub.</summary>
        public int HubId { get; set; }
    }

    /// <summary>A row whose keys to a spoke and to a bay set null, so a hub's delete updates it.</summary>
    [Table("Rims", Schema = "rf")]
    [TableType]
    public class Rim : TopLevelEntity
    {
        /// <summary>The spoke, cleared when the spoke is deleted.</summary>
        public int? SpokeId { get; set; }

        /// <summary>The bay, cleared when the bay is deleted.</summary>
        public int? BayId { get; set; }
    }

    /// <summary>A row whose key to its rim cascades: written only when a rim is deleted.</summary>
    [Table("Treads", Schema = "rf")]
    [TableType]
    public class Tread : TopLevelEntity
    {
        /// <summary>The rim.</summary>
        public int RimId { get; set; }
    }

    /// <summary>A row whose key to its spoke cascades, naming a user whose preferences tag it bumps.</summary>
    [Table("Seats", Schema = "rf")]
    [TableType]
    [BumpsUserVersionTag(UserVersionTagNames.Preferences, nameof(RiderId))]
    public class Seat : TopLevelEntity
    {
        /// <summary>The spoke; deleting it deletes the seat.</summary>
        public int SpokeId { get; set; }

        /// <summary>The user who rides the seat.</summary>
        public int RiderId { get; set; }
    }

    /// <summary>The spokes' key to their hub, <c>ON DELETE CASCADE</c>.</summary>
    public sealed class SpokeConfiguration : IEntityTypeConfiguration<Spoke>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Spoke> builder)
        {
            _ = builder.HasOne<Hub>().WithMany().HasForeignKey(static spoke => spoke.HubId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>The rims' keys to a spoke and to a bay, <c>ON DELETE SET NULL</c>.</summary>
    public sealed class RimConfiguration : IEntityTypeConfiguration<Rim>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Rim> builder)
        {
            _ = builder.HasOne<Spoke>().WithMany().HasForeignKey(static rim => rim.SpokeId).OnDelete(DeleteBehavior.SetNull);
            _ = builder.HasOne<Bay>().WithMany().HasForeignKey(static rim => rim.BayId).OnDelete(DeleteBehavior.SetNull);
        }
    }

    /// <summary>The treads' key to their rim, <c>ON DELETE CASCADE</c>.</summary>
    public sealed class TreadConfiguration : IEntityTypeConfiguration<Tread>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Tread> builder)
        {
            _ = builder.HasOne<Rim>().WithMany().HasForeignKey(static tread => tread.RimId).OnDelete(DeleteBehavior.Cascade);
        }
    }

    /// <summary>The seats' key to their spoke, <c>ON DELETE CASCADE</c>.</summary>
    public sealed class SeatConfiguration : IEntityTypeConfiguration<Seat>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<Seat> builder)
        {
            _ = builder.HasOne<Spoke>().WithMany().HasForeignKey(static seat => seat.SpokeId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}
