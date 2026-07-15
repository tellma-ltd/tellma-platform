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
    ///     Applies the identity UI's security headers: a strict same-origin content-security
    ///     policy with <c>frame-ancestors 'none'</c> plus <c>X-Frame-Options: DENY</c>, so login,
    ///     consent, and passkey ceremonies can never be framed by a hostile site. In in-proc mode
    ///     the headers apply only to requests under the engine's path base; a host's existing
    ///     headers are never overwritten.
    /// </summary>
    /// <param name="next">The next middleware.</param>
    /// <param name="options">The engine options (path base scoping).</param>
    public sealed class SecurityHeadersMiddleware(RequestDelegate next, IOptions<TellmaIdentityOptions> options)
    {
        /// <summary>
        ///     Every identity page ships external, same-origin JavaScript and CSS only, so the
        ///     policy needs no nonces, hashes, or third-party hosts. <c>img-src data:</c> covers
        ///     inline SVG/QR data URIs.
        /// </summary>
        private const string ContentSecurityPolicy =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
            + "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'; object-src 'none'";

        private readonly string _pathBase = options.Value.PathBase;

        /// <summary>Processes one request.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The pipeline task.</returns>
        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (_pathBase.Length == 0 || context.Request.Path.StartsWithSegments(_pathBase))
            {
                context.Response.OnStarting(static state =>
                {
                    IHeaderDictionary headers = ((HttpResponse)state).Headers;
                    headers.TryAdd("Content-Security-Policy", ContentSecurityPolicy);
                    headers.TryAdd("X-Frame-Options", "DENY");
                    headers.TryAdd("X-Content-Type-Options", "nosniff");
                    headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                    headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
                    return Task.CompletedTask;
                }, context.Response);
            }

            return next(context);
        }
    }
}
