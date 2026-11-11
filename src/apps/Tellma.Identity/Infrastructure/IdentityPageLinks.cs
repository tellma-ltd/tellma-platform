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
    ///     <para>
    ///         It also puts a language the user chose in place of the one a page's address states,
    ///         there and in the address the page returns to once the user has signed in.
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

        /// <summary>
        ///     The query name under which a sign-in page holds the address to return to once the
        ///     user has signed in. The pages bind it without case, and the cookie challenge
        ///     writes it capitalized.
        /// </summary>
        private const string ReturnAddressName = "returnUrl";

        /// <summary>
        ///     How many return addresses, one inside the other, a chosen language is followed
        ///     into. One is the ordinary case; a page that itself holds a return address and is
        ///     then challenged makes two.
        /// </summary>
        private const int MaxReturnAddressDepth = 3;

        /// <summary>Whether a URL addresses one of the engine's pages on this server.</summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The candidate URL.</param>
        /// <returns>True for a local URL whose path lies in one of the engine's page folders.</returns>
        public bool IsIdentityPage(PathString requestPathBase, string? url)
        {
            if (!TryGetEnginePath(requestPathBase, url, out ReadOnlySpan<char> path))
            {
                return false;
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

        /// <summary>Whether a URL addresses the account entry on this server.</summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The candidate URL.</param>
        /// <returns>True for a local URL whose path is the account entry's.</returns>
        public bool IsAccountEntry(PathString requestPathBase, string? url)
        {
            if (!TryGetEnginePath(requestPathBase, url, out ReadOnlySpan<char> path))
            {
                return false;
            }

            // Without case and with or without a closing slash, as routing matches it.
            if (path.Length > 1 && path[^1] == '/')
            {
                path = path[..^1];
            }

            return path.Length == AccountSections.EntryRoute.Length + 1
                && path[0] == '/'
                && path[1..].Equals(AccountSections.EntryRoute, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        ///     Puts a language the user chose in place of the language a local URL states, and in
        ///     place of the one stated by the address that URL returns to after sign-in.
        ///     <para>
        ///         A sign-in page reached through a challenge holds the challenged address whole,
        ///         with the language that address was opened in. Replacing only the page's own
        ///         value would show the chosen language until the user signed in and then return
        ///         them to the language they chose to leave. The return address is rewritten only
        ///         when a sign-in or account page holds it and it is itself one of those pages or
        ///         the account entry: a pending authorization request is never altered from here.
        ///     </para>
        /// </summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The local URL the user is being returned to.</param>
        /// <param name="uiLocales">The chosen language.</param>
        /// <returns>The URL stating the chosen language wherever it stated one.</returns>
        public string WithLanguage(PathString requestPathBase, string url, string uiLocales)
        {
            ArgumentNullException.ThrowIfNull(url);
            ArgumentException.ThrowIfNullOrWhiteSpace(uiLocales);

            return WithLanguage(requestPathBase, url, uiLocales, depth: 0);
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

        /// <summary>Replaces the stated language of a URL and of the return addresses nested in it.</summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The URL.</param>
        /// <param name="uiLocales">The chosen language.</param>
        /// <param name="depth">How many return addresses deep this URL already is.</param>
        /// <returns>The URL stating the chosen language wherever it stated one.</returns>
        private string WithLanguage(PathString requestPathBase, string url, string uiLocales, int depth)
        {
            string replaced = CarriedQuery.WithUiLocales(url, uiLocales);

            // Only the engine's pages hold a return address under this name. On any other URL a
            // value so named is that URL's own business, and it is left as written. Of the
            // addresses a page may return to, only the engine's pages and the account entry
            // are followed into.
            return depth == MaxReturnAddressDepth || !IsIdentityPage(requestPathBase, url)
                ? replaced
                : CarriedQuery.WithValue(
                    replaced,
                    ReturnAddressName,
                    nested => IsIdentityPage(requestPathBase, nested) || IsAccountEntry(requestPathBase, nested)
                        ? WithLanguage(requestPathBase, nested, uiLocales, depth + 1)
                        : nested);
        }

        /// <summary>
        ///     Reads the path of a local URL as it lies under the engine's mount point: after the
        ///     host's path base and the engine's own, and only when it is a plain path.
        /// </summary>
        /// <param name="requestPathBase">The path base the host itself is served under.</param>
        /// <param name="url">The candidate URL.</param>
        /// <param name="path">The path under the mount point, starting with its slash.</param>
        /// <returns>False for a URL that is not local, is mounted elsewhere, or is not plain.</returns>
        private bool TryGetEnginePath(PathString requestPathBase, string? url, out ReadOnlySpan<char> path)
        {
            path = default;

            // Local only: a URL that names a host is someone else's page however its path reads.
            if (!ReturnUrlValidator.IsValid(url))
            {
                return false;
            }

            int end = url.AsSpan().IndexOfAny('?', '#');
            ReadOnlySpan<char> candidate = end < 0 ? url.AsSpan() : url.AsSpan(0, end);

            // Generated links carry the host's path base and then the engine's own, so both are
            // stepped over before the path is compared.
            if (!TrySkip(ref candidate, requestPathBase.Value) || !TrySkip(ref candidate, options.Value.PathBase))
            {
                return false;
            }

            // A page's path is plain: names and slashes. Anything else — a dot segment, an
            // escape, a backslash — is a path a browser may resolve to somewhere other than
            // where it reads, and such a path is not one of the engine's, whatever it starts with.
            foreach (char character in candidate)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('/' or '-' or '_'))
                {
                    return false;
                }
            }

            path = candidate;
            return true;
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
