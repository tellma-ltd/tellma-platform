// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     Recognizes a document client's <c>client_id</c> — the <c>https</c> URL of its own
    ///     metadata document — and decides whether such an id may be fetched at all.
    /// </summary>
    /// <remarks>
    ///     Recognition and acceptance are separate on purpose. Anything that parses as an absolute
    ///     <c>https</c> URL with a host and no fragment is a document client and is never looked up
    ///     as a registered client, so a registration can never answer for a URL. Whether the URL is
    ///     acceptable is a second question, and a refusal there is <c>invalid_client</c>.
    /// </remarks>
    public static class ClientIdUrl
    {
        /// <summary>
        ///     The longest client id accepted: the width of the applications table's
        ///     <c>ClientId</c> column, where the document client's row is stored under it.
        /// </summary>
        public const int MaxLength = 100;

        /// <summary>Whether a client id names a client ID metadata document.</summary>
        /// <param name="clientId">The client id as presented.</param>
        /// <returns>True for an absolute <c>https</c> URL with a host and no fragment.</returns>
        public static bool IsDocumentClientId([NotNullWhen(true)] string? clientId)
        {
            return TryRecognize(clientId, out _);
        }

        /// <summary>Recognizes a document client's id and parses it.</summary>
        /// <param name="clientId">The client id as presented.</param>
        /// <param name="url">The parsed URL, whose original string is the client id exactly.</param>
        /// <returns>True for an absolute <c>https</c> URL with a host and no fragment.</returns>
        public static bool TryRecognize([NotNullWhen(true)] string? clientId, [NotNullWhen(true)] out Uri? url)
        {
            url = null;

            // A fragment is looked for in the raw string: parsing drops an empty one ("…#").
            if (string.IsNullOrEmpty(clientId)
                || clientId.Contains('#', StringComparison.Ordinal)
                || !Uri.TryCreate(clientId, UriKind.Absolute, out Uri? parsed)
                || !string.Equals(parsed.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
                || string.IsNullOrEmpty(parsed.Host))
            {
                return false;
            }

            url = parsed;
            return true;
        }

        /// <summary>
        ///     Why a recognized client id may not be fetched, or null when it may. The id must be
        ///     short enough to be stored, plain ASCII, already in the canonical form a URL parser
        ///     writes it in (so two spellings of one address can never be two clients), free of
        ///     user information and dot segments, and named by host: an IP literal is never
        ///     fetched.
        /// </summary>
        /// <param name="url">A recognized client id.</param>
        /// <returns>The <see cref="ClientMetadataRejectionReasons" /> value, or null.</returns>
        public static string? FindProblem(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);

            string clientId = url.OriginalString;
            if (clientId.Length > MaxLength)
            {
                return ClientMetadataRejectionReasons.ClientIdTooLong;
            }

            // An IP literal is the fetch guard's to refuse, under its own reason.
            if (url.HostNameType is UriHostNameType.IPv4 or UriHostNameType.IPv6)
            {
                return ClientMetadataRejectionReasons.Address;
            }

            // The canonical form lowercases the scheme and host, drops a default port, adds the
            // root path and removes dot segments. A client id in any other spelling would let two
            // ids name one document — and, under the store's case-insensitive collation, one row.
            return !clientId.All(char.IsAscii)
                || !string.Equals(clientId, url.AbsoluteUri, StringComparison.Ordinal)
                || !string.IsNullOrEmpty(url.UserInfo)
                || HasDotSegment(url.AbsolutePath)
                    ? ClientMetadataRejectionReasons.ClientId
                    : null;
        }

        /// <summary>The host a client id's document is fetched from, as the consent page shows it.</summary>
        /// <param name="url">A recognized client id.</param>
        /// <returns>The host in ASCII form, with its port when it is not the default.</returns>
        public static string DisplayHost(Uri url)
        {
            ArgumentNullException.ThrowIfNull(url);

            return url.IsDefaultPort
                ? url.IdnHost
                : url.IdnHost + ":" + url.Port.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>
        ///     Whether a path holds a <c>.</c> or <c>..</c> segment, also when percent-encoded: the
        ///     parser removes the plain ones but leaves encoded ones for the server to interpret.
        /// </summary>
        private static bool HasDotSegment(string path)
        {
            foreach (string segment in path.Split('/'))
            {
                string decoded = Uri.UnescapeDataString(segment);
                if (decoded is "." or "..")
                {
                    return true;
                }
            }

            return false;
        }
    }
}
