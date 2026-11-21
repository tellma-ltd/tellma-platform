// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Csrf
{
    /// <summary>
    ///     The one CSRF control of every surface. It runs after authorization, so it reads the matched
    ///     endpoint's policy, and before the tenant middleware and every endpoint filter, so a refused
    ///     request touches no tenant.
    /// </summary>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="policies">The authorization policy provider, which resolves an endpoint's policy.</param>
    /// <param name="metrics">The host's instruments.</param>
    /// <param name="api">The API options, which name the accepted clients.</param>
    /// <param name="configuration">The application's configuration, which holds the public origin.</param>
    /// <param name="logger">The logger.</param>
    internal sealed class TellmaCsrfMiddleware(
        RequestDelegate next,
        IAuthorizationPolicyProvider policies,
        HostMetrics metrics,
        IOptions<ApiOptions> api,
        IConfiguration configuration,
        ILogger<TellmaCsrfMiddleware> logger)
    {
        /// <summary>Each endpoint's scope, decided once: an endpoint's metadata never changes.</summary>
        private readonly ConcurrentDictionary<Endpoint, CsrfScope> _scopes = new();

        /// <summary>Applies the control.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Endpoint? endpoint = context.GetEndpoint();
            if (endpoint is null)
            {
                // Nothing matched: nothing runs that could act on the cookie.
                await next(context).ConfigureAwait(false);
                return;
            }

            if (!_scopes.TryGetValue(endpoint, out CsrfScope scope))
            {
                scope = _scopes.GetOrAdd(endpoint, await ScopeOfAsync(endpoint, policies).ConfigureAwait(false));
            }

            // A safe method changes nothing, and a browser cannot add the header to a navigation or a
            // resource load, so it answers only to the provenance rules.
            if (scope == CsrfScope.Full && IsSafe(context.Request.Method))
            {
                scope = CsrfScope.Provenance;
            }

            CsrfRefusal? refusal = CsrfRules.Evaluate(
                context.Request,
                scope,
                endpoint.Metadata.GetMetadata<AcceptsBinaryMetadata>() is not null,
                configuration[HostConfigurationKeys.PublicOrigin],
                api.Value.ClientNames);
            if (refusal is null)
            {
                await next(context).ConfigureAwait(false);
                return;
            }

            // --- The refusal: counted by rule, logged with the route, answered as a problem ---
            metrics.RecordCsrfRejection(refusal.Rule);
            HostLog.CsrfRejected(logger, refusal.Rule, endpoint is RouteEndpoint route ? TellmaEndpointAudit.Describe(route) : endpoint.DisplayName ?? "(unnamed endpoint)");
            await ProblemWriter.WriteAsync(
                context,
                refusal.Status,
                "csrf-rejected",
                arguments: new Dictionary<string, object?> { ["rule"] = refusal.Rule }).ConfigureAwait(false);
        }

        /// <summary>The scope an endpoint is subject to.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="provider">The policy provider.</param>
        /// <returns>The scope.</returns>
        internal static async Task<CsrfScope> ScopeOfAsync(Endpoint endpoint, IAuthorizationPolicyProvider provider)
        {
            // Anonymous endpoints authenticate nothing, and an endpoint whose policy names another
            // scheme never reads the session cookie: neither can be ridden by a cross-site request.
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null
                || !EndpointPolicies.UsesSession(await EndpointPolicies.ResolveAsync(endpoint, provider).ConfigureAwait(false)))
            {
                return CsrfScope.Exempt;
            }

            // The hub's negotiate is a request the realtime client cannot decorate.
            return endpoint.Metadata.GetMetadata<TenantEndpointMetadata>()?.Surface == TenantSurface.Hub
                ? CsrfScope.Provenance
                : CsrfScope.Full;
        }

        /// <summary>Whether a method is one the control lets through without the header.</summary>
        /// <param name="method">The method.</param>
        /// <returns>True for <c>GET</c>, <c>HEAD</c> and <c>OPTIONS</c>.</returns>
        private static bool IsSafe(string method)
        {
            return HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
        }
    }
}
