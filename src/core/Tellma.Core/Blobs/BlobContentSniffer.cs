// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Abstractions.Blobs;

namespace Tellma.Core.Blobs
{
    /// <summary>Why <see cref="BlobContentSniffer" /> refused a file.</summary>
    internal enum ContentTypeRefusal
    {
        /// <summary>Not refused: the file's type was determined and is allowed.</summary>
        None,

        /// <summary>The type was determined, but the kind does not allow it.</summary>
        NotAllowed,

        /// <summary>An executable, HTML or SVG: refused whatever the declaration or the allowed set.</summary>
        AlwaysRejected,

        /// <summary>No signature matched, and the declaration is not an allowed text type.</summary>
        Unrecognised,
    }

    /// <summary>The outcome of determining an upload's media type.</summary>
    /// <param name="ContentType">The type: the determined one when accepted; otherwise the best
    ///     description of what was refused (the sniffed type, else the parsed declaration, else
    ///     <c>application/octet-stream</c>), for the error's argument and the log.</param>
    /// <param name="Refusal">Why the file was refused, or <see cref="ContentTypeRefusal.None" />.</param>
    internal sealed record ContentTypeDecision(string ContentType, ContentTypeRefusal Refusal)
    {
        /// <summary>Whether the file is accepted under <see cref="ContentType" />.</summary>
        public bool IsAccepted => Refusal == ContentTypeRefusal.None;
    }

    /// <summary>
    ///     Determines an upload's media type from its first bytes. The server, not the client,
    ///     decides what a blob is: signatures win over the declaration, the declaration counts only
    ///     for zip-based documents and text, and executables, HTML and SVG are never stored.
    /// </summary>
    /// <remarks>
    ///     The rules, in order:
    ///     <list type="number">
    ///         <item>An executable (<c>MZ</c>, ELF, Mach-O), HTML or SVG is refused, whatever the
    ///         declaration or the kind allows. Markup is recognised after a byte-order mark and
    ///         leading whitespace, in UTF-8 or UTF-16.</item>
    ///         <item>JPEG, PNG, GIF, WebP and PDF take their sniffed type, whatever was declared.</item>
    ///         <item>A zip archive takes the declared type when it is one of the three OOXML types or
    ///         <c>application/zip</c>, else <c>application/zip</c> when the kind allows it, else it
    ///         is refused.</item>
    ///         <item>Text (<c>text/plain</c>, <c>text/csv</c>, <c>application/json</c>) takes the
    ///         declared type when the kind allows it and the sniffed window holds no NUL byte.</item>
    ///         <item>Anything else is refused.</item>
    ///     </list>
    ///     A determined type outside the kind's allowed set is refused too. The declaration is
    ///     parsed as a media type without parameters, ignoring case.
    /// </remarks>
    internal static class BlobContentSniffer
    {
        /// <summary>How many leading bytes the sniffer inspects: 8 KiB.</summary>
        public const int SniffLength = 8 * 1024;

        /// <summary>The description of a refused file with no recognisable type and no usable declaration.</summary>
        public const string UnknownContentType = "application/octet-stream";

        // An HTML comment, which opens a document a browser sniffs as HTML.
        private const string HtmlComment = "<!--";

        // The HTML signatures the WHATWG MIME-sniffing standard recognises, lowercase. Each tag must
        // be followed by a byte that ends a tag name (whitespace, '/' or '>'), so "<br" does not
        // match "<brand"; that is slightly broader than the standard's space or '>', on purpose.
        private static readonly string[] HtmlTags =
        [
            "<!doctype html", "<html", "<head", "<script", "<iframe", "<h1", "<div", "<font", "<table",
            "<a", "<style", "<title", "<b", "<body", "<br", "<p", HtmlComment,
        ];

        /// <summary>Determines the media type of an upload.</summary>
        /// <param name="head">The upload's first bytes, up to <see cref="SniffLength" />; all of it when shorter.</param>
        /// <param name="declaredContentType">The client's declaration (a <c>Content-Type</c> value), or null.</param>
        /// <param name="allowedContentTypes">The kind's allowed types, lowercase and without parameters.</param>
        /// <returns>The decision.</returns>
        public static ContentTypeDecision Determine(
            ReadOnlySpan<byte> head, string? declaredContentType, IReadOnlyList<string> allowedContentTypes)
        {
            ArgumentNullException.ThrowIfNull(allowedContentTypes);

            if (head.Length > SniffLength)
            {
                head = head[..SniffLength];
            }

            string? declared = ParseMediaType(declaredContentType);

            // 1. Never stored, whatever was declared or allowed.
            if (IsExecutable(head) || IsMarkup(head))
            {
                return new ContentTypeDecision(declared ?? UnknownContentType, ContentTypeRefusal.AlwaysRejected);
            }

            // 2. A signature is the truth; the declaration is ignored.
            string? sniffed = SniffSignature(head);
            if (sniffed is not null)
            {
                return Allow(sniffed, allowedContentTypes);
            }

            // 3. A zip archive cannot say which OOXML document it is from its first bytes, so the
            // declaration picks among the zip-based types; anything else is a plain zip.
            if (IsZip(head))
            {
                bool zipBased = declared is not null
                    && (BlobContentTypes.OfficeOpenXml.Contains(declared) || declared == BlobContentTypes.Zip);
                return Allow(zipBased ? declared! : BlobContentTypes.Zip, allowedContentTypes);
            }

            // 4. Text has no signature; the declaration decides, provided the bytes are not binary.
            if (declared is not null && BlobContentTypes.TextTypes.Contains(declared) && !head.Contains((byte)0))
            {
                return Allow(declared, allowedContentTypes);
            }

            // 5. Nothing recognisable.
            return new ContentTypeDecision(declared ?? UnknownContentType, ContentTypeRefusal.Unrecognised);
        }

        /// <summary>
        ///     Parses a <c>Content-Type</c> value as a bare media type: the part before any
        ///     parameters, trimmed and lowercased, when it is a well-formed <c>type/subtype</c>.
        /// </summary>
        /// <param name="value">The value, or null.</param>
        /// <returns>The media type, or null when absent or malformed.</returns>
        public static string? ParseMediaType(string? value)
        {
            if (value is null)
            {
                return null;
            }

            int semicolon = value.IndexOf(';', StringComparison.Ordinal);
            ReadOnlySpan<char> mediaType = (semicolon < 0 ? value.AsSpan() : value.AsSpan(0, semicolon)).Trim();
            int slash = mediaType.IndexOf('/');
            return slash > 0
                && slash < mediaType.Length - 1
                && IsToken(mediaType[..slash])
                && IsToken(mediaType[(slash + 1)..])
                    ? mediaType.ToString().ToLowerInvariant()
                    : null;
        }

        /// <summary>Accepts a determined type when the kind allows it.</summary>
        /// <param name="contentType">The determined type.</param>
        /// <param name="allowedContentTypes">The kind's allowed types.</param>
        /// <returns>The decision.</returns>
        private static ContentTypeDecision Allow(string contentType, IReadOnlyList<string> allowedContentTypes)
        {
            bool allowed = allowedContentTypes.Any(allowedType => string.Equals(allowedType, contentType, StringComparison.OrdinalIgnoreCase));
            return new ContentTypeDecision(contentType, allowed ? ContentTypeRefusal.None : ContentTypeRefusal.NotAllowed);
        }

        /// <summary>The type of a file with a recognised binary signature.</summary>
        /// <param name="head">The first bytes.</param>
        /// <returns>The type, or null when no signature matches.</returns>
        private static string? SniffSignature(ReadOnlySpan<byte> head)
        {
            // JPEG: SOI marker followed by the first segment's marker byte.
            if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]))
            {
                return BlobContentTypes.Jpeg;
            }

            // PNG: the eight-byte signature.
            if (head.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
            {
                return BlobContentTypes.Png;
            }

            // GIF: either version header.
            if (head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8))
            {
                return BlobContentTypes.Gif;
            }

            // WebP: a RIFF container whose form type is WEBP.
            if (head.Length >= 12 && head.StartsWith("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8))
            {
                return BlobContentTypes.WebP;
            }

            // PDF: the header at offset 0.
            return head.StartsWith("%PDF-"u8) ? BlobContentTypes.Pdf : null;
        }

        /// <summary>Whether the bytes start a zip archive: a local file header, an empty archive, or a spanned one.</summary>
        /// <param name="head">The first bytes.</param>
        /// <returns>True for a zip archive.</returns>
        private static bool IsZip(ReadOnlySpan<byte> head)
        {
            return head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04])
                || head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x05, 0x06])
                || head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x07, 0x08]);
        }

        /// <summary>Whether the bytes start a native executable: PE (<c>MZ</c>), ELF or Mach-O.</summary>
        /// <param name="head">The first bytes.</param>
        /// <returns>True for an executable.</returns>
        private static bool IsExecutable(ReadOnlySpan<byte> head)
        {
            return head.StartsWith("MZ"u8)
                || head.StartsWith((ReadOnlySpan<byte>)[0x7F, 0x45, 0x4C, 0x46])

                // Mach-O, 32- and 64-bit in both byte orders, and the universal (fat) container.
                || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCE])
                || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCF])
                || head.StartsWith((ReadOnlySpan<byte>)[0xCE, 0xFA, 0xED, 0xFE])
                || head.StartsWith((ReadOnlySpan<byte>)[0xCF, 0xFA, 0xED, 0xFE])
                || head.StartsWith((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBE])
                || head.StartsWith((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBF]);
        }

        /// <summary>Whether the bytes are HTML or SVG, after an optional byte-order mark and whitespace.</summary>
        /// <param name="head">The first bytes.</param>
        /// <returns>True for markup a browser could render as a document.</returns>
        private static bool IsMarkup(ReadOnlySpan<byte> head)
        {
            // Decode the window as the text it claims to be. UTF-16 markup is as renderable as
            // UTF-8, so a BOM that names it is honoured; without one the bytes are read as
            // Latin-1-compatible ASCII, which is all the signatures need.
            string text;
            if (head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]))
            {
                text = Encoding.Unicode.GetString(head[2..]);
            }
            else if (head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]))
            {
                text = Encoding.BigEndianUnicode.GetString(head[2..]);
            }
            else
            {
                ReadOnlySpan<byte> body = head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? head[3..] : head;
                text = Encoding.Latin1.GetString(body);
            }

            ReadOnlySpan<char> content = text.AsSpan().TrimStart(" \t\r\n\f");

            // HTML: a recognised opening tag (or a comment) at the start, after whitespace.
            foreach (string tag in HtmlTags)
            {
                if (content.StartsWith(tag, StringComparison.OrdinalIgnoreCase)
                    && (tag == HtmlComment || content.Length == tag.Length || content[tag.Length] is ' ' or '\t' or '\r' or '\n' or '\f' or '/' or '>'))
                {
                    return true;
                }
            }

            // SVG: a leading <svg, or an XML prolog or doctype with <svg anywhere in the sniffed
            // window (a prolog may be followed by comments, a doctype and processing instructions).
            return content.StartsWith("<svg", StringComparison.OrdinalIgnoreCase)
                || ((content.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase)
                        || content.StartsWith("<!doctype", StringComparison.OrdinalIgnoreCase))
                    && content.Contains("<svg", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Whether every character is an HTTP token character (RFC 9110 <c>tchar</c>).</summary>
        /// <param name="value">The characters.</param>
        /// <returns>True when non-empty and all are token characters.</returns>
        private static bool IsToken(ReadOnlySpan<char> value)
        {
            if (value.IsEmpty)
            {
                return false;
            }

            foreach (char c in value)
            {
                bool token = c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9')
                    or '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
                if (!token)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
