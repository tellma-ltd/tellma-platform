// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore;
using Tellma.Core.AspNetCore.Api;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Problems;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Mcp.Observability;

namespace Tellma.Core.Mcp.Hosting
{
    /// <summary>
    ///     Maps the tenant MCP endpoint at <c>/{tenantId}/mcp</c> outside the platform's tenant groups:
    ///     the SDK's stateless Streamable HTTP endpoint, which answers POST alone, under the MCP
    ///     bearer policy, rate and timeout, as a read of any active member, behind the browser-origin
    ///     check.
    /// </summary>
    /// <remarks>
    ///     The endpoint stamps its surface's front door: the tool-call filter runs the tenant access
    ///     guard for every call, which the endpoint audit requires of an MCP endpoint. GET and DELETE
    ///     find no endpoint and are answered 405 by the platform's route-miss classification.
    /// </remarks>
    internal sealed class McpSurfaceSource : ITenantSurfaceSource
    {
        /// <summary>The route the information document announces.</summary>
        public const string AnnouncedRoute = "/{tenantId}/mcp";

        /// <summary>The surface's key in the information document.</summary>
        public const string SurfaceKey = "mcp";

        /// <summary>The detail of a refused origin's problem.</summary>
        private const string OriginRefusedDetail = "The request's browser origin is not allowed to reach this server.";

        /// <summary>The endpoint's route pattern.</summary>
        public static readonly string Pattern = TellmaEndpointRouteBuilderExtensions.TenantPrefix + "/mcp";

        /// <inheritdoc />
        public string Key => SurfaceKey;

        /// <inheritdoc />
        public string Route => AnnouncedRoute;

        /// <inheritdoc />
        public void Map(IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);
            long maxBody = endpoints.ServiceProvider.GetRequiredService<IOptions<ApiOptions>>().Value.MaxJsonBodyBytes;

            // --- The SDK's endpoint, under the surface's policy, rate, timeout and body limit ---
            // The tenant metadata makes the tenant middleware bind the tenant and the principal; the
            // platform stamp exempts the surface's own route word from the distribution rules.
            IEndpointConventionBuilder mcp = endpoints.MapMcp(Pattern)
                .RequireAuthorization(TellmaPolicies.Mcp)
                .RequireRatePolicy(TellmaRatePolicies.Mcp)
                .WithRequestTimeout(TellmaRequestTimeouts.Mcp)
                .WithMetadata(
                    new TenantEndpointMetadata(TenantSurface.Mcp, IsMutation: false, AllowWhenReadOnly: false),
                    new TenantFrontDoorMetadata(TenantSurface.Mcp),
                    PlatformEndpointMetadata.Instance,
                    new JsonBodySizeMetadata(maxBody))
                .AllowMember();

            // --- The origin check, outermost on the delegate whatever else built it ---
            mcp.Finally(WrapOriginCheck);
        }

        /// <summary>Wraps an endpoint's request delegate in the browser-origin check.</summary>
        /// <param name="endpoint">The endpoint being built.</param>
        internal static void WrapOriginCheck(EndpointBuilder endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            RequestDelegate? inner = endpoint.RequestDelegate;
            if (inner is null)
            {
                return;
            }

            endpoint.RequestDelegate = async context =>
            {
                McpOptions options = context.RequestServices.GetRequiredService<IOptions<McpOptions>>().Value;
                if (!McpOrigins.IsAllowed(context.Request, options.AllowedOrigins))
                {
                    ILogger logger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(McpLog.Category);
                    McpLog.OriginRefused(logger);
                    await ProblemWriter.WriteAsync(context, StatusCodes.Status403Forbidden, ProblemMapper.ForbiddenCode, OriginRefusedDetail).ConfigureAwait(false);
                    return;
                }

                await inner(context).ConfigureAwait(false);
            };
        }
    }
}
