// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Queries
{
    // A sound model for the query statements: a top-level entity with a multilingual name, a prefixed
    // display name, enums (one nullable), a JSON column, a binary-collated unique serial, columns the
    // language does not read, a reference to a tree and a sibling; a child with a JSON column of its
    // own and a grandchild; and a tree.

    /// <summary>Composes the query model into a composition.</summary>
    internal static class QueryModel
    {
        /// <summary>Adds the query model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.B>("queries", contribute: Contribute));
        }

        /// <summary>The feature's entities.</summary>
        /// <param name="contribution">The contribution.</param>
        private static void Contribute(FeatureContribution contribution)
        {
            _ = contribution
                .Entity<Gadget>()
                .Entity<GadgetExtra>()
                .Entity<Zone>();
        }
    }

    /// <summary>A gadget's kind.</summary>
    public enum GadgetKind
    {
        /// <summary>Small.</summary>
        Small,

        /// <summary>Large.</summary>
        Large,
    }

    /// <summary>The query model's top-level entity.</summary>
    [Table("Gadgets", Schema = "qx")]
    [Temporal]
    [TableType]
    [DisplayNameSource(nameof(Name), Prefix = nameof(Code))]
    public class Gadget : TopLevelEntity
    {
        /// <summary>The code, the display name's prefix.</summary>
        [MaxLength(50)]
        [NaturalKey]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name's second twin.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name's third twin.</summary>
        public string? Name3 { get; set; }

        /// <summary>The kind.</summary>
        public GadgetKind Kind { get; set; }

        /// <summary>A nullable kind.</summary>
        public GadgetKind? OptionalKind { get; set; }

        /// <summary>The price.</summary>
        [Column(TypeName = "decimal(19,4)")]
        public decimal? Price { get; set; }

        /// <summary>A date.</summary>
        public DateOnly? DueOn { get; set; }

        /// <summary>A serial: unique where present, through a filtered index, and compared byte by byte.</summary>
        [MaxLength(64)]
        [Unique]
        [Collation(DatabaseCollations.Binary)]
        public string? Serial { get; set; }

        /// <summary>A thumbnail, <c>varbinary</c>: a column the query language does not read.</summary>
        public byte[]? Thumbnail { get; set; }

        /// <summary>An opening time, <c>time</c>: a column the query language does not read.</summary>
        public TimeOnly? OpensAt { get; set; }

        /// <summary>A ratio, <c>float</c>: a column the query language does not read.</summary>
        public double? Ratio { get; set; }

        /// <summary>The settings, as JSON.</summary>
        [JsonColumn]
        public string? Settings { get; set; }

        /// <summary>The zone.</summary>
        [References(typeof(Zone))]
        public int? ZoneId { get; set; }

        /// <summary>The parts.</summary>
        [NotMapped]
        public List<GadgetPart>? Parts { get; set; }
    }

    /// <summary>A gadget's part.</summary>
    [Table("GadgetParts", Schema = "qx")]
    [TableType]
    public class GadgetPart : ChildEntity
    {
        /// <summary>The gadget.</summary>
        [ParentKey]
        public int GadgetId { get; set; }

        /// <summary>The position.</summary>
        [Unique]
        public int Ordinal { get; set; }

        /// <summary>The notes.</summary>
        [MaxLength(1024)]
        public string? Notes { get; set; }

        /// <summary>The part's own settings, as JSON.</summary>
        [JsonColumn]
        public string? Settings { get; set; }

        /// <summary>The part's notes.</summary>
        [NotMapped]
        public List<GadgetPartNote>? PartNotes { get; set; }
    }

    /// <summary>A part's note.</summary>
    [Table("GadgetPartNotes", Schema = "qx")]
    [TableType]
    public class GadgetPartNote : ChildEntity
    {
        /// <summary>The part.</summary>
        [ParentKey]
        public int GadgetPartId { get; set; }

        /// <summary>The text.</summary>
        [MaxLength(500)]
        public string Text { get; set; } = null!;
    }

    /// <summary>A gadget's sibling row.</summary>
    [Table("GadgetExtras", Schema = "qx")]
    [Sibling(typeof(Gadget), "Extra")]
    public class GadgetExtra : Entity<int>
    {
        /// <summary>A note.</summary>
        [MaxLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>A tree.</summary>
    [Table("Zones", Schema = "qx")]
    [TableType]
    public class Zone : ActivatableTreeEntity
    {
        /// <summary>The code.</summary>
        [MaxLength(50)]
        [NaturalKey]
        public string Code { get; set; } = null!;

        /// <summary>The name.</summary>
        [MaxLength(255)]
        [Multilingual]
        public string Name { get; set; } = null!;

        /// <summary>The name's second twin.</summary>
        public string? Name2 { get; set; }

        /// <summary>The name's third twin.</summary>
        public string? Name3 { get; set; }
    }
}
