// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using ExcludeFromTableTypeAttribute = Tellma.Core.Abstractions.Entities.ExcludeFromTableTypeAttribute;
using TableTypeAttribute = Tellma.Core.Abstractions.Entities.TableTypeAttribute;

namespace Tellma.Core.Tests.Data.Model
{
    // A test model shaped like the data layer's fixture schema: one entity per capability the
    // conventions shape, the platform tables the audit, job and blob columns reference, a pack
    // default with a distribution leaf, and a Core configuration open over a replaceable default.

    /// <summary>A stand-in for <c>core.Users</c>, a replaceable default the audit columns reference.</summary>
    [Table("Users", Schema = "core")]
    [TableType]
    public class TestUser : TopLevelEntity
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

    /// <summary>The distribution's leaf of <see cref="TestUser" />.</summary>
    public sealed class MyTestUser : TestUser
    {
        [MaxLength(50)]
        public string? Nickname { get; set; }
    }

    /// <summary>A Core configuration open over the user leaf.</summary>
    /// <typeparam name="TUser">The registered leaf.</typeparam>
    public sealed class TestUserConfiguration<TUser> : IEntityTypeConfiguration<TUser>
        where TUser : TestUser
    {
        public void Configure(EntityTypeBuilder<TUser> builder)
        {
            _ = builder.HasIndex(static user => user.Name).HasDatabaseName("IX_Users_Name");
        }
    }

    /// <summary>A stand-in for <c>core.Jobs</c>.</summary>
    [Table("Jobs", Schema = "core")]
    public class TestJob : Entity<int>
    {
        [MaxLength(100)]
        public string? HandlerKey { get; set; }
    }

    /// <summary>A stand-in for <c>core.Blobs</c>.</summary>
    [Table("Blobs", Schema = "core")]
    public class TestBlob : Entity<int>
    {
        [MaxLength(200)]
        public string? Name { get; set; }
    }

    /// <summary>A two-member enum, stored in the minimum width.</summary>
    public enum WidgetKind
    {
        Plain,
        Fancy,
    }

    /// <summary>An enum with a member longer than the minimum width.</summary>
    public enum LongStatus
    {
        Active,
        PendingConfirmationByTheOwner,
    }

    /// <summary>A flags enum, whose widest value joins every flag.</summary>
    [Flags]
    public enum Access
    {
        None = 0,
        Read = 1,
        Write = 2,
        Delete = 4,
        All = Read | Write | Delete,
    }

    /// <summary>A top-level, temporal, activatable entity with one property per convention.</summary>
    [Table("Widgets", Schema = "fixture")]
    [Temporal]
    [TableType]
    [TableIndex(nameof(Kind), nameof(Price))]
    public class Widget : TopLevelEntity, IActivatable
    {
        [Column(TypeName = "varchar(50)")]
        [NaturalKey]
        public string? Code { get; set; }

        public WidgetKind Kind { get; set; }

        public WidgetKind? OptionalKind { get; set; }

        public LongStatus Status { get; set; }

        public Access Access { get; set; }

        [MaxLength(20)]
        public WidgetKind SizedKind { get; set; }

        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }

        [MaxLength(64)]
        [WriteOnce]
        [Unique]
        [Collation("Latin1_General_100_BIN2")]
        public string Subject { get; set; } = null!;

        [Column(TypeName = "decimal(19,4)")]
        public decimal? Price { get; set; }

        [JsonColumn]
        public string? Settings { get; set; }

        [BlobReference("fixture-image", BlobPreset.Avatar)]
        public int? ImageId { get; set; }

        [References(typeof(Center))]
        public int? CenterId { get; set; }

        [MaxLength(100)]
        [ExcludeFromTableType]
        public string? Scratch { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTimeOffset? DueAt { get; set; }

        [Column(TypeName = "datetimeoffset(7)")]
        public DateTimeOffset? PreciseAt { get; set; }

        [NotMapped]
        public List<WidgetPart>? Parts { get; set; }
    }

    /// <summary>A child of <see cref="Widget" />, which follows its parent's system versioning.</summary>
    [Table("WidgetParts", Schema = "fixture")]
    [TableType]
    public class WidgetPart : ChildEntity
    {
        [ParentKey]
        public int WidgetId { get; set; }

        [Unique]
        public int Ordinal { get; set; }

        [MaxLength(1024)]
        public string? Notes { get; set; }

        [BlobReference("fixture-attachment")]
        public int? AttachmentId { get; set; }

        [NotMapped]
        public List<WidgetPartNote>? PartNotes { get; set; }
    }

    /// <summary>A grandchild that opts out of its temporal ancestors.</summary>
    [Table("WidgetPartNotes", Schema = "fixture")]
    [TableType]
    [Temporal(false)]
    public class WidgetPartNote : ChildEntity
    {
        [ParentKey]
        public int WidgetPartId { get; set; }

        public string Text { get; set; } = null!;
    }

    /// <summary>A one-row-per-owner sibling of <see cref="Widget" />.</summary>
    [Table("WidgetExtras", Schema = "fixture")]
    [Sibling(typeof(Widget), "Extra")]
    public class WidgetExtra : Entity<int>
    {
        [MaxLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>A pack's default tree, which the distribution replaces with <see cref="MyCenter" />.</summary>
    [Table("Centers", Schema = "gl")]
    [TableType]
    public class Center : ActivatableTreeEntity
    {
        [MaxLength(50)]
        [NaturalKey]
        public string Code { get; set; } = null!;

        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        [MaxLength(100)]
        public string? Name2 { get; set; }

        public string? Name3 { get; set; }
    }

    /// <summary>The distribution's leaf of <see cref="Center" />.</summary>
    public sealed class MyCenter : Center
    {
        [MaxLength(50)]
        public string? Region { get; set; }
    }

    /// <summary>A tree with a declared depth and a child with two foreign keys to it.</summary>
    [Table("Nodes", Schema = "fixture")]
    [Tree(MaxDepth = 8)]
    [TableType]
    public class Node : ActivatableTreeEntity
    {
        [Column(TypeName = "varchar(50)")]
        [Unique]
        public string Code { get; set; } = null!;

        [MaxLength(255)]
        public string Name { get; set; } = null!;

        [NotMapped]
        public List<Link>? Links { get; set; }
    }

    /// <summary>A child of <see cref="Node" /> whose second foreign key to it is an ordinary reference.</summary>
    [Table("Links", Schema = "fixture")]
    [TableType]
    public class Link : ChildEntity
    {
        [ParentKey]
        public int NodeId { get; set; }

        [References(typeof(Node))]
        public int OtherNodeId { get; set; }
    }

    /// <summary>A job entity with a <see cref="long" /> key.</summary>
    [Table("Shipments", Schema = "fixture")]
    [TableType]
    [TableIndex(nameof(State), nameof(CreatedAt))]
    public class Shipment : TopLevelEntity<long>, IJobEntity
    {
        public WidgetKind State { get; set; }

        public string? Payload { get; set; }

        public int? JobId { get; set; }
    }

    /// <summary>A standalone table type with a collated key, an enum and an excluded property.</summary>
    [TableType(Name = "CollatedKeyList")]
    public class CollatedKeyRow
    {
        [Key]
        [MaxLength(64)]
        [Collation("Latin1_General_100_BIN2")]
        public string Key { get; set; } = null!;

        public LongStatus Status { get; set; }

        public WidgetKind? OptionalKind { get; set; }

        [ExcludeFromTableType]
        public string? Scratch { get; set; }

        public DateOnly? On { get; set; }

        public DateTimeOffset At { get; set; }
    }

    /// <summary>An entity a closed Core configuration shapes against the conventions.</summary>
    [Table("Configured", Schema = "fixture")]
    [TableType]
    public class Configured : TopLevelEntity, IJobEntity
    {
        public WidgetKind Kind { get; set; }

        public int? JobId { get; set; }

        public int? AttachmentRef { get; set; }
    }

    /// <summary>
    ///     A closed Core configuration whose explicit choices the conventions must keep: a foreign key
    ///     with its own name and delete behaviour, an enum stored as a number, and a table-type opt-out;
    ///     and a foreign key it leaves unnamed, which the conventions name.
    /// </summary>
    public sealed class ConfiguredConfiguration : IEntityTypeConfiguration<Configured>
    {
        public void Configure(EntityTypeBuilder<Configured> builder)
        {
            _ = builder.Property(static configured => configured.Kind).HasConversion<int>();
            _ = builder.HasOne<TestJob>().WithMany().HasForeignKey(static configured => configured.JobId)
                .HasConstraintName("FK_Configured_OwnJob").OnDelete(DeleteBehavior.Cascade);
            _ = builder.HasOne<TestBlob>().WithMany().HasForeignKey(static configured => configured.AttachmentRef);
            _ = builder.HasNoTableType();
        }
    }
}
