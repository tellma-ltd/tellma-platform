// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     A document client's redirect URIs: which are loopback ones, how each is stored on the
    ///     client's row, and how a requested redirect URI is matched against them.
    /// </summary>
    /// <remarks>
    ///     Matching is exact, with one relaxation for native clients (RFC 8252, section 7.3): a loopback
    ///     redirect matches on any port. The row stores a loopback redirect without its port and
    ///     is a native application, so the engine's own redirect check applies the same
    ///     relaxation the document promises.
    /// </remarks>
    public static class DocumentClientRedirects
    {
        /// <summary>
        ///     Whether a redirect URI is a loopback one: an <c>http</c> or <c>https</c> URI whose
        ///     host is a loopback IP address or <c>localhost</c>.
        /// </summary>
        /// <param name="uri">An absolute redirect URI.</param>
        /// <returns>True for a loopback redirect.</returns>
        public static bool IsLoopback(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            if (uri.Scheme is not ("http" or "https"))
            {
                return false;
            }

            // Uri.Host keeps an IPv6 literal's brackets; the address parser wants them gone.
            string host = uri.Host.Trim('[', ']');
            return string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
                || (IPAddress.TryParse(host, out IPAddress? address) && IPAddress.IsLoopback(address));
        }

        /// <summary>
        ///     The redirect URI as the row stores it: as written in the document, except that a
        ///     loopback redirect loses its port, so any port matches it.
        /// </summary>
        /// <param name="uri">A validated redirect URI from the document.</param>
        /// <returns>The stored form.</returns>
        public static string ToStored(Uri uri)
        {
            ArgumentNullException.ThrowIfNull(uri);

            return IsLoopback(uri) && !uri.IsDefaultPort
                ? uri.GetComponents(UriComponents.Scheme | UriComponents.Host | UriComponents.PathAndQuery, UriFormat.UriEscaped)
                : uri.OriginalString;
        }

        /// <summary>Whether a requested redirect URI matches one the document lists.</summary>
        /// <param name="documented">The document's redirect URIs.</param>
        /// <param name="requested">The redirect URI a request names.</param>
        /// <returns>True for an exact match, or a loopback match on any port.</returns>
        public static bool Matches(IEnumerable<Uri> documented, string requested)
        {
            ArgumentNullException.ThrowIfNull(documented);
            ArgumentNullException.ThrowIfNull(requested);

            foreach (Uri candidate in documented)
            {
                if (string.Equals(candidate.OriginalString, requested, StringComparison.Ordinal)
                    || string.Equals(ToStored(candidate), requested, StringComparison.Ordinal))
                {
                    return true;
                }

                if (IsLoopback(candidate)
                    && Uri.TryCreate(requested, UriKind.Absolute, out Uri? parsed)
                    && IsLoopback(parsed)
                    && string.Equals(candidate.Scheme, parsed.Scheme, StringComparison.Ordinal)
                    && string.Equals(candidate.Host, parsed.Host, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(candidate.UserInfo, parsed.UserInfo, StringComparison.Ordinal)
                    && string.Equals(candidate.AbsolutePath, parsed.AbsolutePath, StringComparison.Ordinal)
                    && string.Equals(candidate.Query, parsed.Query, StringComparison.Ordinal)
                    && string.IsNullOrEmpty(parsed.Fragment))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
