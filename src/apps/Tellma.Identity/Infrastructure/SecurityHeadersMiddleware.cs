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
        ///     inline SVG/QR data URIs. <c>require-trusted-types-for 'script'</c> blocks DOM-XSS
        ///     sinks (the ceremony scripts touch none), honoring the Trusted Types commitment.
        /// </summary>
        private const string PolicyBeforeFormAction =
            "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; "
            + "connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";

        /// <summary>The rest of the policy, after whatever <c>form-action</c> ends up allowing.</summary>
        private const string PolicyAfterFormAction =
            "; object-src 'none'; require-trusted-types-for 'script'; trusted-types 'none'";

        /// <summary>The policy for a page that named no extra form-submission destinations.</summary>
        private const string ContentSecurityPolicy = PolicyBeforeFormAction + PolicyAfterFormAction;

        private readonly string _pathBase = options.Value.PathBase;

        /// <summary>Processes one request.</summary>
        /// <param name="context">The request context.</param>
        /// <returns>The pipeline task.</returns>
        public Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            if (_pathBase.Length == 0 || context.Request.Path.StartsWithSegments(_pathBase))
            {
                // Deferred to OnStarting so the policy can still take account of what the page
                // being rendered asks for: the endpoint runs after this middleware, not before.
                context.Response.OnStarting(static state =>
                {
                    var http = (HttpContext)state;
                    IHeaderDictionary headers = http.Response.Headers;
                    headers.TryAdd("Content-Security-Policy", BuildPolicy(http));
                    headers.TryAdd("X-Frame-Options", "DENY");
                    headers.TryAdd("X-Content-Type-Options", "nosniff");
                    headers.TryAdd("Referrer-Policy", "strict-origin-when-cross-origin");
                    headers.TryAdd("Cross-Origin-Opener-Policy", "same-origin");
                    return Task.CompletedTask;
                }, context);
            }

            return next(context);
        }

        /// <summary>
        ///     Builds this response's policy, widening <c>form-action</c> by whatever the page
        ///     named. Almost every page names nothing and gets the constant.
        /// </summary>
        /// <param name="context">The request context.</param>
        /// <returns>The header value.</returns>
        private static string BuildPolicy(HttpContext context)
        {
            return CspFormAction.Allowed(context) is { Count: > 0 } sources
                ? PolicyBeforeFormAction + " " + string.Join(' ', sources) + PolicyAfterFormAction
                : ContentSecurityPolicy;
        }
    }
}
