// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Xml;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     The vocabulary and the reading rules of an uploaded package's XML: the namespaces of the
    ///     transitional and strict dialects, the content and relationship types the reader acts on,
    ///     the reader settings every part is parsed with, and the string items shared strings and
    ///     inline strings hold.
    /// </summary>
    /// <remarks>
    ///     Every part is parsed with document types prohibited and no resolver, so nothing outside the
    ///     package is ever fetched and no entity expands. Elements are matched by local name within
    ///     the part's own namespace, transitional or strict; extension elements of other namespaces
    ///     are skipped.
    /// </remarks>
    internal static class XlsxXml
    {
        /// <summary>The longest text one string item or cell value may join to, in characters.</summary>
        public const int MaxTextLength = 1_048_576;

        /// <summary>The content types namespace, shared by both dialects.</summary>
        public const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        /// <summary>The package relationships namespace, shared by both dialects.</summary>
        public const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>The spreadsheet namespace of the transitional dialect.</summary>
        public const string SpreadsheetTransitional = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

        /// <summary>The spreadsheet namespace of the strict dialect.</summary>
        public const string SpreadsheetStrict = "http://purl.oclc.org/ooxml/spreadsheetml/main";

        /// <summary>The namespace of relationship-id attributes in the transitional dialect.</summary>
        public const string RelationshipIdTransitional = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        /// <summary>The namespace of relationship-id attributes in the strict dialect.</summary>
        public const string RelationshipIdStrict = "http://purl.oclc.org/ooxml/officeDocument/relationships";

        /// <summary>The relationship type of the package's main document, by its last segment.</summary>
        public const string OfficeDocument = "officeDocument";

        /// <summary>The relationship type of a worksheet, by its last segment.</summary>
        public const string Worksheet = "worksheet";

        /// <summary>The relationship type of the shared-string table, by its last segment.</summary>
        public const string SharedStrings = "sharedStrings";

        /// <summary>The relationship type of a link to another workbook, by its last segment.</summary>
        public const string ExternalLink = "externalLink";

        /// <summary>The content type of a link to another workbook.</summary>
        public const string ExternalLinkContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.externalLink+xml";

        /// <summary>The content types of a workbook's main part: a workbook, a template, and their macro-enabled forms.</summary>
        public static readonly IReadOnlySet<string> WorkbookContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.template.main+xml",
            "application/vnd.ms-excel.sheet.macroEnabled.main+xml",
            "application/vnd.ms-excel.template.macroEnabled.main+xml",
        };

        /// <summary>The prefixes a relationship type carries before its last segment: transitional, strict, and Microsoft's.</summary>
        private static readonly string[] RelationshipTypePrefixes =
        [
            "http://schemas.openxmlformats.org/officeDocument/2006/relationships/",
            "http://purl.oclc.org/ooxml/officeDocument/relationships/",
            "http://schemas.microsoft.com/office/2006/relationships/",
        ];

        /// <summary>Opens a reader over a part, with document types prohibited and no resolver.</summary>
        /// <param name="part">The part's stream; closed with the reader.</param>
        /// <returns>The reader.</returns>
        public static XmlReader Open(Stream part)
        {
            return XmlReader.Create(part, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                CloseInput = true,
            });
        }

        /// <summary>Whether the reader is on an element of a spreadsheet namespace with a local name.</summary>
        /// <param name="reader">The reader.</param>
        /// <param name="localName">The local name.</param>
        /// <returns>True when it is.</returns>
        public static bool IsSpreadsheet(XmlReader reader, string localName)
        {
            return reader.NodeType == XmlNodeType.Element
                && string.Equals(reader.LocalName, localName, StringComparison.Ordinal)
                && reader.NamespaceURI is SpreadsheetTransitional or SpreadsheetStrict;
        }

        /// <summary>A relationship type's last segment, when it carries a known prefix.</summary>
        /// <param name="type">The relationship type.</param>
        /// <returns>The segment (<c>worksheet</c>); null for a type of no known prefix.</returns>
        public static string? RelationshipKind(string? type)
        {
            if (type is null)
            {
                return null;
            }

            foreach (string prefix in RelationshipTypePrefixes)
            {
                if (type.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return type[prefix.Length..];
                }
            }

            return null;
        }

        /// <summary>The relationship id of the element the reader is on, in either dialect's namespace.</summary>
        /// <param name="reader">The reader, on an element.</param>
        /// <returns>The id; null when the element carries none.</returns>
        public static string? RelationshipId(XmlReader reader)
        {
            return reader.GetAttribute("id", RelationshipIdTransitional) ?? reader.GetAttribute("id", RelationshipIdStrict);
        }

        /// <summary>
        ///     Reads a string item: a shared string's <c>si</c> or an inline string's <c>is</c>. Its
        ///     plain text, or its rich text runs joined; phonetic runs are skipped and escapes undone.
        ///     The reader is left on the item's end, or on the item itself when it is empty.
        /// </summary>
        /// <param name="reader">The reader, on the item's start.</param>
        /// <param name="sheet">The sheet to blame for a text above the bound; null for the shared strings.</param>
        /// <returns>The text.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The text joins to more than <see cref="MaxTextLength" /> characters.</exception>
        public static string ReadStringItem(XmlReader reader, string? sheet)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (reader.IsEmptyElement)
            {
                return string.Empty;
            }

            // --- The item's t, or each run's t; a phonetic run is a reading aid and is skipped ---
            int depth = reader.Depth;
            string? single = null;
            StringBuilder? joined = null;
            bool more = reader.Read();
            while (more && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                if (IsSpreadsheet(reader, "rPh"))
                {
                    reader.Skip();
                    more = !reader.EOF;
                    continue;
                }

                if (IsSpreadsheet(reader, "t"))
                {
                    string text = ReadText(reader, sheet);
                    if (single is null && joined is null)
                    {
                        single = text;
                    }
                    else
                    {
                        joined ??= new StringBuilder(single);
                        if (joined.Length + text.Length > MaxTextLength)
                        {
                            throw ImportFaults.Malformed(ImportFaults.CellsReason, sheet);
                        }

                        _ = joined.Append(text);
                        single = null;
                    }
                }

                more = reader.Read();
            }

            return XmlStrings.Unescape(joined?.ToString() ?? single ?? string.Empty);
        }

        /// <summary>
        ///     Reads an element's text: every text, white space and character data node it holds, its
        ///     child elements skipped. The reader is left on the element's end, or on the element
        ///     itself when it is empty.
        /// </summary>
        /// <param name="reader">The reader, on the element's start.</param>
        /// <param name="sheet">The sheet to blame for a text above the bound; null for a part of no sheet.</param>
        /// <returns>The text, escapes not undone.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The text is longer than <see cref="MaxTextLength" /> characters.</exception>
        public static string ReadText(XmlReader reader, string? sheet)
        {
            ArgumentNullException.ThrowIfNull(reader);
            if (reader.IsEmptyElement)
            {
                return string.Empty;
            }

            int depth = reader.Depth;
            string? single = null;
            StringBuilder? joined = null;
            bool more = reader.Read();
            while (more && !(reader.NodeType == XmlNodeType.EndElement && reader.Depth == depth))
            {
                // --- A child element is no text of this one and is skipped whole ---
                if (reader.NodeType == XmlNodeType.Element)
                {
                    reader.Skip();
                    more = !reader.EOF;
                    continue;
                }

                // --- Text, white space and character data join, within the bound ---
                if (reader.NodeType is XmlNodeType.Text or XmlNodeType.CDATA or XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace)
                {
                    string value = reader.Value;
                    if (single is null && joined is null)
                    {
                        single = value;
                    }
                    else
                    {
                        joined ??= new StringBuilder(single);
                        _ = joined.Append(value);
                        single = null;
                    }

                    if ((single?.Length ?? joined!.Length) > MaxTextLength)
                    {
                        throw ImportFaults.Malformed(ImportFaults.CellsReason, sheet);
                    }
                }

                more = reader.Read();
            }

            return joined?.ToString() ?? single ?? string.Empty;
        }

        /// <summary>Resolves a relationship's target against the part that declares it.</summary>
        /// <param name="source">The declaring part's name, without a leading slash; empty for the package.</param>
        /// <param name="target">The relationship's target: relative to the source's folder, or absolute from the package root.</param>
        /// <returns>The target part's name, without a leading slash; null when it climbs out of the package.</returns>
        public static string? ResolveTarget(string source, string target)
        {
            ArgumentNullException.ThrowIfNull(source);
            ArgumentNullException.ThrowIfNull(target);

            // --- The base: the package root for an absolute target, else the source's folder ---
            List<string> segments = [];
            string relative = Uri.UnescapeDataString(target.Replace('\\', '/'));
            if (relative.StartsWith('/'))
            {
                relative = relative[1..];
            }
            else
            {
                int slash = source.LastIndexOf('/');
                if (slash > 0)
                {
                    segments.AddRange(source[..slash].Split('/', StringSplitOptions.RemoveEmptyEntries));
                }
            }

            // --- The target's segments, '.' and '..' resolved; climbing above the root is no part ---
            foreach (string segment in relative.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (segment == ".")
                {
                    continue;
                }

                if (segment == "..")
                {
                    if (segments.Count == 0)
                    {
                        return null;
                    }

                    segments.RemoveAt(segments.Count - 1);
                    continue;
                }

                segments.Add(segment);
            }

            return segments.Count == 0 ? null : string.Join('/', segments);
        }

        /// <summary>The name of a part's relationships part: <c>xl/_rels/workbook.xml.rels</c> for <c>xl/workbook.xml</c>.</summary>
        /// <param name="part">The part's name, without a leading slash.</param>
        /// <returns>The relationships part's name.</returns>
        public static string RelationshipsOf(string part)
        {
            ArgumentNullException.ThrowIfNull(part);
            int slash = part.LastIndexOf('/');
            return slash < 0
                ? string.Concat("_rels/", part, ".rels")
                : string.Concat(part.AsSpan(0, slash + 1), "_rels/", part.AsSpan(slash + 1), ".rels");
        }
    }
}
