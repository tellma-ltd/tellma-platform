// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using TableTypeAttribute = Tellma.Core.Abstractions.Entities.TableTypeAttribute;

namespace Tellma.Core.Tests.Data.Metadata.Malformed
{
    // Entities that each break one rule of the entity contract, for the metadata validation suite.
    // Everything else about each is sound, so the rule under test is the only problem it reports.

    // --- Tables and column names ---

    /// <summary>Declares no table.</summary>
    [TableType]
    public class Untabled : TopLevelEntity;

    /// <summary>Maps to the dbo schema.</summary>
    [Table("Dboed")]
    [TableType]
    public class Dboed : TopLevelEntity;

    /// <summary>A default with a table of its own.</summary>
    [Table("Tabled", Schema = "fixture")]
    [TableType]
    public class Tabled : TopLevelEntity;

    /// <summary>A leaf that re-declares another table.</summary>
    [Table("Retabled", Schema = "fixture")]
    public sealed class RetabledLeaf : Tabled;

    /// <summary>A leaf that re-declares its default's table, which is allowed.</summary>
    [Table("Tabled", Schema = "fixture")]
    public sealed class SameTabledLeaf : Tabled;

    /// <summary>Renames a column it declares as a default.</summary>
    [Table("Renamed", Schema = "fixture")]
    [TableType]
    public class Renamed : TopLevelEntity
    {
        [Column("code_column")]
        [MaxLength(20)]
        public string? Code { get; set; }

        [Column(TypeName = "varchar(20)")]
        public string? Typed { get; set; }
    }

    // --- Capability columns and table types ---

    /// <summary>Implements the activation gate explicitly, so the model cannot map it.</summary>
    [Table("ExplicitActives", Schema = "fixture")]
    [TableType]
    public class ExplicitActive : TopLevelEntity, IActivatable
    {
        private bool _isActive = true;

        bool IActivatable.IsActive
        {
            get => _isActive;
            set => _isActive = value;
        }
    }

    /// <summary>A top-level entity without a table type.</summary>
    [Table("Untyped", Schema = "fixture")]
    public class Untyped : TopLevelEntity;

    /// <summary>A top-level entity whose configuration leaves an audit column out of its table type.</summary>
    [Table("Excluding", Schema = "fixture")]
    [TableType]
    public class Excluding : TopLevelEntity;

    /// <summary>Leaves <see cref="TopLevelEntity{TKey}.ModifiedAt" /> out of the table type.</summary>
    public sealed class ExcludingConfiguration : IEntityTypeConfiguration<Excluding>
    {
        public void Configure(EntityTypeBuilder<Excluding> builder)
        {
            _ = builder.Property(static entity => entity.ModifiedAt).ExcludeFromTableType();
        }
    }

    /// <summary>A tree whose configuration puts the shadow node into its table type.</summary>
    [Table("NodeTyped", Schema = "fixture")]
    [TableType]
    public class NodeTyped : TreeEntity
    {
        [MaxLength(20)]
        [NaturalKey]
        public string Code { get; set; } = null!;
    }

    /// <summary>Re-includes the node column in the table type.</summary>
    public sealed class NodeTypedConfiguration : IEntityTypeConfiguration<NodeTyped>
    {
        public void Configure(EntityTypeBuilder<NodeTyped> builder)
        {
            _ = builder.Property<HierarchyId>("Node").IncludeInTableType();
        }
    }

    // --- Trees ---

    /// <summary>A tree too shallow to nest.</summary>
    [Table("Shallow", Schema = "fixture")]
    [TableType]
    [Tree(MaxDepth = 1)]
    public class ShallowTree : TreeEntity
    {
        [MaxLength(20)]
        [NaturalKey]
        public string Code { get; set; } = null!;
    }

    /// <summary>A tree deeper than the platform allows.</summary>
    [Table("Deep", Schema = "fixture")]
    [TableType]
    [Tree(MaxDepth = 40)]
    public class DeepTree : TreeEntity
    {
        [MaxLength(20)]
        [NaturalKey]
        public string Code { get; set; } = null!;
    }

    /// <summary>A tree whose node index a configuration renames.</summary>
    [Table("Renode", Schema = "fixture")]
    [TableType]
    public class RenodedTree : TreeEntity
    {
        [MaxLength(20)]
        [NaturalKey]
        public string Code { get; set; } = null!;
    }

    /// <summary>Gives the node index another name.</summary>
    public sealed class RenodedTreeConfiguration : IEntityTypeConfiguration<RenodedTree>
    {
        public void Configure(EntityTypeBuilder<RenodedTree> builder)
        {
            _ = builder.Property<HierarchyId>("Node");
            _ = builder.HasIndex("Node").IsUnique().HasDatabaseName("IX_Renode_Path");
        }
    }

    // --- Children ---

    /// <summary>A parent of children that break the pairing rules.</summary>
    [Table("Parents", Schema = "fixture")]
    [TableType]
    public class BadParent : TopLevelEntity
    {
        [NotMapped]
        public List<TwoKeyed>? TwoKeyed { get; set; }
    }

    /// <summary>A child with two parent keys.</summary>
    [Table("TwoKeyed", Schema = "fixture")]
    [TableType]
    public class TwoKeyed : ChildEntity
    {
        [ParentKey]
        public int BadParentId { get; set; }

        [ParentKey]
        public int OtherParentId { get; set; }
    }

    /// <summary>A parent whose child declares no parent key.</summary>
    [Table("Orphanages", Schema = "fixture")]
    [TableType]
    public class Orphanage : TopLevelEntity
    {
        [NotMapped]
        public List<Keyless>? Keyless { get; set; }
    }

    /// <summary>A child without a parent key.</summary>
    [Table("Keyless", Schema = "fixture")]
    [TableType]
    public class Keyless : ChildEntity
    {
        public int OrphanageId { get; set; }
    }

    /// <summary>A top-level entity with a parent key.</summary>
    [Table("Misparented", Schema = "fixture")]
    [TableType]
    public class Misparented : TopLevelEntity
    {
        [ParentKey]
        [References(typeof(Tabled))]
        public int TabledId { get; set; }
    }

    /// <summary>A [NotMapped] list of a class that is not a child.</summary>
    [Table("Listers", Schema = "fixture")]
    [TableType]
    public class Lister : TopLevelEntity
    {
        [NotMapped]
        public List<Tabled>? Others { get; set; }
    }

    /// <summary>A child carried by a system entity, which no top-level entity owns.</summary>
    [Table("Systems", Schema = "fixture")]
    public class SystemParent : Entity<int>
    {
        [NotMapped]
        public List<SystemChild>? Children { get; set; }
    }

    /// <summary>The child of <see cref="SystemParent" />.</summary>
    [Table("SystemChildren", Schema = "fixture")]
    [TableType]
    public class SystemChild : ChildEntity
    {
        [ParentKey]
        public int SystemParentId { get; set; }
    }

    /// <summary>A child contributed on its own, which no collection carries.</summary>
    [Table("Strays", Schema = "fixture")]
    [TableType]
    public class Stray : ChildEntity
    {
        [ParentKey]
        [References(typeof(Tabled))]
        public int TabledId { get; set; }
    }

    /// <summary>A parent with a zero cap on its children.</summary>
    [Table("Capped", Schema = "fixture")]
    [TableType]
    public class Capped : TopLevelEntity
    {
        [NotMapped]
        [MaxChildren(0)]
        public List<CappedChild>? Children { get; set; }
    }

    /// <summary>The child of <see cref="Capped" />.</summary>
    [Table("CappedChildren", Schema = "fixture")]
    [TableType]
    public class CappedChild : ChildEntity
    {
        [ParentKey]
        public int CappedId { get; set; }
    }

    /// <summary>One of two children that carry each other.</summary>
    [Table("CycleA", Schema = "fixture")]
    [TableType]
    public class CycleA : ChildEntity
    {
        [ParentKey]
        public int CycleBId { get; set; }

        [NotMapped]
        public List<CycleB>? Bs { get; set; }
    }

    /// <summary>The other of two children that carry each other.</summary>
    [Table("CycleB", Schema = "fixture")]
    [TableType]
    public class CycleB : ChildEntity
    {
        [ParentKey]
        public int CycleAId { get; set; }

        [NotMapped]
        public List<CycleA>? As { get; set; }
    }

    // --- Temporal ---

    /// <summary>A non-temporal root of a temporal child.</summary>
    [Table("Plain", Schema = "fixture")]
    [TableType]
    public class PlainRoot : TopLevelEntity
    {
        [NotMapped]
        public List<VersionedChild>? Children { get; set; }
    }

    /// <summary>A temporal child of a non-temporal root.</summary>
    [Table("VersionedChildren", Schema = "fixture")]
    [TableType]
    [Temporal]
    public class VersionedChild : ChildEntity
    {
        [ParentKey]
        public int PlainRootId { get; set; }
    }

    // --- Multilingual ---

    /// <summary>A group missing its third twin.</summary>
    [Table("TwinLess", Schema = "fixture")]
    [TableType]
    public class TwinLess : TopLevelEntity
    {
        [MaxLength(100)]
        [Multilingual]
        public string Title { get; set; } = null!;

        public string? Title2 { get; set; }
    }

    /// <summary>A group whose second twin is shorter than its anchor.</summary>
    [Table("Mismatched", Schema = "fixture")]
    [TableType]
    public class Mismatched : TopLevelEntity
    {
        [MaxLength(100)]
        [Multilingual]
        public string Title { get; set; } = null!;

        [MaxLength(50)]
        public string? Title2 { get; set; }

        public string? Title3 { get; set; }
    }

    /// <summary>A group whose third twin is required.</summary>
    [Table("RequiredTwins", Schema = "fixture")]
    [TableType]
    public class RequiredTwin : TopLevelEntity
    {
        [MaxLength(100)]
        [Multilingual]
        public string Title { get; set; } = null!;

        public string? Title2 { get; set; }

        [Required]
        public string Title3 { get; set; } = null!;
    }

    // --- Display names ---

    /// <summary>Names a display-name source it does not have.</summary>
    [Table("Unsourced", Schema = "fixture")]
    [TableType]
    [DisplayNameSource("Missing")]
    public class Unsourced : TopLevelEntity;

    /// <summary>Renders a flag as its display name.</summary>
    [Table("FlagNamed", Schema = "fixture")]
    [TableType]
    [DisplayNameSource(nameof(Flag))]
    public class FlagNamed : TopLevelEntity
    {
        public bool Flag { get; set; }
    }

    /// <summary>Prefixes its display name with a decimal.</summary>
    [Table("AmountPrefixed", Schema = "fixture")]
    [TableType]
    [DisplayNameSource(nameof(Title), Prefix = nameof(Amount))]
    public class AmountPrefixed : TopLevelEntity
    {
        [MaxLength(100)]
        public string Title { get; set; } = null!;

        [Column(TypeName = "decimal(19,4)")]
        public decimal Amount { get; set; }
    }

    /// <summary>Renders a date with an integer prefix, which is allowed.</summary>
    [Table("Dated", Schema = "fixture")]
    [TableType]
    [DisplayNameSource(nameof(On), Prefix = nameof(Serial))]
    public class Dated : TopLevelEntity
    {
        public DateOnly On { get; set; }

        public int Serial { get; set; }
    }

    /// <summary>A related projection that drops the code and the group's twins.</summary>
    [Table("Projected", Schema = "fixture")]
    [TableType]
    [RelatedSelect("Id,Name")]
    public class Projected : TopLevelEntity
    {
        [MaxLength(100)]
        [Multilingual]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }

        [MaxLength(20)]
        public string? Code { get; set; }
    }

    /// <summary>A related projection with every column it must keep.</summary>
    [Table("FullyProjected", Schema = "fixture")]
    [TableType]
    [RelatedSelect("Id, Name, Name2, Name3, Code, Extra")]
    public class FullyProjected : TopLevelEntity
    {
        [MaxLength(100)]
        [Multilingual]
        public string Name { get; set; } = null!;

        public string? Name2 { get; set; }

        public string? Name3 { get; set; }

        [MaxLength(20)]
        public string? Code { get; set; }

        [MaxLength(20)]
        public string? Extra { get; set; }
    }

    // --- Uniqueness ---

    /// <summary>A unique JSON column.</summary>
    [Table("UniqueJson", Schema = "fixture")]
    [TableType]
    public class UniqueJson : TopLevelEntity
    {
        [JsonColumn]
        [Unique]
        public string? Settings { get; set; }
    }

    /// <summary>A natural key of a type no key list binds.</summary>
    [Table("DecimalKeyed", Schema = "fixture")]
    [TableType]
    public class DecimalKeyed : TopLevelEntity
    {
        [Column(TypeName = "decimal(19,4)")]
        [NaturalKey]
        public decimal Rate { get; set; }
    }

    /// <summary>A natural key on a binary column.</summary>
    [Table("BinaryKeyed", Schema = "fixture")]
    [TableType]
    public class BinaryKeyed : TopLevelEntity
    {
        [MaxLength(32)]
        [NaturalKey]
        public byte[] Hash { get; set; } = [];
    }

    /// <summary>A unique property whose index a configuration makes non-unique.</summary>
    [Table("Ununique", Schema = "fixture")]
    [TableType]
    public class Ununique : TopLevelEntity
    {
        [MaxLength(20)]
        [Unique]
        public string Code { get; set; } = null!;
    }

    /// <summary>Makes <see cref="Ununique.Code" />'s index non-unique.</summary>
    public sealed class UnuniqueConfiguration : IEntityTypeConfiguration<Ununique>
    {
        public void Configure(EntityTypeBuilder<Ununique> builder)
        {
            _ = builder.HasIndex(static entity => entity.Code).IsUnique(false).HasDatabaseName("IX_Ununique_Code");
        }
    }

    // --- Navigations ---

    /// <summary>A navigation that collides with a property.</summary>
    [Table("Colliding", Schema = "fixture")]
    [TableType]
    public class Colliding : TopLevelEntity
    {
        [References(typeof(Tabled))]
        public int? OwnerId { get; set; }

        [MaxLength(50)]
        public string? Owner { get; set; }
    }

    /// <summary>An owner whose foreign key and sibling both name the navigation Extra.</summary>
    [Table("SiblingOwners", Schema = "fixture")]
    [TableType]
    public class SiblingOwner : TopLevelEntity
    {
        [References(typeof(Tabled))]
        public int? ExtraId { get; set; }
    }

    /// <summary>The sibling of <see cref="SiblingOwner" />.</summary>
    [Table("SiblingOwnerExtras", Schema = "fixture")]
    [Sibling(typeof(SiblingOwner), "Extra")]
    public class SiblingOwnerExtra : Entity<int>;

    /// <summary>A foreign key whose name gives no navigation.</summary>
    [Table("Unnavigable", Schema = "fixture")]
    [TableType]
    public class Unnavigable : TopLevelEntity
    {
        [References(typeof(Tabled))]
        public int? OwnerRef { get; set; }
    }

    // --- Blob references ---

    /// <summary>A blob reference on a non-nullable column.</summary>
    [Table("RequiredBlobs", Schema = "fixture")]
    [TableType]
    public class RequiredBlob : TopLevelEntity
    {
        [BlobReference("required-image")]
        public int ImageId { get; set; }
    }

    /// <summary>A blob reference whose kind is not a valid kind.</summary>
    [Table("BadKinds", Schema = "fixture")]
    [TableType]
    public class BadKind : TopLevelEntity
    {
        [BlobReference("Bad Kind")]
        public int? FileId { get; set; }
    }

    /// <summary>Two blob references of one kind.</summary>
    [Table("SharedKinds", Schema = "fixture")]
    [TableType]
    public class SharedKind : TopLevelEntity
    {
        [BlobReference("shared-file")]
        public int? FirstId { get; set; }

        [BlobReference("shared-file")]
        public int? SecondId { get; set; }
    }

    /// <summary>A blob reference that widens its preset's size limit.</summary>
    [Table("Widened", Schema = "fixture")]
    [TableType]
    public class Widened : TopLevelEntity
    {
        [BlobReference("widened-avatar", BlobPreset.Avatar, MaxSizeBytes = long.MaxValue)]
        public int? ImageId { get; set; }
    }

    // --- Enums ---

    /// <summary>A status stored by number.</summary>
    public enum Phase
    {
        Draft,
        Final,
    }

    /// <summary>An enum a configuration stores as a number.</summary>
    [Table("NumberedEnums", Schema = "fixture")]
    [TableType]
    public class NumberedPhase : TopLevelEntity
    {
        public Phase Phase { get; set; }
    }

    /// <summary>Stores <see cref="NumberedPhase.Phase" /> as a number.</summary>
    public sealed class NumberedPhaseConfiguration : IEntityTypeConfiguration<NumberedPhase>
    {
        public void Configure(EntityTypeBuilder<NumberedPhase> builder)
        {
            _ = builder.Property(static entity => entity.Phase).HasConversion<int>();
        }
    }

    /// <summary>An enum a configuration stores as unicode text.</summary>
    [Table("UnicodeEnums", Schema = "fixture")]
    [TableType]
    public class UnicodePhase : TopLevelEntity
    {
        public Phase Phase { get; set; }
    }

    /// <summary>Stores <see cref="UnicodePhase.Phase" /> as <c>nvarchar(40)</c>.</summary>
    public sealed class UnicodePhaseConfiguration : IEntityTypeConfiguration<UnicodePhase>
    {
        public void Configure(EntityTypeBuilder<UnicodePhase> builder)
        {
            _ = builder.Property(static entity => entity.Phase).HasColumnType("nvarchar(40)");
        }
    }

    // --- Ownership ---

    /// <summary>A property with two ownerships.</summary>
    [Table("DoublyOwned", Schema = "fixture")]
    [TableType]
    public class DoublyOwned : TopLevelEntity
    {
        [MaxLength(20)]
        [WriteOnce]
        [Derived]
        public string? Code { get; set; }
    }

    /// <summary>A parent of a child with a server-owned parent key.</summary>
    [Table("OwnedKeyParents", Schema = "fixture")]
    [TableType]
    public class OwnedKeyParent : TopLevelEntity
    {
        [NotMapped]
        public List<OwnedKeyChild>? Children { get; set; }
    }

    /// <summary>A child whose parent key is server-owned.</summary>
    [Table("OwnedKeyChildren", Schema = "fixture")]
    [TableType]
    public class OwnedKeyChild : ChildEntity
    {
        [ParentKey]
        [ServerOwned]
        public int OwnedKeyParentId { get; set; }
    }

    /// <summary>A derived computed column.</summary>
    [Table("DerivedComputed", Schema = "fixture")]
    [TableType]
    public class DerivedComputed : TopLevelEntity
    {
        [MaxLength(20)]
        public string? Code { get; set; }

        [MaxLength(40)]
        [Derived]
        public string? Upper { get; set; }
    }

    /// <summary>Computes <see cref="DerivedComputed.Upper" />.</summary>
    public sealed class DerivedComputedConfiguration : IEntityTypeConfiguration<DerivedComputed>
    {
        public void Configure(EntityTypeBuilder<DerivedComputed> builder)
        {
            _ = builder.Property(static entity => entity.Upper).HasComputedColumnSql("UPPER([Code])");
        }
    }

    /// <summary>A job entity that declares its server-owned job link write-once.</summary>
    [Table("WriteOnceJobs", Schema = "fixture")]
    [TableType]
    public class WriteOnceJob : TopLevelEntity, IJobEntity
    {
        [WriteOnce]
        public int? JobId { get; set; }
    }

    /// <summary>A server-owned property declared defaulted.</summary>
    [Table("DefaultedOwned", Schema = "fixture")]
    [TableType]
    public class DefaultedOwned : TopLevelEntity
    {
        [MaxLength(20)]
        [ServerOwned]
        [Defaulted]
        public string? Code { get; set; }
    }

    // --- Annotations ---

    /// <summary>An annotation on a property the model does not map.</summary>
    [Table("UnmappedUnique", Schema = "fixture")]
    [TableType]
    public class UnmappedUnique : TopLevelEntity
    {
        [NotMapped]
        [Unique]
        public string? Display { get; set; }
    }

    /// <summary>A string annotation on a number.</summary>
    [Table("SearchedNumbers", Schema = "fixture")]
    [TableType]
    public class SearchedNumber : TopLevelEntity
    {
        [Searchable]
        public int Count { get; set; }
    }

    /// <summary>A non-public property a configuration maps.</summary>
    [Table("Secretive", Schema = "fixture")]
    [TableType]
    public class Secretive : TopLevelEntity
    {
        [MaxLength(20)]
        internal string? Secret { get; set; }
    }

    /// <summary>Maps <c>Secret</c>.</summary>
    public sealed class SecretiveConfiguration : IEntityTypeConfiguration<Secretive>
    {
        public void Configure(EntityTypeBuilder<Secretive> builder)
        {
            _ = builder.Property<string>("Secret").HasMaxLength(20);
        }
    }

    /// <summary>A foreign key over a shadow column.</summary>
    [Table("ShadowKeyed", Schema = "fixture")]
    [TableType]
    public class ShadowKeyed : TopLevelEntity;

    /// <summary>Adds a shadow foreign key to the jobs table.</summary>
    public sealed class ShadowKeyedConfiguration : IEntityTypeConfiguration<ShadowKeyed>
    {
        public void Configure(EntityTypeBuilder<ShadowKeyed> builder)
        {
            _ = builder.HasOne<MdJob>().WithMany().HasForeignKey("ShadowJobId");
        }
    }

    // --- Leaves ---

    /// <summary>A class contributed as an entity that does not derive from the entity base.</summary>
    [Table("NotEntities", Schema = "fixture")]
    public class NotAnEntity
    {
        public int Id { get; set; }
    }

    /// <summary>A generic entity class.</summary>
    /// <typeparam name="T">Any type.</typeparam>
    [Table("Generics", Schema = "fixture")]
    [TableType]
    public class GenericEntity<T> : TopLevelEntity;

    /// <summary>A base two leaves share.</summary>
    public abstract class Party : TopLevelEntity
    {
        [MaxLength(100)]
        [Unique]
        public string Code { get; set; } = null!;
    }

    /// <summary>One leaf of <see cref="Party" />.</summary>
    [Table("Customers", Schema = "fixture")]
    [TableType]
    public class Customer : Party;

    /// <summary>The other leaf of <see cref="Party" />.</summary>
    [Table("Suppliers", Schema = "fixture")]
    [TableType]
    public class Supplier : Party;

    /// <summary>
    ///     A closed configuration of the replaced default <see cref="MdUser" />, which re-adds it beside its
    ///     leaf; the row image of the shared table cannot hold the leaf's column, so no model is built.
    /// </summary>
    public sealed class DefaultUserConfiguration : IEntityTypeConfiguration<MdUser>
    {
        public void Configure(EntityTypeBuilder<MdUser> builder)
        {
            _ = builder.HasIndex(static user => user.Name).HasDatabaseName("IX_Users_Name");
        }
    }

    /// <summary>A closed configuration of the replaced default <see cref="Tabled" />, whose leaf adds no column.</summary>
    public sealed class DefaultTabledConfiguration : IEntityTypeConfiguration<Tabled>
    {
        public void Configure(EntityTypeBuilder<Tabled> builder)
        {
            _ = builder.HasIndex(static entity => entity.CreatedAt).HasDatabaseName("IX_Tabled_CreatedAt");
        }
    }

    /// <summary>Holds the first of two entities that share an entity name.</summary>
    public static class First
    {
        /// <summary>The first <c>fixture.Duplicate</c>.</summary>
        [Table("DuplicatesA", Schema = "fixture")]
        [TableType]
        public class Duplicate : TopLevelEntity;
    }

    /// <summary>Holds the second of two entities that share an entity name.</summary>
    public static class Second
    {
        /// <summary>The second <c>fixture.Duplicate</c>.</summary>
        [Table("DuplicatesB", Schema = "fixture")]
        [TableType]
        public class Duplicate : TopLevelEntity;
    }

    /// <summary>References an entity the model does not map, which the model cannot be built with.</summary>
    [Table("Dangling", Schema = "fixture")]
    [TableType]
    public class Dangling : TopLevelEntity
    {
        [References(typeof(Tabled))]
        public int? TabledId { get; set; }
    }

    // --- Mappings the metadata cannot describe ---

    /// <summary>An entity a configuration maps to a view rather than a table.</summary>
    public class Viewed : Entity<int>
    {
        [MaxLength(20)]
        public string? Code { get; set; }
    }

    /// <summary>Maps <see cref="Viewed" /> to a view.</summary>
    public sealed class ViewedConfiguration : IEntityTypeConfiguration<Viewed>
    {
        public void Configure(EntityTypeBuilder<Viewed> builder)
        {
            _ = builder.ToView("Viewed", "fixture");
        }
    }

    /// <summary>A class derived from a contributed entity and contributed itself, so it shares its base's table.</summary>
    public class VipCustomer : Customer;

    /// <summary>The first default of a leaf registered for two.</summary>
    [Table("TwoDefaults", Schema = "fixture")]
    [TableType]
    public class FirstDefault : TopLevelEntity;

    /// <summary>The second default, derived from the first.</summary>
    public class SecondDefault : FirstDefault;

    /// <summary>The leaf registered for both defaults.</summary>
    public sealed class TwoDefaultsLeaf : SecondDefault;

    /// <summary>An owner whose sibling shares its table.</summary>
    [Table("Split", Schema = "fixture")]
    [TableType]
    public class SplitOwner : TopLevelEntity;

    /// <summary>A sibling mapped to its owner's table.</summary>
    [Table("Split", Schema = "fixture")]
    [Sibling(typeof(SplitOwner), "Half")]
    public class SplitHalf : Entity<int>
    {
        [MaxLength(20)]
        public string? Note { get; set; }
    }

    /// <summary>An entity the model builds through its constructor, which has no parameterless one.</summary>
    [Table("Constructed", Schema = "fixture")]
    [TableType]
    public class Constructed(string code) : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = code;
    }
}
