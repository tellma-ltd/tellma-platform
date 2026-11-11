// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Testing.Entities
{
    // The fixture schema: one table per shape the data layer's suites exercise, each carrying the
    // capability sets and annotations its row in spec 0012 §13.2 names.

    /// <summary>A widget's kind, stored by name.</summary>
    public enum WidgetKind
    {
        /// <summary>A plain widget.</summary>
        Plain,

        /// <summary>A fancy widget.</summary>
        Fancy,
    }

    /// <summary>A shipment's state, stored by name.</summary>
    public enum ShipmentState
    {
        /// <summary>Waiting for its job.</summary>
        Pending,

        /// <summary>Shipped by its job.</summary>
        Shipped,
    }

    /// <summary>
    ///     A top-level, system-versioned, activatable entity with a natural key, multilingual names, a
    ///     write-once unique subject, a JSON column and an image reference.
    /// </summary>
    [Table("Widgets", Schema = FixtureSchema.Name)]
    [Temporal]
    [TableType]
    public class Widget : TopLevelEntity, IActivatable
    {
        /// <summary>The natural key, <c>varchar(50)</c>, unique when present.</summary>
        [Column(TypeName = "varchar(50)")]
        [NaturalKey]
        public string? Code { get; set; }

        /// <summary>The kind, <c>varchar(8)</c>.</summary>
        [MaxLength(8)]
        public WidgetKind Kind { get; set; }

        /// <summary>The name in the first content language.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name in the second content language.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name in the third content language.</summary>
        public string? Name3 { get; set; }

        /// <summary>The subject, set on create only and unique.</summary>
        [MaxLength(64)]
        [WriteOnce]
        [Unique]
        public string Subject { get; set; } = null!;

        /// <summary>The price, <c>decimal(19,4)</c>.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Price { get; set; }

        /// <summary>Free-form settings, a JSON document.</summary>
        [JsonColumn]
        public string? Settings { get; set; }

        /// <summary>The widget's image.</summary>
        [BlobReference("fixture-image", BlobPreset.Avatar)]
        public int? ImageId { get; set; }

        /// <inheritdoc />
        public bool IsActive { get; set; } = true;

        /// <summary>The widget's parts, saved with it.</summary>
        [NotMapped]
        public List<WidgetPart>? Parts { get; set; }
    }

    /// <summary>A child of <see cref="Widget" />, system-versioned with its parent.</summary>
    [Table("WidgetParts", Schema = FixtureSchema.Name)]
    [TableType]
    public class WidgetPart : ChildEntity
    {
        /// <summary>The widget.</summary>
        [ParentKey]
        public int WidgetId { get; set; }

        /// <summary>The part's position, unique within its widget.</summary>
        [Unique]
        public int Ordinal { get; set; }

        /// <summary>Notes.</summary>
        [MaxLength(1024)]
        public string? Notes { get; set; }

        /// <summary>Free-form settings, a JSON document.</summary>
        [JsonColumn]
        public string? Settings { get; set; }

        /// <summary>The part's notes, saved with it.</summary>
        [NotMapped]
        public List<WidgetPartNote>? PartNotes { get; set; }
    }

    /// <summary>A grandchild of <see cref="Widget" />.</summary>
    [Table("WidgetPartNotes", Schema = FixtureSchema.Name)]
    [TableType]
    public class WidgetPartNote : ChildEntity
    {
        /// <summary>The part.</summary>
        [ParentKey]
        public int WidgetPartId { get; set; }

        /// <summary>The note's text.</summary>
        public string Text { get; set; } = null!;
    }

    /// <summary>A one-row-per-widget sibling of <see cref="Widget" />.</summary>
    [Table("WidgetExtras", Schema = FixtureSchema.Name)]
    [Sibling(typeof(Widget), "Extra")]
    public class WidgetExtra : Entity<int>
    {
        /// <summary>A note.</summary>
        [MaxLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>An activatable tree with a declared depth.</summary>
    [Table("Nodes", Schema = FixtureSchema.Name)]
    [Tree(MaxDepth = 8)]
    [TableType]
    public class Node : ActivatableTreeEntity
    {
        /// <summary>The code, <c>varchar(50)</c>, unique.</summary>
        [Column(TypeName = "varchar(50)")]
        [Unique]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The node's links, saved with it.</summary>
        [NotMapped]
        public List<Link>? Links { get; set; }
    }

    /// <summary>A child of <see cref="Node" /> whose second foreign key to it is an ordinary reference.</summary>
    [Table("Links", Schema = FixtureSchema.Name)]
    [TableType]
    public class Link : ChildEntity
    {
        /// <summary>The node the link belongs to.</summary>
        [ParentKey]
        public int NodeId { get; set; }

        /// <summary>The node the link points at.</summary>
        [References(typeof(Node))]
        public int OtherNodeId { get; set; }
    }

    /// <summary>A job entity with a <see cref="long" /> key.</summary>
    [Table("Shipments", Schema = FixtureSchema.Name)]
    [TableType]
    public class Shipment : TopLevelEntity<long>, IJobEntity
    {
        /// <summary>The state, <c>varchar(8)</c>.</summary>
        [MaxLength(8)]
        public ShipmentState State { get; set; }

        /// <summary>The payload.</summary>
        public string? Payload { get; set; }

        /// <inheritdoc />
        public int? JobId { get; set; }
    }

    /// <summary>The fixture schema's name.</summary>
    public static class FixtureSchema
    {
        /// <summary>The schema of the fixture's own tables.</summary>
        public const string Name = "fixture";
    }
}
