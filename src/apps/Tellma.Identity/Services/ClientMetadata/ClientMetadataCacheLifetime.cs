// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net.Http.Headers;

namespace Tellma.Identity.Services.ClientMetadata
{
    /// <summary>
    ///     How long a fetched document or key set is cached: the lifetime its HTTP caching headers
    ///     give it, clamped to the configured window.
    /// </summary>
    public static class ClientMetadataCacheLifetime
    {
        /// <summary>
        ///     The freshness lifetime the response's headers give it (RFC 9111, section 4.2.1), before
        ///     clamping: zero for <c>no-store</c> or <c>no-cache</c>; else <c>max-age</c> less the
        ///     response's <c>Age</c>; else <c>Expires</c> less <c>Date</c> (or the fetch time); else
        ///     zero, since a response that states nothing has not asked to be kept.
        /// </summary>
        /// <param name="headers">The response headers.</param>
        /// <param name="contentHeaders">The content headers, which carry <c>Expires</c>.</param>
        /// <param name="fetchedAt">When the response was received.</param>
        /// <returns>The lifetime, never negative.</returns>
        public static TimeSpan FromHeaders(HttpResponseHeaders headers, HttpContentHeaders? contentHeaders, DateTimeOffset fetchedAt)
        {
            ArgumentNullException.ThrowIfNull(headers);

            CacheControlHeaderValue? cacheControl = headers.CacheControl;
            if (cacheControl is { NoStore: true } or { NoCache: true })
            {
                return TimeSpan.Zero;
            }

            if (cacheControl?.MaxAge is { } maxAge)
            {
                return NonNegative(maxAge - (headers.Age ?? TimeSpan.Zero));
            }

            if (contentHeaders?.Expires is { } expires)
            {
                DateTimeOffset date = headers.Date ?? fetchedAt;
                return NonNegative(expires - date);
            }

            return TimeSpan.Zero;
        }

        /// <summary>Clamps a lifetime to the configured window.</summary>
        /// <param name="lifetime">The lifetime the headers give.</param>
        /// <param name="minimum">The shortest a document is kept.</param>
        /// <param name="maximum">The longest a document is kept.</param>
        /// <returns>The clamped lifetime.</returns>
        public static TimeSpan Clamp(TimeSpan lifetime, TimeSpan minimum, TimeSpan maximum)
        {
            return lifetime < minimum ? minimum : lifetime > maximum ? maximum : lifetime;
        }

        /// <summary>A lifetime, or zero when it is negative.</summary>
        private static TimeSpan NonNegative(TimeSpan lifetime)
        {
            return lifetime < TimeSpan.Zero ? TimeSpan.Zero : lifetime;
        }
    }
}
