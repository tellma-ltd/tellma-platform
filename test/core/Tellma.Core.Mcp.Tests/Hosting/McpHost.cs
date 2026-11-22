// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;
using System.ComponentModel;
using System.Globalization;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.AspNetCore;
using Tellma.Testing.WebHost;

namespace Tellma.Core.Mcp.Tests.Hosting
{
    /// <summary>
    ///     The web test host with the tenant MCP server composed, over tenant 1 of the in-memory
    ///     catalog, the scripted user connection and the test issuer, with a distribution tool type
    ///     whose tools fail on purpose and a probe on the public API's surface. Started once per class.
    /// </summary>
    public sealed class McpHost : IAsyncLifetime
    {
        /// <summary>The host; null until started.</summary>
        private TellmaTestHost? _host;

        /// <summary>The started host.</summary>
        internal TellmaTestHost Host => _host ?? throw new InvalidOperationException("The host has not started.");

        /// <summary>A tenant server's resource, the audience its tokens carry.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The resource.</returns>
        internal static string Resource(int tenantId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{TellmaTestHost.PublicOrigin}/{tenantId}/mcp");
        }

        /// <summary>An access token of the test issuer.</summary>
        /// <param name="audience">The audience: one value, or an array of them.</param>
        /// <param name="subject">The subject; a member unless it is the host's stranger.</param>
        /// <param name="service">Whether the token speaks for a service account.</param>
        /// <returns>The token.</returns>
        internal static string Token(object audience, string subject = "sub-1", bool service = false)
        {
            return TellmaTestHost.Token(new Dictionary<string, object>
            {
                ["sub"] = subject,
                ["azp"] = service ? subject : "claude-code",
                ["aud"] = audience,
                ["scope"] = "tellma_api",
                ["tellma_kind"] = service ? "service" : "individual",
                ["auth_time"] = DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds(),
            });
        }

        /// <summary>Connects an SDK client to a tenant's server.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="token">The bearer token.</param>
        /// <returns>The client.</returns>
        internal async Task<McpClient> ConnectAsync(int tenantId, string token)
        {
            HttpClientTransport transport = new(
                new HttpClientTransportOptions
                {
                    Endpoint = new Uri(Resource(tenantId)),
                    TransportMode = HttpTransportMode.StreamableHttp,
                    AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + token },
                },
                Host.Server.CreateClient(),
                ownsHttpClient: true);
            return await McpClient.CreateAsync(transport, cancellationToken: TestContext.Current.CancellationToken);
        }

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _host = await TellmaTestHost.StartAsync(static options =>
            {
                // --- The server with one distribution tool type, and a probe on the public API's surface ---
                options.MapProbes = false;
                options.ComposeWeb = static tellma => _ = tellma.AddMcp(static mcp => mcp.ToolTypes.Add(typeof(FaultyTools)));
                options.Map = static (app, endpoints) =>
                {
                    _ = endpoints.Api.MapGet("/probe", static () => Results.Ok()).WithMutation(false).AllowMember();
                };
            });
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_host is not null)
            {
                await _host.DisposeAsync();
            }
        }
    }

    /// <summary>A distribution's tools that fail on purpose, each as the platform's services fail.</summary>
    [McpServerToolType]
    internal static class FaultyTools
    {
        /// <summary>Refuses the caller as an ungranted read refuses them.</summary>
        /// <returns>Never returns.</returns>
        /// <exception cref="ForbiddenException">Always.</exception>
        [McpServerTool(Name = "faulty_forbidden"), Description("Refuses the caller.")]
        public static string Forbidden()
        {
            throw new ForbiddenException("forbidden", "fx.Widget", "Read", ForbiddenReason.NoGrant);
        }

        /// <summary>Refuses the call as a malformed argument refuses it.</summary>
        /// <returns>Never returns.</returns>
        /// <exception cref="BadRequestException">Always.</exception>
        [McpServerTool(Name = "faulty_bad_request"), Description("Refuses the call.")]
        public static string BadRequest()
        {
            throw new BadRequestException("The widget is unknown.");
        }

        /// <summary>Fails as a bug fails.</summary>
        /// <returns>Never returns.</returns>
        /// <exception cref="InvalidOperationException">Always.</exception>
        [McpServerTool(Name = "faulty_bug"), Description("Fails.")]
        public static string Bug()
        {
            throw new InvalidOperationException("secret internals");
        }
    }
}
