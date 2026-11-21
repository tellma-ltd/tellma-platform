// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.Primitives;

namespace Tellma.Core.AspNetCore.Limits
{
    // Spec 0016 §8.5: output caching for the tenant-independent public documents alone, never under
    // /{tenantId}. Each document is built from configuration and the route, never from the caller,
    // the query string or the Host header, so one cached copy serves every caller.

    /// <summary>
    ///     The output-cache policy of a public document: a <c>GET</c> or <c>HEAD</c> answered from one
    ///     copy for a fixed time, whoever asks, keyed by its path alone.
    /// </summary>
    /// <param name="duration">How long a copy is served.</param>
    internal sealed class PublicDocumentCachePolicy(TimeSpan duration) : IOutputCachePolicy
    {
        /// <summary>The information document's policy: a minute, its <c>max-age</c>.</summary>
        public static readonly PublicDocumentCachePolicy OneMinute = new(TimeSpan.FromMinutes(1));

        /// <summary>The protected-resource metadata's and the OpenAPI document's policy: five minutes.</summary>
        public static readonly PublicDocumentCachePolicy FiveMinutes = new(TimeSpan.FromMinutes(5));

        /// <summary>How long a copy is served.</summary>
        public TimeSpan Duration { get; } = duration;

        /// <inheritdoc />
        public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            ArgumentNullException.ThrowIfNull(context);
            HttpRequest request = context.HttpContext.Request;
            bool readable = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method);

            // --- Whoever asks: the document names no caller, so a signed-in caller shares the copy ---
            context.EnableOutputCaching = true;
            context.AllowCacheLookup = readable;
            context.AllowCacheStorage = readable;
            context.AllowLocking = true;
            context.ResponseExpirationTimeSpan = Duration;

            // --- Keyed by the path alone: neither the Host header nor a query string varies a copy ---
            context.CacheVaryByRules.VaryByHost = false;
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            return ValueTask.CompletedTask;
        }

        /// <inheritdoc />
        public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellation)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Only a whole document is stored, and never one that sets a cookie for its caller ---
            HttpResponse response = context.HttpContext.Response;
            if (response.StatusCode != StatusCodes.Status200OK || !StringValues.IsNullOrEmpty(response.Headers.SetCookie))
            {
                context.AllowCacheStorage = false;
            }

            return ValueTask.CompletedTask;
        }
    }
}
