// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;
using System.Text;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.AspNetCore.Blobs
{
    // Spec 0017 §5.3: what every blob download response carries, computed from the resolved blob
    // alone so a conditional request is answered without the store.

    /// <summary>
    ///     The headers of a blob download: the framing and sniffing guards on every response, the
    ///     caching a blob's state allows, whether it opens inline or downloads, and whether a
    ///     conditional request already holds the bytes.
    /// </summary>
    internal static class BlobResponseHeaders
    {
        /// <summary>The content security policy of every blob response: only the platform's own pages may embed it.</summary>
        public const string ContentSecurityPolicy = "frame-ancestors 'self'";

        /// <summary>The value of <c>X-Content-Type-Options</c>: the browser never second-guesses the served type.</summary>
        public const string NoSniff = "nosniff";

        /// <summary>
        ///     The caching of a committed blob: a year without revalidation, never shared. The bytes
        ///     behind an id never change, and a new upload is a new id and so a new address.
        /// </summary>
        public const string CommittedCacheControl = "private, max-age=31536000, immutable";

        /// <summary>The caching of a staged blob, which only its uploader may read and a save may still drop.</summary>
        public const string StagedCacheControl = "private, no-store";

        // The media types a browser may show in place: images, PDF and the text types. Everything
        // else (OOXML, zip) downloads, as does any type when the caller asks to download.
        private static readonly HashSet<string> InlineTypes = new(
            [.. BlobContentTypes.Images, BlobContentTypes.Pdf, .. BlobContentTypes.TextTypes],
            StringComparer.OrdinalIgnoreCase);

        /// <summary>The <c>Cache-Control</c> value of a blob in a state.</summary>
        /// <param name="state">The resolved blob's state.</param>
        /// <returns><see cref="CommittedCacheControl" /> for a committed blob; <see cref="StagedCacheControl" /> otherwise.</returns>
        public static string CacheControl(BlobState state)
        {
            return state == BlobState.Committed ? CommittedCacheControl : StagedCacheControl;
        }

        /// <summary>Whether a served media type opens in the browser rather than downloading.</summary>
        /// <param name="contentType">The served media type, as the server determined it.</param>
        /// <param name="download">Whether the caller asked to download.</param>
        /// <returns>True when the response is inline.</returns>
        public static bool IsInline(string contentType, bool download)
        {
            ArgumentNullException.ThrowIfNull(contentType);
            return !download && InlineTypes.Contains(contentType);
        }

        /// <summary>
        ///     The <c>Content-Disposition</c> value: <c>inline</c> or <c>attachment</c>, with the
        ///     blob's file name as an RFC 8187 <c>filename*</c> parameter when the upload named one.
        /// </summary>
        /// <param name="contentType">The served media type.</param>
        /// <param name="fileName">The sanitised file name; null when the upload named none.</param>
        /// <param name="download">Whether the caller asked to download.</param>
        /// <returns>The header value.</returns>
        public static string ContentDisposition(string contentType, string? fileName, bool download)
        {
            string type = IsInline(contentType, download) ? "inline" : "attachment";
            return string.IsNullOrEmpty(fileName) ? type : $"{type}; filename*=UTF-8''{EncodeFileName(fileName)}";
        }

        /// <summary>
        ///     Whether an <c>If-None-Match</c> header names the blob's entity tag (or <c>*</c>), so the
        ///     caller already holds the bytes. The comparison is the weak one the header calls for.
        /// </summary>
        /// <param name="ifNoneMatch">The request's header values.</param>
        /// <param name="entityTag">The blob's strong entity tag, quotes included.</param>
        /// <returns>True when the response is <c>304</c>.</returns>
        public static bool IsNotModified(StringValues ifNoneMatch, string entityTag)
        {
            ArgumentNullException.ThrowIfNull(entityTag);
            if (StringValues.IsNullOrEmpty(ifNoneMatch) || !EntityTagHeaderValue.TryParseList(ifNoneMatch, out IList<EntityTagHeaderValue>? tags))
            {
                return false;
            }

            EntityTagHeaderValue current = new(entityTag);
            return tags.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
        }

        /// <summary>
        ///     Stamps the headers every blob response carries, a refusal included: the framing policy
        ///     and the sniffing guard.
        /// </summary>
        /// <param name="headers">The response's headers.</param>
        public static void ApplyGuards(IHeaderDictionary headers)
        {
            ArgumentNullException.ThrowIfNull(headers);
            headers[HeaderNames.ContentSecurityPolicy] = ContentSecurityPolicy;
            headers[HeaderNames.XContentTypeOptions] = NoSniff;
        }

        /// <summary>Stamps the headers a resolved blob's answer carries, a <c>304</c> included: its tag and its caching.</summary>
        /// <param name="headers">The response's headers.</param>
        /// <param name="download">The resolved blob.</param>
        public static void ApplyCaching(IHeaderDictionary headers, BlobDownload download)
        {
            ArgumentNullException.ThrowIfNull(headers);
            ArgumentNullException.ThrowIfNull(download);
            headers[HeaderNames.ETag] = download.ETag;
            headers[HeaderNames.CacheControl] = CacheControl(download.State);
        }

        /// <summary>
        ///     Percent-encodes a file name as an RFC 8187 value: its UTF-8 bytes, with only the
        ///     attribute characters left as they are.
        /// </summary>
        /// <param name="fileName">The file name.</param>
        /// <returns>The encoded value.</returns>
        internal static string EncodeFileName(string fileName)
        {
            ArgumentNullException.ThrowIfNull(fileName);
            StringBuilder encoded = new(fileName.Length * 3);
            foreach (byte value in Encoding.UTF8.GetBytes(fileName))
            {
                _ = IsAttributeCharacter(value)
                    ? encoded.Append((char)value)
                    : encoded.Append('%').Append(value.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            return encoded.ToString();
        }

        /// <summary>Whether a byte is an RFC 8187 <c>attr-char</c>, which a value carries unencoded.</summary>
        /// <param name="value">The byte.</param>
        /// <returns>True for an ASCII letter or digit, or one of <c>!#$&amp;+-.^_`|~</c>.</returns>
        private static bool IsAttributeCharacter(byte value)
        {
            return value is (>= (byte)'a' and <= (byte)'z') or (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'0' and <= (byte)'9')
                or (byte)'!' or (byte)'#' or (byte)'$' or (byte)'&' or (byte)'+' or (byte)'-' or (byte)'.' or (byte)'^' or (byte)'_' or (byte)'`' or (byte)'|' or (byte)'~';
        }
    }
}
