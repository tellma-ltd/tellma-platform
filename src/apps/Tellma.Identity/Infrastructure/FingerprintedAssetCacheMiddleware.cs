// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Tellma.Identity.Infrastructure
{
    /// <summary>
    ///     Serves the engine's own static assets with a long immutable cache when — and only when —
    ///     the request carries the content fingerprint the markup stamped on it.
    ///     <para>
    ///         The static-asset endpoint answers <c>no-cache</c> by default, which is correct for a
    ///         bare path: the URL says nothing about the bytes, so the browser must revalidate.
    ///         That revalidation is what makes the brand font arrive late on every reload. A URL
    ///         carrying <c>?v=&lt;hash&gt;</c> is a different promise — the content hash is part of
    ///         the address, so those bytes can never change under it, and the response is safe to
    ///         keep for a year. Editing an asset changes its hash, which changes the URL the page
    ///         asks for, so nothing stale is ever reused.
    ///     </para>
    /// </summary>
    /// <param name="next">The next middleware.</param>
    public sealed class FingerprintedAssetCacheMiddleware(RequestDelegate next)
    {
        /// <summary>
        ///     The engine's static asset root, as served by the razor class library. No trailing
        ///     slash: <see cref="PathString.StartsWithSegments(PathString, StringComparison)" />
        ///     matches whole segments, and a trailing slash makes it match nothing.
        /// </summary>
        private const string AssetPathPrefix = "/_content/Tellma.Identity";

        /// <summary>The query key the version tag helper stamps the content hash into.</summary>
        private const string VersionQueryKey = "v";

        /// <summary>One year, the maximum a well-behaved cache should be asked to hold anything.</summary>
        private const int ImmutableSeconds = 31_536_000;

        /// <summary>Applies the cache header to fingerprinted asset responses.</summary>
        /// <param name="context">The request being handled.</param>
        /// <returns>A task that completes when the pipeline has run.</returns>
        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (context.Request.Path.StartsWithSegments(AssetPathPrefix, StringComparison.Ordinal)
                && context.Request.Query.ContainsKey(VersionQueryKey))
            {
                // Set it as the response starts: the asset endpoint writes its own Cache-Control
                // while handling the request, so anything set before that would be overwritten.
                context.Response.OnStarting(static state =>
                {
                    var current = (HttpContext)state;
                    if (current.Response.StatusCode is StatusCodes.Status200OK or StatusCodes.Status304NotModified)
                    {
                        current.Response.Headers[HeaderNames.CacheControl] =
                            $"public, max-age={ImmutableSeconds}, immutable";
                    }

                    return Task.CompletedTask;
                }, context);
            }

            return next(context);
        }
    }
}
