// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tellma.Identity.Options;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Keeps a request's <see cref="CarriedQuery" /> on the links that stay inside the identity
    ///     pages, so the language a client asked for and the way back to that client survive every
    ///     step between the page the user arrived on and the page they end up on.
    ///     <para>
    ///         Only the engine's own pages receive the values: the sign-in pages and the account
    ///         pages. A protocol endpoint is never given them, because a parameter added to an
    ///         authorization request changes that request; and a URL on another origin is never
    ///         given them, because they are this server's to interpret and nobody else's.
    ///     </para>
    /// </summary>
    /// <param name="options">The engine options, for the path base the pages are mounted at.</param>
    public sealed class IdentityPageLinks(IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>
        ///     The page folders of the identity area, as their paths begin after the mount point.
        ///     Every page the engine ships lives under one of them.
        /// </summary>
        private static readonly string[] PageFolders =
        [
            "/" + TellmaIdentityConstants.AreaName + "/Account",
            "/" + TellmaIdentityConstants.AreaName + "/Manage",
        ];

        /// <summary>Whether a URL addresses one of the engine's pages on this server.</summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The candidate URL.</param>
        /// <returns>True for a local URL whose path lies in one of the engine's page folders.</returns>
        public bool IsIdentityPage(PathString requestPathBase, string? url)
        {
            // Local only: a URL that names a host is someone else's page however its path reads.
            if (!ReturnUrlValidator.IsValid(url))
            {
                return false;
            }

            int end = url.AsSpan().IndexOfAny('?', '#');
            ReadOnlySpan<char> path = end < 0 ? url.AsSpan() : url.AsSpan(0, end);

            // Generated links carry the host's path base and then the engine's own, so both are
            // stepped over before the folder is compared.
            if (!TrySkip(ref path, requestPathBase.Value) || !TrySkip(ref path, options.Value.PathBase))
            {
                return false;
            }

            // A page's path is plain: names and slashes. Anything else — a dot segment, an
            // escape, a backslash — is a path a browser may resolve to somewhere other than
            // where it reads, and such a path is not given the values, whatever it starts with.
            foreach (char character in path)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '_'))
                {
                    return false;
                }
            }

            foreach (string folder in PageFolders)
            {
                // Whole segments, and without case as routing matches them: "/Identity/Accounts"
                // is not the Account folder, and "/identity/account/login" is.
                if (path.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
                    && (path.Length == folder.Length || path[folder.Length] == '/'))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Adds the request's carried values to a URL that addresses one of the engine's pages,
        ///     and returns any other URL unchanged.
        /// </summary>
        /// <param name="context">The request whose values are carried.</param>
        /// <param name="url">The URL a link, form or redirect is about to use.</param>
        /// <returns>The URL to use in its place.</returns>
        public string Carry(HttpContext context, string url)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(url);

            var carried = CarriedQuery.Read(context.Request);
            return carried.IsEmpty || !IsIdentityPage(context.Request.PathBase, url)
                ? url
                : carried.AppendTo(url);
        }

        /// <summary>Steps a path over a leading prefix, when the prefix is there to step over.</summary>
        /// <param name="path">The path, advanced past the prefix on success.</param>
        /// <param name="prefix">The prefix, or nothing when there is none to skip.</param>
        /// <returns>False when a prefix was expected and the path does not start with it.</returns>
        private static bool TrySkip(ref ReadOnlySpan<char> path, string? prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return true;
            }

            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || path.Length == prefix.Length
                || path[prefix.Length] != '/')
            {
                return false;
            }

            path = path[prefix.Length..];
            return true;
        }
    }
}
