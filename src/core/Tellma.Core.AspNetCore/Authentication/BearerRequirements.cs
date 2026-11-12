// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using System.Globalization;
using System.Security.Claims;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Authentication
{
    /// <summary>Requires a scope among the token's space-delimited <c>scope</c> claims.</summary>
    /// <param name="Scope">The scope.</param>
    internal sealed record ScopeRequirement(string Scope) : IAuthorizationRequirement;

    /// <summary>The resource a bearer token's audience must name.</summary>
    internal enum AudienceResource
    {
        /// <summary>The distribution's public origin: the public API and the control plane.</summary>
        PublicOrigin,

        /// <summary><c>{PublicOrigin}/{tenantId}/mcp</c>, for the tenant the route names.</summary>
        TenantMcp,
    }

    /// <summary>Requires the token's audience to name a resource exactly.</summary>
    /// <param name="Resource">The resource.</param>
    internal sealed record AudienceRequirement(AudienceResource Resource) : IAuthorizationRequirement;

    /// <summary>
    ///     Decides the bearer policies' scope and audience requirements. The bearer handler validates
    ///     no audience of its own: each policy names the canonical resource of its route.
    /// </summary>
    /// <param name="configuration">The application's configuration, which holds the public origin.</param>
    internal sealed class BearerRequirementHandler(IConfiguration configuration) : IAuthorizationHandler
    {
        /// <inheritdoc />
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            foreach (IAuthorizationRequirement requirement in context.PendingRequirements.ToList())
            {
                bool met = requirement switch
                {
                    ScopeRequirement scope => HasScope(context.User, scope.Scope),
                    AudienceRequirement audience => ExpectedAudience(audience.Resource, context.Resource) is string expected
                        && context.User.FindAll(TellmaClaimTypes.Audience).Any(claim => string.Equals(claim.Value, expected, StringComparison.Ordinal)),
                    _ => false,
                };

                // A requirement of another handler's is left to it; one of these that fails stays pending.
                if (met)
                {
                    context.Succeed(requirement);
                }
            }

            return Task.CompletedTask;
        }

        /// <summary>Whether a principal holds a scope.</summary>
        /// <param name="user">The principal.</param>
        /// <param name="scope">The scope.</param>
        /// <returns>True when one of its scope claims lists it.</returns>
        internal static bool HasScope(ClaimsPrincipal user, string scope)
        {
            return user.FindAll(TellmaClaimTypes.Scope)
                .SelectMany(static claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .Contains(scope, StringComparer.Ordinal);
        }

        /// <summary>The audience a resource requires, or null when it cannot be known.</summary>
        /// <param name="resource">The resource.</param>
        /// <param name="authorizationResource">What the authorization runs over: the request, under endpoint routing.</param>
        /// <returns>The audience.</returns>
        private string? ExpectedAudience(AudienceResource resource, object? authorizationResource)
        {
            string? origin = configuration[HostConfigurationKeys.PublicOrigin];
            if (string.IsNullOrWhiteSpace(origin))
            {
                return null;
            }

            if (resource == AudienceResource.PublicOrigin)
            {
                return origin;
            }

            // The tenant's MCP resource names the tenant the route names, in its canonical form.
            return authorizationResource is HttpContext http
                && http.GetRouteValue(TellmaEndpointAudit.TenantIdParameter) is { } value
                && int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.None, CultureInfo.InvariantCulture, out int tenantId)
                && tenantId > 0
                ? string.Create(CultureInfo.InvariantCulture, $"{origin}/{tenantId}/mcp")
                : null;
        }
    }
}
