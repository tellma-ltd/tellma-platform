// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.SignalR;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     The endpoint audit: a walk over every endpoint the host maps that fails startup, naming the
    ///     route and method, for an endpoint a tenant could be reached through without the platform's
    ///     controls, or one that takes a route word the platform or the SPA owns. <c>MapTellma</c> runs
    ///     it over the platform's endpoints, and the realised gate again over every endpoint.
    /// </summary>
    /// <param name="mapped">The application's endpoint sources.</param>
    /// <param name="policies">The authorization policy provider.</param>
    /// <param name="stacks">The stacks' routes, whose resource segments the audit checks.</param>
    internal sealed class TellmaEndpointAudit(MappedEndpoints mapped, IAuthorizationPolicyProvider policies, IStackRouteSource stacks) : IStartupCheck
    {
        /// <summary>The check's name.</summary>
        public const string CheckName = "host.endpoint-audit";

        /// <summary>The route parameter every tenant route begins with.</summary>
        internal const string TenantIdParameter = "tenantId";

        /// <summary>The full name of the framework's short-circuit marker, an internal type.</summary>
        private const string ShortCircuitMetadataType = "Microsoft.AspNetCore.Routing.ShortCircuit.ShortCircuitMetadata";

        /// <summary>The platform's routes under <c>/api</c>, where no endpoint of the tenantless group may sit.</summary>
        private static readonly string[] PlatformApiSegments = ["admin", "distribution-info", "client-telemetry", "webhooks"];

        /// <inheritdoc />
        public string Name => CheckName;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public async Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            return !mapped.IsMapped
                ? [new CompositionProblem(
                    CheckName,
                    "MapTellma was not called, so the platform's endpoints and route groups are not mapped.",
                    "Call app.UseTellma() and then app.MapTellma() in the web host's Program.cs.")]
                : await AuditAsync(mapped.Endpoints(), policies, stacks.Routes).ConfigureAwait(false);
        }

        /// <summary>Audits endpoints.</summary>
        /// <param name="endpoints">The endpoints.</param>
        /// <param name="provider">The authorization policy provider.</param>
        /// <param name="stacks">The stacks' routes; none when no stack registry is composed.</param>
        /// <returns>One problem per violation, each naming the route and method.</returns>
        public static async Task<IReadOnlyList<CompositionProblem>> AuditAsync(
            IEnumerable<Endpoint> endpoints, IAuthorizationPolicyProvider provider, IEnumerable<StackRoute>? stacks = null)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            ArgumentNullException.ThrowIfNull(provider);

            List<CompositionProblem> problems = [];

            // --- The stacks' segments sit at /{tenantId}/{segment}, beside the platform's words ---
            foreach (string segment in (stacks ?? []).Select(static stack => stack.ResourceSegment).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (Contains(TellmaRoutes.ServerTenantSegments, segment) || Contains(TellmaRoutes.SpaTenantSegments, segment))
                {
                    problems.Add(Problem(
                        $"The stack at /{{tenantId}}/{segment}",
                        $"takes '{segment}' as its resource segment, which names a platform tenant surface or an SPA page under /{{tenantId}}.",
                        $"Give the stack another resource segment than '{segment}'."));
                }
            }

            foreach (RouteEndpoint endpoint in endpoints.OfType<RouteEndpoint>())
            {
                string where = Describe(endpoint);

                // --- What the endpoint is under ---
                AuthorizationPolicy? policy;
                try
                {
                    policy = await EndpointPolicies.ResolveAsync(endpoint, provider).ConfigureAwait(false);
                }
                catch (InvalidOperationException exception)
                {
                    problems.Add(Problem(where, $"names an authorization policy that is not registered: {exception.Message}", "Name one of the Tellma policies, or register the policy it names."));
                    continue;
                }

                EndpointMetadataCollection metadata = endpoint.Metadata;
                bool anonymous = metadata.GetMetadata<IAllowAnonymous>() is not null;
                bool tenantless = metadata.GetMetadata<TenantlessEndpointMetadata>() is not null;
                bool platform = metadata.GetMetadata<PlatformEndpointMetadata>() is not null;
                TellmaGroup? group = metadata.GetMetadata<TellmaGroupMetadata>()?.Group;
                TenantEndpointMetadata? tenant = metadata.GetMetadata<TenantEndpointMetadata>();
                bool cookie = !anonymous && EndpointPolicies.UsesSession(policy);

                // --- Anonymous access is for tenantless endpoints alone ---
                if (anonymous && !tenantless)
                {
                    problems.Add(Problem(where, "allows anonymous callers without TenantlessEndpointMetadata.", "Call AsTenantlessEndpoint(reason) on it, or remove AllowAnonymous."));
                }

                // --- Short-circuiting is for endpoints that stated they are anonymous ---
                // Routing then runs the endpoint at once, before authentication, authorization, the
                // CSRF control and the tenant middleware, so a policy it names would never be applied.
                if (!anonymous && ShortCircuits(metadata))
                {
                    problems.Add(Problem(where, "short-circuits routing without allowing anonymous callers, so authentication, authorization, the CSRF control and the tenant middleware never run for it.", "Remove ShortCircuit(), or state an anonymous tenantless endpoint with AllowAnonymous() and AsTenantlessEndpoint(reason)."));
                }

                // --- Tenant routes: the tenant controls, every one of them ---
                if (BeginsWithTenantId(endpoint.RoutePattern))
                {
                    AuditTenantEndpoint(endpoint, where, group, tenant, policy, anonymous, problems);
                }
                else
                {
                    if (tenant is not null)
                    {
                        problems.Add(Problem(where, "carries TenantEndpointMetadata on a route that does not begin with the tenantId parameter, so no tenant can be resolved for it.", "Map the endpoint on a tenant group, under /{tenantId}."));
                    }

                    if (!tenantless && !HasAuthorization(metadata))
                    {
                        problems.Add(Problem(where, "has neither an authorization policy nor TenantlessEndpointMetadata.", "Call RequireAuthorization with a Tellma policy, or AsTenantlessEndpoint(reason)."));
                    }
                }

                // --- Cookie endpoints: side-effect-free GETs, same-origin only ---
                // An unstated endpoint is refused as such, not as the mutation it is stamped for safety.
                bool unstated = metadata.GetMetadata<MutationUnstatedMetadata>() is not null;
                if (cookie && tenant is { IsMutation: true } && !unstated && AnswersGet(metadata))
                {
                    problems.Add(Problem(where, "is a cookie-authenticated GET stamped as a mutation; a browser can be made to send a GET from any site.", "Map the mutation as a POST, or stamp WithMutation(false) on a GET that changes nothing."));
                }

                if (cookie && (metadata.GetMetadata<IEnableCorsAttribute>() is not null || metadata.GetMetadata<ICorsPolicyMetadata>() is not null))
                {
                    problems.Add(Problem(where, "attaches a CORS policy to a cookie-authenticated endpoint.", "Remove the CORS policy: a cookie-authenticated endpoint answers its own origin alone."));
                }

                // --- The tenantless group's endpoints say why they need no tenant ---
                if (group == TellmaGroup.Tenantless && !tenantless)
                {
                    problems.Add(Problem(where, "sits on the Tenantless group without TenantlessEndpointMetadata.", "Call AsTenantlessEndpoint(reason) on every endpoint of the Tenantless group."));
                }

                // --- A distribution's route words ---
                if (!platform)
                {
                    AuditRouteWords(endpoint.RoutePattern, where, group, problems);
                }
            }

            return problems;
        }

        /// <summary>Whether a route begins with the tenant id parameter.</summary>
        /// <param name="pattern">The route pattern.</param>
        /// <returns>True for a tenant route.</returns>
        internal static bool BeginsWithTenantId(RoutePattern pattern)
        {
            return pattern.PathSegments.Count > 0
                && pattern.PathSegments[0].Parts is [RoutePatternParameterPart { Name: var name }]
                && string.Equals(name, TenantIdParameter, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The route and methods a problem names: <c>GET /{tenantId}/api/web/…</c>.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <returns>The description.</returns>
        internal static string Describe(RouteEndpoint endpoint)
        {
            IReadOnlyList<string>? methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            string verbs = methods is { Count: > 0 } ? string.Join(",", methods) : "*";
            string route = endpoint.RoutePattern.RawText ?? endpoint.DisplayName ?? "(unnamed route)";
            return $"{verbs} {(route.StartsWith('/') ? route : "/" + route)}";
        }

        /// <summary>Audits an endpoint whose route begins with the tenant id.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="where">Its description.</param>
        /// <param name="group">The platform group it was mapped on, if any.</param>
        /// <param name="tenant">Its tenant metadata, if any.</param>
        /// <param name="policy">Its effective policy.</param>
        /// <param name="anonymous">Whether it allows anonymous callers.</param>
        /// <param name="problems">The problems found so far.</param>
        private static void AuditTenantEndpoint(
            RouteEndpoint endpoint,
            string where,
            TellmaGroup? group,
            TenantEndpointMetadata? tenant,
            AuthorizationPolicy? policy,
            bool anonymous,
            List<CompositionProblem> problems)
        {
            EndpointMetadataCollection metadata = endpoint.Metadata;
            if (tenant is null)
            {
                problems.Add(Problem(where, "begins with the tenantId parameter but lacks TenantEndpointMetadata, so the tenant middleware would not resolve its tenant.", "Map the endpoint on a tenant group of MapTellma (Web, Blobs, Hub or Api), which stamps it."));
            }
            else
            {
                AuditGuard(endpoint, where, group, tenant, problems);

                if (metadata.GetMetadata<MutationUnstatedMetadata>() is not null)
                {
                    problems.Add(Problem(where, "does not state whether it changes data, so no tenant state can be applied to it with certainty.", "Call WithMutation(true) or WithMutation(false) on the endpoint."));
                }

                // The surface's own policy, and no scheme beyond the one it names.
                (string allowed, string scheme) = tenant.Surface switch
                {
                    TenantSurface.Api => (TellmaPolicies.Api, TellmaAuthentication.BearerScheme),
                    TenantSurface.Mcp => (TellmaPolicies.Mcp, TellmaAuthentication.BearerScheme),
                    TenantSurface.Web or TenantSurface.Hub or TenantSurface.Blobs => (TellmaPolicies.Web, TellmaAuthentication.SessionScheme),
                    _ => throw new InvalidOperationException($"{tenant.Surface} is not a tenant surface."),
                };
                bool named = metadata.GetOrderedMetadata<IAuthorizeData>().Any(data => string.Equals(data.Policy, allowed, StringComparison.Ordinal));
                bool soleScheme = policy is not null && policy.AuthenticationSchemes.All(s => string.Equals(s, scheme, StringComparison.Ordinal));
                if (!named || !soleScheme)
                {
                    problems.Add(Problem(where, $"lacks the policy {allowed} its {tenant.Surface} surface requires, or authenticates another scheme beside it.", $"Map the endpoint on the {tenant.Surface} group, which requires {allowed}, and leave its authorization as the group sets it."));
                }
            }

            if (anonymous)
            {
                problems.Add(Problem(where, "is a tenant endpoint that allows anonymous callers.", "Remove AllowAnonymous: a tenant endpoint authenticates every caller."));
            }

            int grants = metadata.GetOrderedMetadata<SecurableEndpointMetadata>().Count + metadata.GetOrderedMetadata<MemberEndpointMetadata>().Count;
            if (grants != 1)
            {
                string found = grants == 0 ? "neither SecurableEndpointMetadata nor MemberEndpointMetadata" : $"{grants} of SecurableEndpointMetadata and MemberEndpointMetadata";
                problems.Add(Problem(where, $"carries {found}; a tenant endpoint carries exactly one.", "Call exactly one of RequireSecurable(resource, action) and AllowMember() on the endpoint."));
            }
        }

        /// <summary>
        ///     Whether an endpoint short-circuits routing. The framework's marker is internal, so it is
        ///     recognized by its name; the suite proves <c>ShortCircuit()</c> is still recognized.
        /// </summary>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <returns>True when routing runs the endpoint at once.</returns>
        private static bool ShortCircuits(EndpointMetadataCollection metadata)
        {
            return metadata.Any(static item => item.GetType().FullName == ShortCircuitMetadataType);
        }

        /// <summary>
        ///     Proves an access guard reaches a tenant endpoint: its surface is its group's, the group's
        ///     guard wraps the very delegate it runs, the hub's door keys on its surface, and an endpoint
        ///     outside the groups is MCP's, stamped by MCP's own front door.
        /// </summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="where">Its description.</param>
        /// <param name="group">The platform group it was mapped on, if any.</param>
        /// <param name="tenant">Its tenant metadata.</param>
        /// <param name="problems">The problems found so far.</param>
        private static void AuditGuard(RouteEndpoint endpoint, string where, TellmaGroup? group, TenantEndpointMetadata tenant, List<CompositionProblem> problems)
        {
            // --- The surface is the group's: the hub's door and the CSRF scope key on the surface ---
            TenantSurface? groupSurface = group switch
            {
                TellmaGroup.Web => TenantSurface.Web,
                TellmaGroup.Api => TenantSurface.Api,
                TellmaGroup.Hub => TenantSurface.Hub,
                TellmaGroup.Blobs => TenantSurface.Blobs,
                TellmaGroup.Tenantless or null => null,
                _ => throw new InvalidOperationException($"{group} is not a platform group."),
            };
            if (groupSurface is { } expected && expected != tenant.Surface)
            {
                problems.Add(Problem(where, $"is stamped the {tenant.Surface} surface on the {group} group, so the group's access guard and CSRF rules would not be the surface's.", $"Map a {tenant.Surface} endpoint on the {tenant.Surface} group, and stamp no other surface on the {group} group."));
                return;
            }

            if (groupSurface is null && tenant.Surface != TenantSurface.Mcp)
            {
                problems.Add(Problem(where, $"is a {tenant.Surface} tenant endpoint outside the platform's tenant groups, so no access guard runs for it.", $"Map it on the {tenant.Surface} group of MapTellma."));
                return;
            }

            // --- The guard each surface's front door stamps ---
            EndpointMetadataCollection metadata = endpoint.Metadata;
            switch (tenant.Surface)
            {
                case TenantSurface.Web:
                case TenantSurface.Api:
                case TenantSurface.Blobs:
                    // The group's wrapper must be the delegate the endpoint runs: a convention after it
                    // that replaced the delegate would have dropped the guard.
                    if (metadata.GetMetadata<TenantGuardMetadata>() is not { } guard || !ReferenceEquals(guard.Guarded, endpoint.RequestDelegate))
                    {
                        problems.Add(Problem(where, "runs a request delegate the tenant access guard does not wrap.", "Leave the request delegate as the tenant group's guard wraps it; do not replace it in a later convention."));
                    }

                    break;

                case TenantSurface.Hub:
                    // The tenant middleware runs the hub's front door for every Hub endpoint, as a
                    // read under the hub's provenance rules alone. That holds for a SignalR hub's own
                    // connection endpoints and nothing else, so nothing else may sit on the group.
                    if (metadata.GetMetadata<HubMetadata>() is null)
                    {
                        problems.Add(Problem(where, "is on the Hub group but is not a SignalR hub's connection endpoint, so it would be served as a read under the hub's CSRF rules alone.", "Map hubs alone on the Hub group (MapHub), and any other endpoint on the Web group."));
                    }

                    if (metadata.GetMetadata<MutationStamp>() is not null || tenant.IsMutation)
                    {
                        problems.Add(Problem(where, "states whether it changes data on the Hub group, whose connections are always reads.", "Remove WithMutation, or any TenantEndpointMetadata naming a mutation, from the hub endpoint."));
                    }

                    break;

                case TenantSurface.Mcp:
                    if (metadata.GetMetadata<TenantFrontDoorMetadata>() is not { Surface: TenantSurface.Mcp })
                    {
                        problems.Add(Problem(where, "is an MCP endpoint that MCP's front door did not install, so no access guard runs for it.", "Map the MCP endpoint through the MCP feature, whose request filter runs the access guard."));
                    }

                    break;

                default:
                    throw new InvalidOperationException($"{tenant.Surface} is not a tenant surface.");
            }
        }

        /// <summary>Audits a distribution endpoint's route words against the platform's and the SPA's.</summary>
        /// <param name="pattern">The route pattern.</param>
        /// <param name="where">The endpoint's description.</param>
        /// <param name="group">The platform group it was mapped on, if any.</param>
        /// <param name="problems">The problems found so far.</param>
        private static void AuditRouteWords(RoutePattern pattern, string where, TellmaGroup? group, List<CompositionProblem> problems)
        {
            string? first = Literal(pattern, 0);
            if (first is not null && (Contains(TellmaRoutes.ServerFirstSegments, first) || Contains(TellmaRoutes.SpaFirstSegments, first)))
            {
                if (group != TellmaGroup.Tenantless)
                {
                    problems.Add(Problem(where, $"begins with '{first}', a route word the platform or the SPA owns.", $"Choose another first segment, or map a tenantless endpoint on the Tenantless group; '{first}' is reserved."));
                }
                else if (Literal(pattern, 1) is string second && PlatformApiSegments.Contains(second, StringComparer.OrdinalIgnoreCase))
                {
                    problems.Add(Problem(where, $"sits under /api/{second}, a platform route.", $"Map it outside /api/{second}, which the platform owns."));
                }
            }

            // A tenant route outside the platform's groups must not take a tenant surface's word or an SPA page's.
            if (BeginsWithTenantId(pattern) && group is null or TellmaGroup.Tenantless
                && Literal(pattern, 1) is string word
                && (Contains(TellmaRoutes.ServerTenantSegments, word) || Contains(TellmaRoutes.SpaTenantSegments, word)))
            {
                problems.Add(Problem(where, $"takes '{word}' as its second segment, which names a platform tenant surface or an SPA page under /{{tenantId}}.", $"Choose another segment than '{word}'."));
            }
        }

        /// <summary>A path segment's text when it is one literal.</summary>
        /// <param name="pattern">The route pattern.</param>
        /// <param name="index">The segment's index.</param>
        /// <returns>The literal, or null.</returns>
        private static string? Literal(RoutePattern pattern, int index)
        {
            return index < pattern.PathSegments.Count && pattern.PathSegments[index].Parts is [RoutePatternLiteralPart literal]
                ? literal.Content
                : null;
        }

        /// <summary>Whether a set of route words holds a word, ignoring case as routing does.</summary>
        /// <param name="words">The words.</param>
        /// <param name="word">The word.</param>
        /// <returns>True when it does.</returns>
        private static bool Contains(IEnumerable<string> words, string word)
        {
            return words.Contains(word, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Whether an endpoint answers <c>GET</c>.</summary>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <returns>True when it does, or answers every method.</returns>
        private static bool AnswersGet(EndpointMetadataCollection metadata)
        {
            IReadOnlyList<string>? methods = metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
            return methods is null || methods.Count == 0 || methods.Any(HttpMethods.IsGet);
        }

        /// <summary>Whether an endpoint states any authorization of its own.</summary>
        /// <param name="metadata">The endpoint's metadata.</param>
        /// <returns>True when it does.</returns>
        private static bool HasAuthorization(EndpointMetadataCollection metadata)
        {
            return metadata.GetMetadata<IAuthorizeData>() is not null
                || metadata.GetMetadata<AuthorizationPolicy>() is not null
                || metadata.GetMetadata<IAuthorizationRequirementData>() is not null;
        }

        /// <summary>A problem of this check.</summary>
        /// <param name="where">The endpoint's description.</param>
        /// <param name="description">What is wrong.</param>
        /// <param name="fix">How to fix it.</param>
        /// <returns>The problem.</returns>
        private static CompositionProblem Problem(string where, string description, string fix)
        {
            return new CompositionProblem(CheckName, $"{where} {description}", fix);
        }
    }
}
