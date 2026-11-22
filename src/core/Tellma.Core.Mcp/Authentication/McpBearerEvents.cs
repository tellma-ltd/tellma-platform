// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.AspNetCore;
using Tellma.Core.AspNetCore.Authentication;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Surfaces;

namespace Tellma.Core.Mcp.Authentication
{
    /// <summary>
    ///     The bearer scheme's events on the tenant MCP endpoint, chained ahead of any the scheme
    ///     already has and inert on every other endpoint: the token's audience must be this tenant's
    ///     MCP resource exactly, and the scheme's refusals name the resource's metadata document, so
    ///     a client that was refused can discover where to obtain a token.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A token issued for another tenant's server, or for the public API, fails
    ///         authentication: it is not a valid token for this resource, answered 401
    ///         <c>invalid_token</c>, never 403. The bearer policy's audience requirement still runs
    ///         behind it.
    ///     </para>
    ///     <para>
    ///         Every URL is built from the configured public origin and the route's tenant id, never
    ///         from the request's <c>Host</c>, so no request can make the server point a client at
    ///         another authorization server.
    ///     </para>
    /// </remarks>
    internal static class McpBearerEvents
    {
        /// <summary>The OAuth error of a missing scope.</summary>
        public const string InsufficientScope = "insufficient_scope";

        /// <summary>The OAuth error of a token that is not valid for the resource.</summary>
        public const string InvalidToken = "invalid_token";

        /// <summary>The problem code of a refused credential, as the web surface answers it.</summary>
        public const string UnauthenticatedCode = "unauthenticated";

        /// <summary>Chains the MCP events onto the bearer scheme's options.</summary>
        /// <param name="options">The bearer scheme's options.</param>
        public static void Attach(JwtBearerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            JwtBearerEvents events = options.Events ??= new JwtBearerEvents();

            // Each earlier handler runs first; the MCP endpoint's rules apply after it, and only there.
            Func<TokenValidatedContext, Task> validated = events.OnTokenValidated;
            Func<JwtBearerChallengeContext, Task> challenged = events.OnChallenge;
            Func<ForbiddenContext, Task> forbidden = events.OnForbidden;
            events.OnTokenValidated = async context =>
            {
                await validated(context).ConfigureAwait(false);
                ValidateAudience(context);
            };
            events.OnChallenge = async context =>
            {
                await challenged(context).ConfigureAwait(false);
                await ChallengeAsync(context).ConfigureAwait(false);
            };
            events.OnForbidden = async context =>
            {
                await forbidden(context).ConfigureAwait(false);
                await ForbidAsync(context).ConfigureAwait(false);
            };
        }

        /// <summary>Fails a validated token whose audience does not name this tenant's MCP resource.</summary>
        /// <param name="context">The validated token's context.</param>
        internal static void ValidateAudience(TokenValidatedContext context)
        {
            if (context.Result is { Failure: not null } || Resource(context.HttpContext) is not string resource)
            {
                return;
            }

            ClaimsPrincipal? principal = context.Principal;
            bool named = principal is not null && principal
                .FindAll(TellmaClaimTypes.Audience)
                .Any(claim => string.Equals(claim.Value, resource, StringComparison.Ordinal));
            if (!named)
            {
                context.Fail("The token was not issued for this tenant's MCP server.");
            }
        }

        /// <summary>
        ///     Answers a missing or invalid token on the MCP endpoint: 401, with the challenge naming
        ///     the resource's metadata document, and the error when a token was presented.
        /// </summary>
        /// <param name="context">The challenge's context.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static Task ChallengeAsync(JwtBearerChallengeContext context)
        {
            HttpContext http = context.HttpContext;
            if (context.Handled || MetadataUrl(http) is not string metadata)
            {
                return Task.CompletedTask;
            }

            context.HandleResponse();
            string challenge = context.AuthenticateFailure is null
                ? $"Bearer resource_metadata=\"{metadata}\""
                : $"Bearer error=\"{InvalidToken}\", resource_metadata=\"{metadata}\"";
            http.Response.Headers[HeaderNames.WWWAuthenticate] = challenge;
            return ProblemWriter.WriteAsync(http, StatusCodes.Status401Unauthorized, UnauthenticatedCode);
        }

        /// <summary>
        ///     Answers a valid token the MCP policy refuses: 403, with the challenge naming the scope
        ///     the token lacks and the resource's metadata document.
        /// </summary>
        /// <param name="context">The refusal's context.</param>
        /// <returns>A task that completes when the answer is written.</returns>
        internal static Task ForbidAsync(ForbiddenContext context)
        {
            HttpContext http = context.HttpContext;
            if (http.Response.HasStarted || MetadataUrl(http) is not string metadata)
            {
                return Task.CompletedTask;
            }

            http.Response.Headers[HeaderNames.WWWAuthenticate] =
                $"Bearer error=\"{InsufficientScope}\", scope=\"{TellmaClaimTypes.ApiScope}\", resource_metadata=\"{metadata}\"";
            return ProblemWriter.WriteAsync(http, StatusCodes.Status403Forbidden, ProblemMapper.ForbiddenCode);
        }

        /// <summary>The MCP resource of the request's tenant, when the request is to the MCP endpoint.</summary>
        /// <param name="http">The request.</param>
        /// <returns>The resource identifier, or null on any other endpoint.</returns>
        internal static string? Resource(HttpContext http)
        {
            return Target(http) is (string origin, int tenantId) ? ProtectedResourceEndpoint.Resource(origin, tenantId) : null;
        }

        /// <summary>The metadata document of the request's tenant, when the request is to the MCP endpoint.</summary>
        /// <param name="http">The request.</param>
        /// <returns>The document's URL, or null on any other endpoint.</returns>
        internal static string? MetadataUrl(HttpContext http)
        {
            return Target(http) is (string origin, int tenantId) ? ProtectedResourceEndpoint.Url(origin, tenantId) : null;
        }

        /// <summary>The public origin and the route's tenant, when the request is to the MCP endpoint.</summary>
        /// <param name="http">The request.</param>
        /// <returns>The pair, or null on any other endpoint, or when either cannot be known.</returns>
        private static (string Origin, int TenantId)? Target(HttpContext http)
        {
            // --- The MCP endpoint alone: the scheme also serves the public API ---
            if (http.GetEndpoint()?.Metadata.GetMetadata<TenantEndpointMetadata>() is not { Surface: TenantSurface.Mcp })
            {
                return null;
            }

            // --- The tenant as the route names it, in its canonical digits; the origin as configured ---
            string? origin = http.RequestServices.GetRequiredService<IConfiguration>()[HostConfigurationKeys.PublicOrigin];
            return !string.IsNullOrWhiteSpace(origin)
                && http.GetRouteValue(TellmaEndpointAudit.TenantIdParameter) is { } value
                && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int tenantId)
                && tenantId > 0
                ? (origin, tenantId)
                : null;
        }
    }
}
