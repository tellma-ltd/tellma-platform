// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Blobs
{
    /// <summary>
    ///     The media types blob kinds allow, spelled once, lowercase and without parameters — the
    ///     form the server determines and stores for every blob.
    /// </summary>
    public static class BlobContentTypes
    {
        /// <summary>A PDF document.</summary>
        public const string Pdf = "application/pdf";

        /// <summary>A Word document (OOXML).</summary>
        public const string Docx = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

        /// <summary>An Excel workbook (OOXML).</summary>
        public const string Xlsx = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

        /// <summary>A PowerPoint presentation (OOXML).</summary>
        public const string Pptx = "application/vnd.openxmlformats-officedocument.presentationml.presentation";

        /// <summary>A JPEG image.</summary>
        public const string Jpeg = "image/jpeg";

        /// <summary>A PNG image.</summary>
        public const string Png = "image/png";

        /// <summary>A WebP image.</summary>
        public const string WebP = "image/webp";

        /// <summary>A GIF image.</summary>
        public const string Gif = "image/gif";

        /// <summary>Plain text.</summary>
        public const string Text = "text/plain";

        /// <summary>Comma-separated values.</summary>
        public const string Csv = "text/csv";

        /// <summary>A JSON document.</summary>
        public const string Json = "application/json";

        /// <summary>A zip archive that is not one of the OOXML types.</summary>
        public const string Zip = "application/zip";

        /// <summary>The four image types every image kind accepts as input.</summary>
        public static IReadOnlyList<string> Images { get; } = Array.AsReadOnly([Jpeg, Png, WebP, Gif]);

        /// <summary>The three OOXML document types, each a zip archive underneath.</summary>
        public static IReadOnlyList<string> OfficeOpenXml { get; } = Array.AsReadOnly([Docx, Xlsx, Pptx]);

        /// <summary>The text types, whose type comes from the declaration because text has no signature.</summary>
        public static IReadOnlyList<string> TextTypes { get; } = Array.AsReadOnly([Text, Csv, Json]);

        /// <summary>The media type an image processor writes for <paramref name="format" />.</summary>
        /// <param name="format">The output format.</param>
        /// <returns><see cref="WebP" />, <see cref="Jpeg" /> or <see cref="Png" />.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="format" /> is not a defined format.</exception>
        public static string ForImageFormat(ImageFormat format)
        {
            return format switch
            {
                ImageFormat.WebP => WebP,
                ImageFormat.Jpeg => Jpeg,
                ImageFormat.Png => Png,
                _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Not a defined image format."),
            };
        }
    }
}
