// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Per-response additions to the content-security policy's <c>form-action</c> directive.
    ///     <para>
    ///         A browser checks <c>form-action</c> against every hop of the navigation a form
    ///         submission produces, not just the immediate action. A page whose form posts back
    ///         here and is answered with a redirect to another origin therefore needs that origin
    ///         named in its own policy, or the browser discards the redirect and leaves the user
    ///         on the page that submitted — with no visible error and nothing wrong on the server.
    ///     </para>
    ///     <para>
    ///         Only the values a page supplies here are added, and the sole caller supplies a
    ///         client's registered callbacks, so the policy widens by exactly the destinations the
    ///         request was always going to be allowed to reach.
    ///     </para>
    /// </summary>
    public static class CspFormAction
    {
        /// <summary>Where the sources ride from the page that knows them to the header writer.</summary>
        private const string ItemKey = "Tellma.Identity.CspFormAction";

        /// <summary>
        ///     Characters that would end a source expression or the directive itself. A registered
        ///     URI containing one could rewrite the policy around it, so such a value is dropped
        ///     rather than emitted.
        /// </summary>
        private static readonly System.Buffers.SearchValues<char> Unsafe =
            System.Buffers.SearchValues.Create(" \t\r\n;,'\"");

        /// <summary>Names additional form-submission destinations this response's policy must allow.</summary>
        /// <param name="context">The request context.</param>
        /// <param name="uris">Absolute URIs, typically a client's registered redirect URIs.</param>
        public static void Allow(HttpContext context, IEnumerable<string> uris)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(uris);

            List<string> sources = [];
            foreach (string uri in uris)
            {
                if (ToSource(uri) is { } source && !sources.Contains(source, StringComparer.Ordinal))
                {
                    sources.Add(source);
                }
            }

            if (sources.Count > 0)
            {
                context.Items[ItemKey] = sources;
            }
        }

        /// <summary>Reads back what <see cref="Allow" /> recorded, if anything.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The extra sources, or null when the page named none.</returns>
        internal static IReadOnlyList<string>? Allowed(HttpContext context)
        {
            return context.Items.TryGetValue(ItemKey, out object? value)
                ? value as IReadOnlyList<string>
                : null;
        }

        /// <summary>
        ///     Converts a registered URI to a CSP source expression, or null when it cannot safely
        ///     become one.
        /// </summary>
        /// <param name="uri">The registered URI.</param>
        /// <returns>The source expression, or null.</returns>
        private static string? ToSource(string uri)
        {
            // An http(s) URI is emitted whole, so the match stays as narrow as the registration
            // (a source path that does not end in '/' must match exactly). Any other scheme — a
            // native app's private scheme, which hands off to the operating system rather than to
            // a page — becomes a bare scheme source, because CSP's host-source grammar cannot
            // describe an opaque URI.
            return string.IsNullOrWhiteSpace(uri)
                || uri.AsSpan().ContainsAny(Unsafe)
                || !Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsed)
                ? null
                : parsed.Scheme is "http" or "https" ? uri : parsed.Scheme + ":";
        }
    }
}
