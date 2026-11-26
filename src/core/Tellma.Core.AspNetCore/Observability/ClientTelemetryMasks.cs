// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;

namespace Tellma.Core.AspNetCore.Observability
{
    /// <summary>
    ///     The relay's backstop over a client's error message and stack: the URL and e-mail masks the
    ///     SPA applies before it sends them, applied again, so a client that skipped them still ships no
    ///     address.
    /// </summary>
    /// <remarks>
    ///     The patterns are the SPA's, run without backtracking so a crafted text of a few kilobytes
    ///     costs linear time. That engine has no lookbehind, so each "not preceded by" of the SPA's
    ///     patterns is a captured lead character, written back ahead of the mask.
    /// </remarks>
    internal static partial class ClientTelemetryMasks
    {
        /// <summary>What a URL becomes.</summary>
        public const string UrlMask = "<url>";

        /// <summary>What an e-mail address becomes.</summary>
        public const string EmailMask = "<email>";

        /// <summary>The lead character's group.</summary>
        private const string Lead = "lead";

        /// <summary>The longest a mask may run over one text before it gives up, in milliseconds.</summary>
        private const int TimeoutMilliseconds = 1000;

        /// <summary>
        ///     Masks the URLs, then the e-mail addresses, of a text.
        /// </summary>
        /// <param name="text">The text.</param>
        /// <param name="masked">The masked text; null when a mask ran out of time.</param>
        /// <returns>True when the text was masked; false when a mask ran out of time, and the text is not to be kept.</returns>
        public static bool TryMask(string text, out string? masked)
        {
            ArgumentNullException.ThrowIfNull(text);
            try
            {
                masked = Emails().Replace(Urls().Replace(text, static match => match.Groups[Lead].Value + UrlMask), EmailMask);
                return true;
            }
            catch (RegexMatchTimeoutException)
            {
                masked = null;
                return false;
            }
        }

        /// <summary>
        ///     A URL: one with a scheme, one that starts <c>www.</c>, or a path with a query or a
        ///     fragment. A scheme or <c>www.</c> starts a word; a path's slash follows no word character,
        ///     slash or dot.
        /// </summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(
            @"(?:^|(?<lead>[^A-Za-z0-9_]))(?:[a-z][a-z0-9+.-]*://[^\s'""<>]+|www\.[^\s'""<>]+)|(?:^|(?<lead>[^A-Za-z0-9_/.]))/[^\s'""<>?#]*[?#][^\s'""<>]*",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeoutMilliseconds)]
        internal static partial Regex Urls();

        /// <summary>
        ///     An e-mail address: a local part, <c>@</c>, and a domain with a dot, none of them holding
        ///     white space, another <c>@</c>, angle or curly quotes, guillemets, brackets, parentheses,
        ///     commas or semicolons.
        /// </summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(
            @"[^\s@<>'""\u201C\u201D\u00AB\u00BB(),;[\]]+@[^\s@<>'""\u201C\u201D\u00AB\u00BB(),;[\]]+\.[^\s@<>'""\u201C\u201D\u00AB\u00BB(),;[\].]+",
            RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
            TimeoutMilliseconds)]
        internal static partial Regex Emails();
    }
}
