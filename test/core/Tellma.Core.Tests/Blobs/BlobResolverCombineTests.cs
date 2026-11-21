// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The download combination over canned result sets: a committed blob under a readable owner
    ///     and not otherwise, a staged one served without an owner, nothing for no row or a released
    ///     one, and a variant served only when the row lists it and the kind declares its format,
    ///     with each served download's storage name, content type, image flag and ETag.
    /// </summary>
    public sealed class BlobResolverCombineTests
    {
        /// <summary>The blob's id.</summary>
        private const int Id = 77;

        /// <summary>An attachment kind, with a WebP thumbnail.</summary>
        private static readonly BlobKindDescriptor Files = Kind("fx-file", BlobKindPolicy.Attachment);

        /// <summary>A signature kind, with no thumbnail.</summary>
        private static readonly BlobKindDescriptor Signatures = Kind("fx-signature", BlobKindPolicy.Signature);

        /// <summary>The primary's storage name.</summary>
        private static readonly string StorageKey = BlobName.Primary(Files.Kind, "0123456789abcdef0123456789abcdef");

        [Fact]
        public void A_committed_blob_is_served_under_a_readable_owner_and_hidden_otherwise()
        {
            ResolvedBlob committed = Row(BlobState.Committed, BlobContentTypes.Pdf);

            BlobDownload served = Assert.IsType<BlobDownload>(BlobResolver.Combine(Files, null, committed, ownerVisible: true));
            Assert.Equal(new BlobDownload(Id, Files.Kind, BlobState.Committed, StorageKey, BlobContentTypes.Pdf, 1234, "report.pdf", false, "\"77\""), served);

            Assert.Null(BlobResolver.Combine(Files, null, committed, ownerVisible: false));
        }

        [Fact]
        public void A_staged_blob_is_served_without_an_owner_and_no_row_or_a_released_one_is_nothing()
        {
            BlobDownload staged = Assert.IsType<BlobDownload>(BlobResolver.Combine(Files, null, Row(BlobState.Staged, BlobContentTypes.Png), ownerVisible: false));
            Assert.Equal((BlobState.Staged, true, "\"77\""), (staged.State, staged.IsImage, staged.ETag));

            Assert.Null(BlobResolver.Combine(Files, null, null, ownerVisible: true));
            Assert.Null(BlobResolver.Combine(Files, null, Row(BlobState.Released, BlobContentTypes.Png), ownerVisible: true));
            Assert.Null(BlobResolver.Combine(Files, null, Row(BlobState.Deleting, BlobContentTypes.Png), ownerVisible: true));
        }

        [Fact]
        public void A_listed_thumbnail_is_served_in_the_kinds_format_under_its_own_name_and_etag()
        {
            BlobDownload thumb = Assert.IsType<BlobDownload>(BlobResolver.Combine(Files, ThumbnailPolicy.VariantName, Row(BlobState.Committed, BlobContentTypes.Pdf, "thumb"), ownerVisible: true));

            Assert.Equal(
                new BlobDownload(Id, Files.Kind, BlobState.Committed, BlobName.Variant(StorageKey, "thumb"), BlobContentTypes.WebP, 1234, "report.pdf", true, "\"77-thumb\""),
                thumb);
        }

        [Fact]
        public void An_unlisted_variant_or_one_the_kind_declares_no_format_for_is_nothing()
        {
            Assert.Null(BlobResolver.Combine(Files, ThumbnailPolicy.VariantName, Row(BlobState.Committed, BlobContentTypes.Pdf), ownerVisible: true));
            Assert.Null(BlobResolver.Combine(Files, "small", Row(BlobState.Committed, BlobContentTypes.Pdf, "thumb,small"), ownerVisible: true));
            Assert.Null(BlobResolver.Combine(Signatures, ThumbnailPolicy.VariantName, Row(BlobState.Committed, BlobContentTypes.Png, "thumb"), ownerVisible: true));

            // --- A listed variant of a committed blob is still hidden without its owner ---
            Assert.Null(BlobResolver.Combine(Files, ThumbnailPolicy.VariantName, Row(BlobState.Committed, BlobContentTypes.Pdf, "thumb"), ownerVisible: false));
        }

        /// <summary>A blob row as the download's read returns it.</summary>
        /// <param name="state">The state.</param>
        /// <param name="contentType">The primary's content type.</param>
        /// <param name="variants">The listed variants, comma-separated.</param>
        /// <returns>The row.</returns>
        private static ResolvedBlob Row(BlobState state, string contentType, string variants = "")
        {
            return new ResolvedBlob(Id, Files.Kind, state, StorageKey, contentType, 1234, "report.pdf", null, null, variants);
        }

        /// <summary>A kind owned by a root column.</summary>
        /// <param name="kind">The kind.</param>
        /// <param name="policy">The policy.</param>
        /// <returns>The descriptor.</returns>
        private static BlobKindDescriptor Kind(string kind, BlobKindPolicy policy)
        {
            return new BlobKindDescriptor(kind, policy, "fx.Owner", "fx.Owner", new TableName("fx", "Owners"), "FileId", string.Empty);
        }
    }
}
