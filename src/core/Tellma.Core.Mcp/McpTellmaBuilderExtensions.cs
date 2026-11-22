// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore;
using Tellma.Core.AspNetCore.Hosting;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Composition;
using Tellma.Core.Mcp.Authentication;
using Tellma.Core.Mcp.Describe;
using Tellma.Core.Mcp.Hosting;
using Tellma.Core.Mcp.Observability;
using Tellma.Core.Mcp.Resources;
using Tellma.Core.Mcp.Tools;

namespace Tellma.Core.Mcp
{
    /// <summary>Composes the tenant MCP server into a distribution's web host.</summary>
    public static class McpTellmaBuilderExtensions
    {
        /// <summary>
        ///     Composes the tenant MCP server: one stateless Streamable HTTP endpoint per tenant at
        ///     <c>/{tenantId}/mcp</c>, mapped by <c>MapTellma</c> and announced in the distribution's
        ///     information; its bearer challenge, which points a client at the tenant's
        ///     protected-resource metadata; the request filter that runs the tenant's front door
        ///     before every tool call; and the platform's read tools with the distribution's own tool
        ///     types. Options bind from <c>Tellma:Mcp</c>.
        /// </summary>
        /// <remarks>
        ///     Called once, from the web host's <c>composeWeb</c> delegate: the server is the web host's
        ///     alone, and the migrator never composes it. A distribution's tools are SDK tool types
        ///     named in <see cref="McpOptions.ToolTypes" /> by <paramref name="configure" />; they run
        ///     under the same bearer, filter and rate limit as the platform's.
        /// </remarks>
        /// <param name="tellma">The composition builder.</param>
        /// <param name="configure">Sets the options in code, after configuration; the only place tool types are named.</param>
        /// <returns>The builder, for chaining.</returns>
        /// <exception cref="InvalidOperationException">The server was already composed.</exception>
        public static TellmaBuilder AddMcp(this TellmaBuilder tellma, Action<McpOptions>? configure = null)
        {
            ArgumentNullException.ThrowIfNull(tellma);
            IServiceCollection services = tellma.Services;

            // The tool types are read once here, so a second call could not add its own.
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(McpMarker)))
            {
                throw new InvalidOperationException("The MCP server is already composed; call AddMcp once, naming every tool type in that call.");
            }

            _ = services.AddSingleton<McpMarker>();

            // --- The options: bound by the feature, then set in code; the limits the host's policies read follow them ---
            _ = tellma.AddFeature<McpFeature>();
            McpOptions probe = new();
            if (configure is not null)
            {
                configure(probe);
                _ = services.Configure(configure);
            }

            Type[] toolTypes = [.. probe.ToolTypes];
            _ = services.AddOptions<McpLimitOptions>().Configure<IOptions<McpOptions>>(static (limits, mcp) =>
            {
                limits.ToolCallsPerMinutePerUser = mcp.Value.ToolCallsPerMinutePerUser;
                limits.ToolTimeout = mcp.Value.ToolTimeout;
            });

            // --- The server's own services ---
            _ = services.AddHttpContextAccessor();
            _ = services.AddSingleton<McpMetrics>();
            _ = services.AddSingleton<McpEntities>();
            _ = services.AddSingleton<EntityDescriber>();
            _ = services.AddSingleton<DescribeCache>();
            _ = services.AddScoped<McpRequestAccess>();
            _ = services.AddScoped<McpCallState>();
            _ = services.AddSingleton<IStartupCheck, McpToolNameCheck>();

            // --- The route, mapped among the tenant surfaces, and the bearer challenge it answers with ---
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ITenantSurfaceSource, McpSurfaceSource>());
            _ = services.PostConfigure<JwtBearerOptions>(TellmaAuthentication.BearerScheme, McpBearerEvents.Attach);

            // --- The platform's tools and resource; the SDK lists every one registered here ---
            _ = services.AddSingleton<McpServerTool, WhoamiTool>();
            _ = services.AddSingleton<McpServerTool, DescribeTool>();
            _ = services.AddSingleton<McpServerTool, QueryTool>();
            _ = services.AddSingleton<McpServerTool, AggregateTool>();
            _ = services.AddSingleton<McpServerTool, GetTool>();
            _ = services.AddSingleton<McpServerResource, QueryexSyntaxResource>();

            // --- The SDK's server: stateless, in the request's own scope, its identity set per request ---
            // The request's scope holds the context the tenant middleware bound, which every tool reads.
            IMcpServerBuilder server = services
                .AddMcpServer(static options => options.ScopeRequests = false)
                .WithHttpTransport(static transport =>
                {
                    transport.Stateless = true;
                    transport.ConfigureSessionOptions = McpServerIdentity.ConfigureAsync;
                })
                .WithRequestFilters(static filters =>
                {
                    _ = filters.AddCallToolFilter(McpToolCallFilter.CallTool);
                    _ = filters.AddListToolsFilter(McpToolCallFilter.ListTools);
                });
            if (toolTypes.Length > 0)
            {
                _ = server.WithTools(toolTypes, TellmaJson.Options);
            }

            return tellma;
        }

        /// <summary>Marks a service collection the server was composed into.</summary>
        private sealed class McpMarker
        {
        }
    }
}
