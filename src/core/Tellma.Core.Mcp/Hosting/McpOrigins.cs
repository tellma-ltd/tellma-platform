// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Tellma.Core.Mcp.Hosting
{
    /// <summary>
    ///     The browser-origin rule of the tenant MCP endpoint: a request without an <c>Origin</c> (a
    ///     native client) passes, one from a listed origin passes, and every other is refused, which
    ///     stops a page on another site from driving a user's local client against the server.
    /// </summary>
    internal static class McpOrigins
    {
        /// <summary>Whether a value is an origin: an http or https scheme, a host and an optional port, with no path, query or fragment.</summary>
        /// <param name="value">The value.</param>
        /// <returns>True when it is an origin as a browser sends one.</returns>
        public static bool IsOrigin(string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            {
                return false;
            }

            // The authority alone, as the browser serializes it; a trailing slash or a path is not an origin.
            return (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
                && !string.IsNullOrEmpty(uri.Host)
                && string.IsNullOrEmpty(uri.UserInfo)
                && string.Equals(uri.GetLeftPart(UriPartial.Authority), value, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a request's origin is admitted.</summary>
        /// <param name="request">The request.</param>
        /// <param name="allowed">The allowed origins, compared ignoring case.</param>
        /// <returns>True when the request carries no <c>Origin</c>, or exactly one that is listed.</returns>
        public static bool IsAllowed(HttpRequest request, IReadOnlySet<string> allowed)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(allowed);
            StringValues origins = request.Headers[HeaderNames.Origin];
            if (origins.Count == 0)
            {
                return true;
            }

            // Several values, or an opaque "null" origin, name no origin the list could hold.
            return origins.Count == 1
                && origins[0] is string origin
                && allowed.Any(entry => string.Equals(entry, origin, StringComparison.OrdinalIgnoreCase));
        }
    }
}
