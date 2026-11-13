// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Data.Saves
{
    // A model for the save statement: a top-level entity with a blob column, a child with a blob column
    // of its own, a grandchild, and a reference between rows of the root's table; a top-level entity
    // with a long key and no child; a tree; and a sibling with no sequence.

    /// <summary>Composes the save model into a composition.</summary>
    internal static class SaveModel
    {
        /// <summary>Adds the save model's feature.</summary>
        /// <param name="tellma">The composition's builder.</param>
        public static void Compose(TellmaBuilder tellma)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.C>("saves", contribute: static contribution => contribution
                .Entity<Crate>()
                .Entity<CrateExtra>()
                .Entity<Ledger>()
                .Entity<Area>()));
        }
    }

    /// <summary>The save model's top-level entity: a blob column, a child collection and a self reference.</summary>
    [Table("Crates", Schema = "sv")]
    [Temporal]
    [TableType]
    public class Crate : TopLevelEntity
    {
        /// <summary>The name, compared byte by byte when the save tells a changed row.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The crate this one is packed in: a reference to another row of the table.</summary>
        [References(typeof(Crate))]
        public int? OuterId { get; set; }

        /// <summary>The label image.</summary>
        [BlobReference("save-label", BlobPreset.Avatar)]
        public int? LabelId { get; set; }

        /// <summary>The compartments.</summary>
        [NotMapped]
        public List<Compartment>? Compartments { get; set; }
    }

    /// <summary>A crate's compartment: a child with a blob column.</summary>
    [Table("Compartments", Schema = "sv")]
    [TableType]
    public class Compartment : ChildEntity
    {
        /// <summary>The crate.</summary>
        [ParentKey]
        public int CrateId { get; set; }

        /// <summary>The position.</summary>
        public int Position { get; set; }

        /// <summary>The compartment's photo.</summary>
        [BlobReference("save-photo", BlobPreset.Photo)]
        public int? PhotoId { get; set; }

        /// <summary>The items.</summary>
        [NotMapped]
        public List<Item>? Items { get; set; }
    }

    /// <summary>A compartment's item: a grandchild of the crate.</summary>
    [Table("Items", Schema = "sv")]
    [TableType]
    public class Item : ChildEntity
    {
        /// <summary>The compartment.</summary>
        [ParentKey]
        public int CompartmentId { get; set; }

        /// <summary>The label.</summary>
        [MaxLength(100)]
        public string Label { get; set; } = null!;
    }

    /// <summary>A crate's sibling row, whose id is the crate's.</summary>
    [Table("CrateExtras", Schema = "sv")]
    [Sibling(typeof(Crate), "Extra")]
    public class CrateExtra : Entity<int>
    {
        /// <summary>A note.</summary>
        [MaxLength(255)]
        public string? Note { get; set; }
    }

    /// <summary>A top-level entity with a long key.</summary>
    [Table("Ledgers", Schema = "sv")]
    [TableType]
    public class Ledger : TopLevelEntity<long>
    {
        /// <summary>The memo.</summary>
        [MaxLength(255)]
        public string? Memo { get; set; }
    }

    /// <summary>A tree.</summary>
    [Table("Areas", Schema = "sv")]
    [TableType]
    public class Area : ActivatableTreeEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;
    }
}
