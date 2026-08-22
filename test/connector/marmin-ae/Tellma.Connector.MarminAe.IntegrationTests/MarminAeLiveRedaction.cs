// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Frozen;
using System.Text;
using System.Text.Json;

namespace Tellma.Connector.MarminAe.IntegrationTests
{
    /// <summary>Takes the account's own identity out of a body before it is published.</summary>
    /// <remarks>
    ///     <para>
    ///         The live suite writes what it sends and receives into a build artifact of a public
    ///         repository, and the bodies it receives describe a real, registered taxpayer: its tax
    ///         registration numbers, its trade licence, its network address, its postal address and
    ///         its mailbox. None of that belongs in a build log, and a bearer token least of all.
    ///     </para>
    ///     <para>
    ///         Values are substituted rather than the document being rewritten, so everything the
    ///         vendor chose — field order, spacing, number formatting, nulls — survives byte for
    ///         byte and the artifact stays usable as a parser input. It also means an identifier
    ///         embedded in an escaped string, such as a tax number inside a rendered document, is
    ///         caught by the same pass that catches the field it came from.
    ///     </para>
    ///     <para>
    ///         Substitution is blind to where the text occurs: a scrubbed value that happens to be a
    ///         substring of something innocent takes the innocent occurrence with it. That is the
    ///         intended trade — over-redaction costs legibility, under-redaction cannot be undone
    ///         once a run is published.
    ///     </para>
    /// </remarks>
    internal static class MarminAeLiveRedaction
    {
        /// <summary>What a redacted value is replaced with.</summary>
        internal const string Placeholder = "<redacted>";

        // Shorter values are skipped: they are more likely to be a currency or a country code that
        // occurs everywhere than an identifier, and replacing one would shred the body.
        private const int ShortestReplaceableValue = 4;

        // Property names whose value identifies the account rather than the document. Compared with
        // separators removed, so one entry covers document_xml, documentXml and documentXML alike.
        private static readonly FrozenSet<string> AccountIdentifiers = new[]
        {
            "additionalstreetname",
            "addressline",
            "authorityname",
            "cityname",
            "companyid",
            "countrysubentity",
            "createdby",
            "documentxml",
            "email",
            "endpointid",
            "holdername",
            "issuedby",
            "issuedto",
            "logourl",
            "name",
            "orgid",
            "partyname",
            "partynameinlocallanguage",
            "postalzone",
            "primaryaccountnumberid",
            "profileid",
            "receiverid",
            "refreshtoken",
            "requestid",
            "schemeagencyid",
            "senderid",
            "streetname",
            "telephone",
            "tin",
            "token",
            "updatedby",
        }.ToFrozenSet(StringComparer.Ordinal);

        /// <summary>Replaces the account's identifying values throughout a body.</summary>
        /// <param name="body">The body as it came off the wire.</param>
        /// <param name="known">
        ///     Values known to identify the account before the body is read — the configured
        ///     credentials — which are scrubbed even from a body that is not JSON at all.
        /// </param>
        /// <returns>The body with those values replaced.</returns>
        internal static string Scrub(string body, IEnumerable<string> known)
        {
            ArgumentNullException.ThrowIfNull(body);
            ArgumentNullException.ThrowIfNull(known);

            HashSet<string> values = new(StringComparer.Ordinal);
            foreach (string candidate in known)
            {
                Consider(values, candidate);
            }

            // A failure body may be a gateway's HTML, and a truncated one may be nothing at all;
            // neither is a reason to publish the credentials collected above.
            if (LooksLikeJson(body))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    Collect(document.RootElement, values);
                }
                catch (JsonException)
                {
                    // Unparseable, so there is nothing to walk. The known values still apply.
                }
            }

            if (values.Count == 0)
            {
                return body;
            }

            // Longest first: a value that contains a shorter one must be replaced whole, or the
            // shorter substitution would break the longer match apart.
            List<string> ordered = [.. values];
            ordered.Sort(static (left, right) => right.Length != left.Length
                ? right.Length - left.Length
                : string.CompareOrdinal(left, right));

            StringBuilder builder = new(body);
            foreach (string value in ordered)
            {
                builder.Replace(value, Placeholder);

                // The same value as the wire spells it when it needs escaping.
                string encoded = JsonEncodedText.Encode(value).ToString();
                if (!string.Equals(encoded, value, StringComparison.Ordinal))
                {
                    builder.Replace(encoded, Placeholder);
                }
            }

            return builder.ToString();
        }

        /// <summary>Whether a property's value says who the account is.</summary>
        /// <param name="propertyName">The property name as the wire spells it.</param>
        /// <returns>True when the value under it is scrubbed.</returns>
        internal static bool IsAccountIdentifier(string propertyName)
        {
            ArgumentNullException.ThrowIfNull(propertyName);

            return AccountIdentifiers.Contains(Normalize(propertyName));
        }

        private static void Collect(JsonElement element, HashSet<string> values)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Collect(item, values);
                }

                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // A postal address carries an identifier of its own, and it is the account's address.
            // Everywhere else an id names a document, which the artifact is worthless without.
            bool isAddress = element.TryGetProperty("street_name", out _)
                || element.TryGetProperty("city_name", out _);

            foreach (JsonProperty property in element.EnumerateObject())
            {
                bool sensitive = IsAccountIdentifier(property.Name)
                    || (isAddress && string.Equals(Normalize(property.Name), "id", StringComparison.Ordinal));

                if (sensitive && property.Value.ValueKind == JsonValueKind.String)
                {
                    Consider(values, property.Value.GetString());
                }

                Collect(property.Value, values);
            }
        }

        private static void Consider(HashSet<string> values, string? candidate)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && candidate.Length >= ShortestReplaceableValue)
            {
                values.Add(candidate);
            }
        }

        private static bool LooksLikeJson(string body)
        {
            ReadOnlySpan<char> trimmed = body.AsSpan().TrimStart();

            return trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
        }

        private static string Normalize(string propertyName)
        {
            return propertyName.Replace("_", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .ToLowerInvariant();
        }
    }
}
