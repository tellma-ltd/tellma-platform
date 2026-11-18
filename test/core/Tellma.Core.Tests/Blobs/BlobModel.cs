// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    // The blob suite's model: an album stack three levels deep, a blob reference at each level,
    // one child with a query surface of its own and one without, and a plain entity outside any
    // stack's aggregate.

    /// <summary>Composes the blob suite's model.</summary>
    internal static class BlobModel
    {
        /// <summary>The album stack's resource.</summary>
        public const string Albums = "bm.BmAlbum";

        /// <summary>The page child query surface's resource.</summary>
        public const string Pages = "bm.BmPage";

        /// <summary>The root's kind: an image kind.</summary>
        public const string CoverKind = "bm-cover";

        /// <summary>The child's kind: an attachment kind with a thumbnail.</summary>
        public const string ScanKind = "bm-scan";

        /// <summary>The grandchild's kind, under a child without a query surface.</summary>
        public const string StickerKind = "bm-sticker";

        /// <summary>A feature stacking the album, then whatever else the scenario contributes.</summary>
        /// <param name="contribute">The rest of the scenario's contribution.</param>
        /// <returns>The feature.</returns>
        public static TestFeature<Tag.D> Feature(Action<FeatureContribution>? contribute = null)
        {
            return new TestFeature<Tag.D>("bm", contribute: contribution =>
            {
                _ = contribution.Entity<BmAlbum>();
                contribute?.Invoke(contribution);
            });
        }
    }

    /// <summary>An album: the aggregate root, with a cover image.</summary>
    [Table("Albums", Schema = "bm")]
    [TableType]
    [Stack(Operations = StackOperations.All)]
    public class BmAlbum : TopLevelEntity
    {
        /// <summary>The name.</summary>
        [MaxLength(255)]
        public string Name { get; set; } = null!;

        /// <summary>The cover.</summary>
        [BlobReference(BlobModel.CoverKind, BlobPreset.Photo)]
        public int? CoverId { get; set; }

        /// <summary>The pages.</summary>
        [NotMapped]
        public List<BmPage>? Pages { get; set; }

        /// <summary>The spreads, whose type has no query surface.</summary>
        [NotMapped]
        public List<BmSpread>? Spreads { get; set; }
    }

    /// <summary>A page of an album, with a query surface of its own and a scan.</summary>
    [Table("Pages", Schema = "bm")]
    [TableType]
    public class BmPage : ChildEntity
    {
        /// <summary>The album.</summary>
        [ParentKey]
        public int AlbumId { get; set; }

        /// <summary>The scan.</summary>
        [BlobReference(BlobModel.ScanKind)]
        public int? ScanId { get; set; }
    }

    /// <summary>A spread of an album, without a query surface, with stickers.</summary>
    [Table("Spreads", Schema = "bm")]
    [TableType]
    [ApiResource(Queryable = false)]
    public class BmSpread : ChildEntity
    {
        /// <summary>The album.</summary>
        [ParentKey]
        public int AlbumId { get; set; }

        /// <summary>The stickers.</summary>
        [NotMapped]
        public List<BmSticker>? Stickers { get; set; }
    }

    /// <summary>A sticker on a spread, whose owner has no query surface.</summary>
    [Table("Stickers", Schema = "bm")]
    [TableType]
    [ApiResource(Queryable = false)]
    public class BmSticker : ChildEntity
    {
        /// <summary>The spread.</summary>
        [ParentKey]
        public int SpreadId { get; set; }

        /// <summary>The sticker's image.</summary>
        [BlobReference(BlobModel.StickerKind, BlobPreset.Signature)]
        public int? ImageId { get; set; }
    }

    /// <summary>A plain entity outside any stack's aggregate, declaring a blob reference no securable governs.</summary>
    [Table("Loose", Schema = "bm")]
    [TableType]
    public class BmLoose : TopLevelEntity
    {
        /// <summary>The file.</summary>
        [BlobReference("bm-loose")]
        public int? FileId { get; set; }
    }

    /// <summary>Maps <see cref="BmLoose" /> as a model configuration only, never as an entity of a stack.</summary>
    public sealed class BmLooseConfiguration : IEntityTypeConfiguration<BmLoose>
    {
        /// <inheritdoc />
        public void Configure(EntityTypeBuilder<BmLoose> builder)
        {
            _ = builder.Property(static loose => loose.FileId);
        }
    }
}
