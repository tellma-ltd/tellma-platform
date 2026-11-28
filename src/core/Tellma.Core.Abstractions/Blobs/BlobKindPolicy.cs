// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     What a blob kind accepts and stores: its size limit, its allowed media types, whether its
    ///     images are normalised, whether it renders a thumbnail, how long a staged upload waits to
    ///     be attached, and who may download it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A kind starts from the preset its <see cref="BlobReferenceAttribute" /> names (see
    ///         <see cref="For" />), narrowed by the attribute, and a composition then transforms it
    ///         with <c>with</c> expressions (<c>p =&gt; p with { MaxSize = 4 * 1024 * 1024 }</c>). The
    ///         server stays the authority on every rule here; the client receives the policy only to
    ///         pre-check a file before uploading it.
    ///     </para>
    ///     <para>
    ///         Records compare by value, but <see cref="AllowedContentTypes" /> compares by reference;
    ///         compare its elements when two policies' types must match.
    ///     </para>
    /// </remarks>
    public sealed record BlobKindPolicy
    {
        // The preset sizes, in bytes.
        private const long Mebibyte = 1024L * 1024;

        /// <summary>
        ///     The largest upload the kind accepts, in bytes; for an image kind, the largest input
        ///     before normalisation. Never above the platform's global upload ceiling.
        /// </summary>
        public required long MaxSize { get; init; }

        /// <summary>
        ///     The media types the kind stores, lowercase and without parameters (see
        ///     <see cref="BlobContentTypes" />). The server determines a file's type from its bytes;
        ///     a type outside this set is refused.
        /// </summary>
        public required IReadOnlyList<string> AllowedContentTypes { get; init; }

        /// <summary>When set, every upload is an image the server re-encodes per this policy.</summary>
        public ImagePolicy? Image { get; init; }

        /// <summary>When set, a <c>thumb</c> variant is stored whenever the primary can be rendered.</summary>
        public ThumbnailPolicy? Thumbnail { get; init; }

        /// <summary>How long a staged upload of this kind waits to be attached; null takes the platform default.</summary>
        public TimeSpan? StagingTtl { get; init; }

        /// <summary>Who may download a committed blob of this kind.</summary>
        public BlobReadAccess ReadAccess { get; init; } = BlobReadAccess.OwnerRead;

        /// <summary>
        ///     Documents, images, text and archives: 100 MiB; PDF, the three OOXML types, the four
        ///     image types, plain text, CSV, JSON and zip; no image normalisation; a 256-pixel WebP
        ///     thumbnail where the file can be rendered.
        /// </summary>
        public static BlobKindPolicy Attachment { get; } = new()
        {
            MaxSize = 100 * Mebibyte,
            AllowedContentTypes = Array.AsReadOnly(
            [
                BlobContentTypes.Pdf,
                BlobContentTypes.Docx,
                BlobContentTypes.Xlsx,
                BlobContentTypes.Pptx,
                BlobContentTypes.Jpeg,
                BlobContentTypes.Png,
                BlobContentTypes.WebP,
                BlobContentTypes.Gif,
                BlobContentTypes.Text,
                BlobContentTypes.Csv,
                BlobContentTypes.Json,
                BlobContentTypes.Zip,
            ]),
            Thumbnail = new ThumbnailPolicy(256),
        };

        /// <summary>
        ///     A profile picture: up to 10 MiB of input in one of the four image types, centre-cropped
        ///     to a 512-pixel WebP square, with a 96-pixel WebP thumbnail.
        /// </summary>
        public static BlobKindPolicy Avatar { get; } = new()
        {
            MaxSize = 10 * Mebibyte,
            AllowedContentTypes = BlobContentTypes.Images,
            Image = new ImagePolicy(512, ImageFit.CoverSquare),
            Thumbnail = new ThumbnailPolicy(96),
        };

        /// <summary>
        ///     A photograph: up to 10 MiB of input in one of the four image types, fitted within
        ///     1600 pixels as WebP, with a 256-pixel WebP thumbnail.
        /// </summary>
        public static BlobKindPolicy Photo { get; } = new()
        {
            MaxSize = 10 * Mebibyte,
            AllowedContentTypes = BlobContentTypes.Images,
            Image = new ImagePolicy(1600, ImageFit.Contain),
            Thumbnail = new ThumbnailPolicy(256),
        };

        /// <summary>
        ///     A handwritten signature: up to 2 MiB of input in one of the four image types, fitted
        ///     within 400 pixels as PNG with its alpha channel preserved, so it can be layered over a
        ///     document; no thumbnail.
        /// </summary>
        public static BlobKindPolicy Signature { get; } = new()
        {
            MaxSize = 2 * Mebibyte,
            AllowedContentTypes = BlobContentTypes.Images,
            Image = new ImagePolicy(400, ImageFit.Contain, ImageFormat.Png),
        };

        /// <summary>The policy a <see cref="BlobPreset" /> starts a kind from.</summary>
        /// <param name="preset">The preset.</param>
        /// <returns>The preset's policy.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="preset" /> is not a defined preset.</exception>
        public static BlobKindPolicy For(BlobPreset preset)
        {
            return preset switch
            {
                BlobPreset.Attachment => Attachment,
                BlobPreset.Avatar => Avatar,
                BlobPreset.Photo => Photo,
                BlobPreset.Signature => Signature,
                _ => throw new ArgumentOutOfRangeException(nameof(preset), preset, "Not a defined blob preset."),
            };
        }
    }
}
