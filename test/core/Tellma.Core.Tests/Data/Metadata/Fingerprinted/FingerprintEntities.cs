// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Tests.Data.Metadata.Fingerprinted
{
    // Pairs of entities that differ in exactly one respect, for the fingerprint suite. The schema
    // fingerprint's shapes share one model; each storage variant maps the table fixture.Stores in a
    // model of its own.

    // --- Schema fingerprints: an aggregate and its variants ---

    /// <summary>The reference shape: two editable columns, a server-owned one and a child collection.</summary>
    [Table("Shapes", Schema = "fixture")]
    [TableType]
    public class Shape : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = null!;

        public int Count { get; set; }

        [MaxLength(20)]
        [ServerOwned]
        public string? Stamp { get; set; }

        [NotMapped]
        public List<ShapeLine>? Lines { get; set; }
    }

    /// <summary>The reference shape's child.</summary>
    [Table("ShapeLines", Schema = "fixture")]
    [TableType]
    public class ShapeLine : ChildEntity
    {
        [ParentKey]
        public int ParentId { get; set; }

        [MaxLength(50)]
        public string? Text { get; set; }
    }

    /// <summary>The reference shape under another class and table: the same shape.</summary>
    [Table("SameShapes", Schema = "fixture")]
    [TableType]
    public class SameShape : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = null!;

        public int Count { get; set; }

        [MaxLength(20)]
        [ServerOwned]
        public string? Stamp { get; set; }

        [NotMapped]
        public List<SameShapeLine>? Lines { get; set; }
    }

    /// <summary>The same shape's child, identical to the reference child.</summary>
    [Table("SameShapeLines", Schema = "fixture")]
    [TableType]
    public class SameShapeLine : ChildEntity
    {
        [ParentKey]
        public int ParentId { get; set; }

        [MaxLength(50)]
        public string? Text { get; set; }
    }

    /// <summary>The reference shape with one editable column nullable: another shape.</summary>
    [Table("NullableShapes", Schema = "fixture")]
    [TableType]
    public class NullableShape : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = null!;

        public int? Count { get; set; }

        [MaxLength(20)]
        [ServerOwned]
        public string? Stamp { get; set; }

        [NotMapped]
        public List<NullableShapeLine>? Lines { get; set; }
    }

    /// <summary>The nullable shape's child, identical to the reference child.</summary>
    [Table("NullableShapeLines", Schema = "fixture")]
    [TableType]
    public class NullableShapeLine : ChildEntity
    {
        [ParentKey]
        public int ParentId { get; set; }

        [MaxLength(50)]
        public string? Text { get; set; }
    }

    /// <summary>The reference shape with a longer server-owned column: the same importable shape.</summary>
    [Table("OwnedShapes", Schema = "fixture")]
    [TableType]
    public class OwnedShape : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = null!;

        public int Count { get; set; }

        [MaxLength(40)]
        [ServerOwned]
        public string? Stamp { get; set; }

        [NotMapped]
        public List<OwnedShapeLine>? Lines { get; set; }
    }

    /// <summary>The owned shape's child, identical to the reference child.</summary>
    [Table("OwnedShapeLines", Schema = "fixture")]
    [TableType]
    public class OwnedShapeLine : ChildEntity
    {
        [ParentKey]
        public int ParentId { get; set; }

        [MaxLength(50)]
        public string? Text { get; set; }
    }

    /// <summary>The reference shape whose child's column is longer: another shape.</summary>
    [Table("ChildShapes", Schema = "fixture")]
    [TableType]
    public class ChildShape : TopLevelEntity
    {
        [MaxLength(20)]
        public string Code { get; set; } = null!;

        public int Count { get; set; }

        [MaxLength(20)]
        [ServerOwned]
        public string? Stamp { get; set; }

        [NotMapped]
        public List<ChildShapeLine>? Lines { get; set; }
    }

    /// <summary>The child shape's child, whose column is longer than the reference child's.</summary>
    [Table("ChildShapeLines", Schema = "fixture")]
    [TableType]
    public class ChildShapeLine : ChildEntity
    {
        [ParentKey]
        public int ParentId { get; set; }

        [MaxLength(60)]
        public string? Text { get; set; }
    }

    // --- Storage fingerprints: one table, each variant changing one storage fact ---

    /// <summary>The reference storage: <c>fixture.Stores</c> with one name column.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class Store : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The reference storage under another class: the same storage.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreTwin : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The reference storage with another column.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreWithPhone : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(20)]
        public string? Phone { get; set; }
    }

    /// <summary>The reference storage with its name nullable.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreNullable : TopLevelEntity
    {
        [MaxLength(100)]
        public string? Name { get; set; }
    }

    /// <summary>The reference storage with its name indexed.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    [TableIndex(nameof(Name))]
    public class StoreIndexed : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }

    /// <summary>The reference storage with its name defaulted.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreDefaulted : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;
    }

    /// <summary>Gives <see cref="StoreDefaulted.Name" /> a default.</summary>
    public sealed class StoreDefaultedConfiguration : IEntityTypeConfiguration<StoreDefaulted>
    {
        public void Configure(EntityTypeBuilder<StoreDefaulted> builder)
        {
            _ = builder.Property(static store => store.Name).HasDefaultValue("unnamed");
        }
    }

    /// <summary>A store with a scratch column inside its table type.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreWithScratch : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(20)]
        public string? Scratch { get; set; }
    }

    /// <summary>The same table as <see cref="StoreWithScratch" />, its scratch column left out of the table type.</summary>
    [Table("Stores", Schema = "fixture")]
    [TableType]
    public class StoreScratchExcluded : TopLevelEntity
    {
        [MaxLength(100)]
        public string Name { get; set; } = null!;

        [MaxLength(20)]
        [ExcludeFromTableType]
        public string? Scratch { get; set; }
    }
}
