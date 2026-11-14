// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Tests.Data.Metadata
{
    // A sound model for the entity metadata: the platform tables the audit, job and blob columns
    // reference, a temporal aggregate of three levels with every ownership and key kind, a pack's
    // default tree with a distribution leaf, a long-keyed job entity with a sibling, and an entity
    // referenced without a natural key, which draws the one warning.

    /// <summary>A stand-in for <c>core.Users</c>, which the distribution replaces with <see cref="MyMdUser" />.</summary>
    [Table("Users", Schema = "core")]
    [TableType]
    public class MdUser : TopLevelEntity
    {
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }

        [MaxLength(320)]
        [NaturalKey]
        public string? Email { get; set; }
    }

    /// <summary>The distribution's leaf of <see cref="MdUser" />.</summary>
    public sealed class MyMdUser : MdUser
    {
        [MaxLength(50)]
        public string? Nickname { get; set; }
    }

    /// <summary>The distribution's leaf of Core's <see cref="User" />, in models composed with Core.</summary>
    public sealed class MdMember : User
    {
        [MaxLength(50)]
        public string? Nickname { get; set; }
    }

    /// <summary>A stand-in for <c>core.Jobs</c> in models built without Core.</summary>
    [Table("Jobs", Schema = "core")]
    public class MdJob : Entity<int>
    {
        [MaxLength(100)]
        public string? HandlerKey { get; set; }
    }

    /// <summary>A stand-in for <c>core.Blobs</c>.</summary>
    [Table("Blobs", Schema = "core")]
    public class MdBlob : Entity<int>
    {
        [MaxLength(200)]
        public string? Name { get; set; }
    }

    /// <summary>An account's kind.</summary>
    public enum AccountKind
    {
        Asset,
        Liability,
    }

    /// <summary>A temporal, activatable, cacheable aggregate root with one property per ownership and key kind.</summary>
    [Table("Accounts", Schema = "fixture")]
    [TableType]
    [Temporal]
    [Cacheable]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    public class Account : TopLevelEntity, IActivatable
    {
        [NaturalKey(1)]
        public int Number { get; set; }

        [NaturalKey(0)]
        public Guid Ref { get; set; }

        [MaxLength(255)]
        [Multilingual]
        [Unique]
        [Searchable(SearchKind.Prefix)]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }

        [MaxLength(50)]
        [Unique]
        public string Code { get; set; } = null!;

        [MaxLength(64)]
        [Unique]
        public string Subject { get; set; } = null!;

        [MaxLength(64)]
        [Unique]
        public string? Alias { get; set; }

        [MaxLength(100)]
        [ServerOwned]
        public string? Evidence { get; set; } = "unverified";

        [MaxLength(20)]
        [WriteOnce]
        public string? Origin { get; set; }

        [Column(TypeName = "decimal(19,4)")]
        [Derived]
        public decimal Total { get; set; }

        [MaxLength(20)]
        [Derived]
        public string? Normalized { get; set; } = "none";

        [MaxLength(20)]
        [Defaulted]
        public string? Tag { get; set; }

        [MaxLength(200)]
        [ExcludeFromExcel]
        public string? Note { get; set; }

        [MaxLength(400)]
        public string? Label { get; set; }

        public AccountKind Kind { get; set; }

        public bool IsActive { get; set; } = true;

        [References(typeof(Account))]
        public int? ParentAccountId { get; set; }

        [BlobReference("account-image", BlobPreset.Avatar)]
        public int? ImageId { get; set; }

        [NotMapped]
        [MaxChildren(500)]
        public List<AccountLine>? Lines { get; set; }
    }

    /// <summary>Gives <see cref="Account.Label" /> a computed definition, which makes it database-owned.</summary>
    public sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
    {
        public void Configure(EntityTypeBuilder<Account> builder)
        {
            _ = builder.Property(static account => account.Label).HasComputedColumnSql("[Code] + N' ' + [Name]", stored: true);
        }
    }

    /// <summary>A child of <see cref="Account" />, unique per parent by its ordinal and label.</summary>
    [Table("AccountLines", Schema = "fixture")]
    [TableType]
    public class AccountLine : ChildEntity
    {
        [ParentKey]
        public int AccountId { get; set; }

        [Unique]
        public int Ordinal { get; set; }

        [MaxLength(100)]
        [Unique]
        public string Label { get; set; } = null!;

        [NotMapped]
        public List<AccountLineNote>? Notes { get; set; }
    }

    /// <summary>A grandchild, which follows its temporal root.</summary>
    [Table("AccountLineNotes", Schema = "fixture")]
    [TableType]
    public class AccountLineNote : ChildEntity
    {
        [ParentKey]
        public int AccountLineId { get; set; }

        [MaxLength(500)]
        public string Text { get; set; } = null!;
    }

    /// <summary>A pack's default tree, which the distribution replaces with <see cref="MyRegion" />.</summary>
    [Table("Regions", Schema = "geo")]
    [TableType]
    [Tree(MaxDepth = 8)]
    public class Region : ActivatableTreeEntity
    {
        [MaxLength(20)]
        [NaturalKey]
        public string Code { get; set; } = null!;

        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }
    }

    /// <summary>The distribution's leaf of <see cref="Region" />, whose own column may take another name.</summary>
    public sealed class MyRegion : Region
    {
        [Column("ZoneCode")]
        [MaxLength(30)]
        [ValueSet(CoreValueSets.TimeZone)]
        public string? Zone { get; set; }
    }

    /// <summary>A job entity with a <see cref="long" /> key, referencing a keyed and an unkeyed entity.</summary>
    [Table("Shipments", Schema = "fixture")]
    [TableType]
    public class Shipment : TopLevelEntity<long>, IJobEntity
    {
        public int? JobId { get; set; }

        [References(typeof(Region))]
        public int RegionId { get; set; }

        [References(typeof(Carrier))]
        public int? CarrierId { get; set; }
    }

    /// <summary>A one-row-per-owner sibling of <see cref="Shipment" />.</summary>
    [Table("ShipmentExtras", Schema = "fixture")]
    [Sibling(typeof(Shipment), "Extra")]
    public class ShipmentExtra : Entity<long>
    {
        [MaxLength(200)]
        public string? Note { get; set; }
    }

    /// <summary>An entity with no natural key, so a reference to it is a warning.</summary>
    [Table("Carriers", Schema = "fixture")]
    [TableType]
    public class Carrier : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }
}
