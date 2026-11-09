// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     Recognises the accepted inputs by their leading bytes, in managed code, before any native
    ///     library sees them.
    /// </summary>
    /// <remarks>
    ///     Skia's codec factory also decodes BMP, ICO, WBMP and camera raw formats, whose parsers the
    ///     platform has no use for. Refusing everything but the four image signatures (and the PDF
    ///     signature for thumbnails) before the codec is created keeps those parsers out of reach of
    ///     uploaded bytes.
    /// </remarks>
    internal static class ImageSignatures
    {
        /// <summary>How many leading bytes the signatures need: the longest, WebP's, is 12.</summary>
        public const int Length = 12;

        // The signatures, as the WHATWG MIME-sniffing standard spells them.
        private static ReadOnlySpan<byte> Jpeg => [0xFF, 0xD8, 0xFF];

        private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private static ReadOnlySpan<byte> Gif87a => "GIF87a"u8;

        private static ReadOnlySpan<byte> Gif89a => "GIF89a"u8;

        private static ReadOnlySpan<byte> Riff => "RIFF"u8;

        private static ReadOnlySpan<byte> Webp => "WEBP"u8;

        private static ReadOnlySpan<byte> Pdf => "%PDF-"u8;

        /// <summary>The media type of the image the bytes begin with, among the four accepted ones.</summary>
        /// <param name="head">The input's first bytes; at least <see cref="Length" /> unless the input is shorter.</param>
        /// <returns><c>image/jpeg</c>, <c>image/png</c>, <c>image/gif</c> or <c>image/webp</c>; null for anything else.</returns>
        public static string? SniffImage(ReadOnlySpan<byte> head)
        {
            if (head.StartsWith(Jpeg))
            {
                return BlobContentTypes.Jpeg;
            }

            if (head.StartsWith(Png))
            {
                return BlobContentTypes.Png;
            }

            if (head.StartsWith(Gif87a) || head.StartsWith(Gif89a))
            {
                return BlobContentTypes.Gif;
            }

            // RIFF, a four-byte length, then the form type.
            return head.Length >= Length && head.StartsWith(Riff) && head[8..12].SequenceEqual(Webp)
                ? BlobContentTypes.WebP
                : null;
        }

        /// <summary>Whether the bytes begin with the PDF header.</summary>
        /// <param name="head">The input's first bytes.</param>
        /// <returns>True for a PDF.</returns>
        public static bool IsPdf(ReadOnlySpan<byte> head)
        {
            return head.StartsWith(Pdf);
        }

        /// <summary>The Skia format a sniffed image media type decodes as.</summary>
        /// <param name="contentType">One of the four image media types.</param>
        /// <returns>The encoded format Skia's codec must report.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="contentType" /> is not an image type.</exception>
        public static SKEncodedImageFormat FormatOf(string contentType)
        {
            return contentType switch
            {
                BlobContentTypes.Jpeg => SKEncodedImageFormat.Jpeg,
                BlobContentTypes.Png => SKEncodedImageFormat.Png,
                BlobContentTypes.Gif => SKEncodedImageFormat.Gif,
                BlobContentTypes.WebP => SKEncodedImageFormat.Webp,
                _ => throw new ArgumentOutOfRangeException(nameof(contentType), contentType, "Not an accepted image media type."),
            };
        }

        /// <summary>
        ///     Parses a stored media type as a bare, lowercase <c>type/subtype</c>, dropping any
        ///     parameters.
        /// </summary>
        /// <param name="contentType">The media type.</param>
        /// <returns>The bare media type.</returns>
        public static string Normalise(string contentType)
        {
            int semicolon = contentType.IndexOf(';', StringComparison.Ordinal);
            ReadOnlySpan<char> bare = semicolon < 0 ? contentType.AsSpan() : contentType.AsSpan(0, semicolon);
            return bare.Trim().ToString().ToLowerInvariant();
        }
    }
}
