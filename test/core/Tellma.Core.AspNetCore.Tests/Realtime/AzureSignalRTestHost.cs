// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Defaults.Azure.AspNetCore;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     A web host with the hub on Azure SignalR Service, composed by the Azure web defaults as a
    ///     distribution composes it, and the clients that negotiate with it over the test server and
    ///     then connect to the service.
    /// </summary>
    internal static class AzureSignalRTestHost
    {
        /// <summary>How long a client keeps retrying while the host's server connections come online.</summary>
        public static readonly TimeSpan ServiceWarmUp = TimeSpan.FromSeconds(60);

        /// <summary>
        ///     A deployment environment of its own, so the run's application name on the service is
        ///     shared with no other run's.
        /// </summary>
        /// <returns>The environment's name.</returns>
        public static string UniqueEnvironment()
        {
            return $"Live{Guid.NewGuid():N}"[..12];
        }

        /// <summary>Starts a host with the hub on Azure SignalR Service and the hosting check in place.</summary>
        /// <param name="connectionString">The service's connection string.</param>
        /// <param name="environment">The deployment's environment, which names its application on the service.</param>
        /// <returns>The started host.</returns>
        public static Task<TellmaTestHost> StartAsync(string connectionString, string environment)
        {
            return RealtimeTestHost.StartAsync(
                options =>
                {
                    options.Environment = environment;
                    options.Settings[AzureWebDefaultsTellmaBuilderExtensions.SignalRConnectionStringKey] = connectionString;
                    options.ComposeWeb = static web => web.UseAzureWebDefaults();
                },
                hostingCheck: true);
        }

        /// <summary>
        ///     Connects a client to a tenant's hub through the service, retrying while the host's
        ///     server connections are not yet online, and waits for the hub to finish the connect.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The started client.</returns>
        public static async Task<HubClient> ConnectAsync(TellmaTestHost host, int tenantId, string cookie)
        {
            ArgumentNullException.ThrowIfNull(host);
            HubClient client = Client(host, tenantId, cookie);
            DateTimeOffset deadline = DateTimeOffset.UtcNow + ServiceWarmUp;
            while (true)
            {
                try
                {
                    await client.Connection.StartAsync(TestContext.Current.CancellationToken);
                    break;
                }
                catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.InternalServerError && DateTimeOffset.UtcNow < deadline)
                {
                    // The negotiate fails until one of the host's server connections is online.
                    await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                }
            }

            await host.Services.GetRequiredService<ConnectedProbe>().WaitAsync(client.Connection.ConnectionId!);
            return client;
        }

        /// <summary>
        ///     A client of a tenant's hub, not yet started: the negotiate reaches the host through the
        ///     test server with the session cookie, and everything after it reaches the service.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cookie">The session cookie, if any.</param>
        /// <returns>The client.</returns>
        public static HubClient Client(TellmaTestHost host, int tenantId, string? cookie)
        {
            ArgumentNullException.ThrowIfNull(host);
            TestServer server = host.Server;
            HubConnection connection = new HubConnectionBuilder()
                .WithUrl(new Uri(server.BaseAddress, $"/{tenantId}/hub"), options =>
                {
                    // The service's WebSocket is the client's default one, which carries the
                    // service's token and never the session cookie.
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = service => new SplitHandler(server.BaseAddress, server.CreateHandler(), service, cookie);
                })
                .Build();
            return new HubClient(connection);
        }

        /// <summary>
        ///     Sends a request for the host to the test server, with the session cookie, and any
        ///     other to the service.
        /// </summary>
        /// <param name="host">The test server's base address.</param>
        /// <param name="local">The test server's handler.</param>
        /// <param name="service">The client's own handler, which reaches the service.</param>
        /// <param name="cookie">The session cookie, sent to the host alone.</param>
        private sealed class SplitHandler(Uri host, HttpMessageHandler local, HttpMessageHandler service, string? cookie) : HttpMessageHandler
        {
            private readonly HttpMessageInvoker _local = new(local);
            private readonly HttpMessageInvoker _service = new(service);

            /// <inheritdoc />
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                ArgumentNullException.ThrowIfNull(request);
                if (!string.Equals(request.RequestUri?.Host, host.Host, StringComparison.OrdinalIgnoreCase))
                {
                    return _service.SendAsync(request, cancellationToken);
                }

                if (cookie is not null)
                {
                    request.Headers.Add("Cookie", cookie);
                }

                return _local.SendAsync(request, cancellationToken);
            }

            /// <inheritdoc />
            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    _local.Dispose();
                    _service.Dispose();
                }

                base.Dispose(disposing);
            }
        }
    }
}
