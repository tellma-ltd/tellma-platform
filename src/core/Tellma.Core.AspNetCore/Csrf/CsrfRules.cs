// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Tellma.Core.AspNetCore.Csrf
{
    /// <summary>How much of the CSRF control an endpoint is subject to.</summary>
    internal enum CsrfScope
    {
        /// <summary>No session cookie authenticates it: bearer surfaces, anonymous endpoints, the identity engine.</summary>
        Exempt,

        /// <summary>
        ///     Only the browser's own provenance headers, when present: requests a browser cannot
        ///     decorate, the hub's negotiate and every safe method.
        /// </summary>
        Provenance,

        /// <summary>Every rule: a cookie-authenticated request that may change data.</summary>
        Full,
    }

    /// <summary>A refusal of the CSRF control.</summary>
    /// <param name="Rule">The rule the request broke, as the metric and the problem name it.</param>
    /// <param name="Status">The status the refusal answers with.</param>
    internal sealed record CsrfRefusal(string Rule, int Status);

    /// <summary>
    ///     The CSRF control's four rules over one request, applied in order: the fetch metadata, the
    ///     origin, the client header and the body's media type. The first broken rule refuses.
    /// </summary>
    internal static class CsrfRules
    {
        /// <summary>The rule on <c>Sec-Fetch-Site</c>.</summary>
        public const string SecFetchSite = "sec_fetch_site";

        /// <summary>The rule on <c>Origin</c>, or <c>Referer</c> without it.</summary>
        public const string Origin = "origin";

        /// <summary>The rule on the <c>Tellma-Client</c> header.</summary>
        public const string Header = "header";

        /// <summary>The rule on a body's <c>Content-Type</c>.</summary>
        public const string ContentType = "content_type";

        /// <summary>The longest client version accepted, which keeps the logged value bounded.</summary>
        public const int MaxVersionLength = 64;

        /// <summary>The fetch metadata header naming the relation between the request's initiator and its target.</summary>
        private const string SecFetchSiteHeader = "Sec-Fetch-Site";

        /// <summary>The only media type a body may declare, unless the endpoint accepts binary bodies.</summary>
        private const string JsonMediaType = "application/json";

        /// <summary>Applies the rules a scope requires.</summary>
        /// <param name="request">The request.</param>
        /// <param name="scope">The endpoint's scope; <see cref="CsrfScope.Exempt" /> passes everything.</param>
        /// <param name="acceptsBinary">Whether the endpoint accepts a body of any declared media type.</param>
        /// <param name="publicOrigin">The distribution's public origin; null refuses every provenance header.</param>
        /// <param name="clientNames">The client names the header may carry.</param>
        /// <returns>The refusal, or null when the request passes.</returns>
        public static CsrfRefusal? Evaluate(
            HttpRequest request, CsrfScope scope, bool acceptsBinary, string? publicOrigin, IReadOnlySet<string> clientNames)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(clientNames);
            if (scope == CsrfScope.Exempt)
            {
                return null;
            }

            // --- Rule 1: the browser says where the request came from ---
            if (request.Headers.TryGetValue(SecFetchSiteHeader, out StringValues site)
                && !(site.Count == 1 && site[0] is "same-origin" or "none"))
            {
                return new CsrfRefusal(SecFetchSite, StatusCodes.Status403Forbidden);
            }

            // --- Rule 2: the origin, when stated, is the distribution's own ---
            string? stated = request.Headers.ContainsKey(HeaderNames.Origin)
                ? Single(request.Headers.Origin) ?? string.Empty
                : request.Headers.ContainsKey(HeaderNames.Referer) ? Single(request.Headers.Referer) ?? string.Empty : null;
            if (stated is not null && !SameOrigin(stated, publicOrigin))
            {
                return new CsrfRefusal(Origin, StatusCodes.Status403Forbidden);
            }

            if (scope == CsrfScope.Provenance)
            {
                return null;
            }

            // --- Rule 3: the custom header, which no cross-origin page can send without a preflight ---
            if (ClientName(request.Headers[TellmaAuthentication.CsrfHeaderName], clientNames) is null)
            {
                return new CsrfRefusal(Header, StatusCodes.Status403Forbidden);
            }

            // --- Rule 4: a body is JSON, or any declared type where the endpoint accepts binary ---
            return HasBody(request) && !AcceptableMediaType(request.ContentType, acceptsBinary)
                ? new CsrfRefusal(ContentType, StatusCodes.Status415UnsupportedMediaType)
                : null;
        }

        /// <summary>
        ///     The client name a <c>Tellma-Client</c> header carries: exactly one value of the form
        ///     <c>&lt;name&gt;/&lt;version&gt;</c>, the name among the configured ones and the version
        ///     non-empty, printable and bounded.
        /// </summary>
        /// <param name="header">The header's values.</param>
        /// <param name="clientNames">The configured client names.</param>
        /// <returns>The name, or null when the header is absent or malformed.</returns>
        public static string? ClientName(StringValues header, IReadOnlySet<string> clientNames)
        {
            ArgumentNullException.ThrowIfNull(clientNames);
            if (header.Count != 1 || header[0] is not string value)
            {
                return null;
            }

            int slash = value.IndexOf('/', StringComparison.Ordinal);
            if (slash <= 0)
            {
                return null;
            }

            string name = value[..slash];
            ReadOnlySpan<char> version = value.AsSpan(slash + 1);
            return clientNames.Contains(name) && version.Length is > 0 and <= MaxVersionLength && IsVisibleAscii(version)
                ? name
                : null;
        }

        /// <summary>Whether a stated origin, or a referrer URL, has the public origin's scheme, host and port.</summary>
        /// <param name="stated">The <c>Origin</c> value or <c>Referer</c> URL.</param>
        /// <param name="publicOrigin">The public origin.</param>
        /// <returns>True when they match; false for <c>null</c>, a malformed value or no configured origin.</returns>
        internal static bool SameOrigin(string stated, string? publicOrigin)
        {
            // Compared as parsed URIs, so the host's case and a default port written out do not differ.
            return publicOrigin is not null
                && Uri.TryCreate(publicOrigin, UriKind.Absolute, out Uri? expected)
                && stated.Contains("://", StringComparison.Ordinal)
                && Uri.TryCreate(stated, UriKind.Absolute, out Uri? actual)
                && actual.UserInfo.Length == 0
                && string.Equals(
                    actual.GetLeftPart(UriPartial.Authority),
                    expected.GetLeftPart(UriPartial.Authority),
                    StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a request carries a body.</summary>
        /// <param name="request">The request.</param>
        /// <returns>True for a positive length, or no length on a request whose framing allows a body.</returns>
        private static bool HasBody(HttpRequest request)
        {
            return request.ContentLength is { } length
                ? length > 0
                : request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody ?? true;
        }

        /// <summary>Whether a body's declared media type is acceptable.</summary>
        /// <param name="contentType">The <c>Content-Type</c> header.</param>
        /// <param name="acceptsBinary">Whether the endpoint accepts any declared type.</param>
        /// <returns>True when it is.</returns>
        private static bool AcceptableMediaType(string? contentType, bool acceptsBinary)
        {
            // A body always declares its type; parameters such as charset are ignored.
            return MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? parsed)
                && parsed.MediaType.HasValue
                && (acceptsBinary || string.Equals(parsed.MediaType.Value, JsonMediaType, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>A header's one value.</summary>
        /// <param name="values">The header's values.</param>
        /// <returns>The value, or null when there are none or several.</returns>
        private static string? Single(StringValues values)
        {
            return values.Count == 1 ? values[0] : null;
        }

        /// <summary>Whether text is visible ASCII: no space, no control character.</summary>
        /// <param name="text">The text.</param>
        /// <returns>True when it is.</returns>
        private static bool IsVisibleAscii(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
            {
                if (c is < '!' or > '~')
                {
                    return false;
                }
            }

            return true;
        }
    }
}
