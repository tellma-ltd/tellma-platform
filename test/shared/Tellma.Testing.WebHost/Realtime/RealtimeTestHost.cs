// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Channels;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;
using Xunit;

namespace Tellma.Testing.WebHost.Realtime
{
    /// <summary>
    ///     A web host with the hub on, in the in-process mode, over five tenants, and the clients that
    ///     connect to it over the test server's WebSockets.
    /// </summary>
    internal static class RealtimeTestHost
    {
        /// <summary>An active tenant.</summary>
        public const int Active = 1;

        /// <summary>A second active tenant, for isolation.</summary>
        public const int Other = 2;

        /// <summary>A read-only tenant.</summary>
        public const int ReadOnly = 3;

        /// <summary>A frozen tenant.</summary>
        public const int Frozen = 4;

        /// <summary>A suspended tenant.</summary>
        public const int Suspended = 5;

        /// <summary>How long a client waits for what it expects.</summary>
        public static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        /// <summary>Starts a host with the hub on.</summary>
        /// <param name="configure">Changes to the defaults, applied last.</param>
        /// <param name="hostingCheck">
        ///     Whether the hosting check stays: these hosts run no job worker, so in the in-process
        ///     mode it refuses to start them.
        /// </param>
        /// <returns>The started host.</returns>
        public static Task<TellmaTestHost> StartAsync(Action<TestHostOptions>? configure = null, bool hostingCheck = false)
        {
            return TellmaTestHost.StartAsync(options =>
            {
                Configure(options, hostingCheck);
                configure?.Invoke(options);
            });
        }

        /// <summary>Turns the hub on in a host's options: the tenants, the setting, no probes.</summary>
        /// <param name="options">The host's options.</param>
        /// <param name="hostingCheck">Whether the hosting check stays.</param>
        public static void Configure(TestHostOptions options, bool hostingCheck)
        {
            ArgumentNullException.ThrowIfNull(options);
            options.Settings["Tellma:Realtime:Enabled"] = "true";

            // The probes map their own negotiate on the Hub group, which the hub's would collide with.
            options.MapProbes = false;
            options.Rows.Add(FakeCatalogReader.Row(Other));
            options.Rows.Add(FakeCatalogReader.Row(ReadOnly, "ReadOnly"));
            options.Rows.Add(FakeCatalogReader.Row(Frozen, "Frozen"));
            options.Rows.Add(FakeCatalogReader.Row(Suspended, "Suspended"));

            // A client is handed back once the hub finished its connect, groups joined.
            options.ConfigureServices += static services =>
            {
                _ = services.AddSingleton<ConnectedProbe>();
                _ = services.AddOptions<HubOptions<TellmaHub>>().Configure<ConnectedProbe>(static (hub, probe) => hub.AddFilter(probe));
            };
            if (!hostingCheck)
            {
                Action<IServiceCollection>? prior = options.ConfigureServices;
                options.ConfigureServices = services =>
                {
                    prior?.Invoke(services);
                    _ = services.Remove(services.Single(static descriptor => descriptor.ImplementationType == typeof(RealtimeHostingCheck)));
                };
            }
        }

        /// <summary>
        ///     Connects a client to a tenant's hub over WebSockets, with a session cookie, and waits
        ///     for the hub to finish the connect: the handshake completes before the hub joins the
        ///     connection to its groups.
        /// </summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cookie">The session cookie.</param>
        /// <returns>The started client.</returns>
        public static async Task<HubClient> ConnectAsync(TellmaTestHost host, int tenantId, string cookie)
        {
            ArgumentNullException.ThrowIfNull(host);
            HubClient client = Client(host, tenantId, cookie);
            await client.Connection.StartAsync(TestContext.Current.CancellationToken);
            await host.Services.GetRequiredService<ConnectedProbe>().WaitAsync(client.Connection.ConnectionId!);
            return client;
        }

        /// <summary>A client of a tenant's hub over WebSockets, not yet started.</summary>
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
                    // --- The negotiate through the test server's handler, the connect through its WebSocket client ---
                    options.Transports = HttpTransportType.WebSockets;
                    options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                    if (cookie is not null)
                    {
                        options.Headers["Cookie"] = cookie;
                    }

                    options.WebSocketFactory = async (context, cancellationToken) =>
                    {
                        WebSocketClient sockets = server.CreateWebSocketClient();
                        if (cookie is not null)
                        {
                            sockets.ConfigureRequest = request => request.Headers.Cookie = cookie;
                        }

                        return await sockets.ConnectAsync(context.Uri, cancellationToken);
                    };
                })
                .Build();
            return new HubClient(connection);
        }

        /// <summary>Publishes an event from a scope bound to a tenant, as a tenant request would.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="clientEvent">The event.</param>
        /// <returns>A task that completes when the event was handed to the hub.</returns>
        public static async Task PublishAsync(TellmaTestHost host, int tenantId, ClientEvent clientEvent)
        {
            ArgumentNullException.ThrowIfNull(host);
            AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await using (scope)
            {
                IRequestContextHolder holder = scope.ServiceProvider.GetRequiredService<IRequestContextHolder>();
                TenantInfo tenant = host.Services.GetRequiredService<ITenantRegistry>().Find(tenantId)
                    ?? throw new InvalidOperationException($"Tenant {tenantId} is not in the snapshot.");
                holder.Set(holder.Current with { Tenant = tenant.Descriptor });
                await scope.ServiceProvider.GetRequiredService<IClientEventPublisher>().PublishAsync(clientEvent);
            }
        }

        /// <summary>Sends the hub's negotiate as the SPA would, with a session cookie.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cookie">The session cookie, if any.</param>
        /// <returns>The response, which the caller disposes.</returns>
        public static async Task<HttpResponseMessage> NegotiateAsync(TellmaTestHost host, int tenantId, string? cookie)
        {
            ArgumentNullException.ThrowIfNull(host);
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, $"/{tenantId}/hub/negotiate?negotiateVersion=1", cookie);
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }

    /// <summary>
    ///     Signals when the hub finished a connection's connect, admitted or refused, and keeps the
    ///     principal the connection arrived with.
    /// </summary>
    internal sealed class ConnectedProbe : IHubFilter
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _connected = new(StringComparer.Ordinal);
        private readonly ConcurrentDictionary<string, ClaimsPrincipal?> _users = new(StringComparer.Ordinal);

        /// <inheritdoc />
        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);
            _users[context.Context.ConnectionId] = context.Context.User;
            try
            {
                await next(context);
            }
            finally
            {
                _ = Signal(context.Context.ConnectionId).TrySetResult();
            }
        }

        /// <summary>Waits for the hub to finish a connection's connect.</summary>
        /// <param name="connectionId">The connection's id.</param>
        /// <returns>A task that completes when it did.</returns>
        public Task WaitAsync(string connectionId)
        {
            return Signal(connectionId).Task.WaitAsync(RealtimeTestHost.Patience, TestContext.Current.CancellationToken);
        }

        /// <summary>The principal a connection arrived with.</summary>
        /// <param name="connectionId">The connection's id.</param>
        /// <returns>The principal; null when the connection had none or never reached the hub.</returns>
        public ClaimsPrincipal? UserOf(string connectionId)
        {
            return _users.GetValueOrDefault(connectionId);
        }

        /// <summary>A connection's signal, created by whichever side asks first.</summary>
        /// <param name="connectionId">The connection's id.</param>
        /// <returns>The signal.</returns>
        private TaskCompletionSource Signal(string connectionId)
        {
            return _connected.GetOrAdd(connectionId, static _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
        }
    }

    /// <summary>A hub client and what it received: its events, in order, and how it closed.</summary>
    internal sealed class HubClient : IAsyncDisposable
    {
        private readonly Channel<JsonElement> _events = Channel.CreateUnbounded<JsonElement>();
        private readonly TaskCompletionSource<Exception?> _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Wraps a connection, recording its events and its close.</summary>
        /// <param name="connection">The connection, not yet started.</param>
        public HubClient(HubConnection connection)
        {
            Connection = connection;
            _ = connection.On<JsonElement>(TellmaHub.EventMethod, envelope => _events.Writer.TryWrite(envelope));
            connection.Closed += exception =>
            {
                _ = _closed.TrySetResult(exception);
                return Task.CompletedTask;
            };
        }

        /// <summary>The connection.</summary>
        public HubConnection Connection { get; }

        /// <summary>Completes when the connection closed, with the error it closed with, if any.</summary>
        public Task<Exception?> Closed => _closed.Task;

        /// <summary>The next event the client received.</summary>
        /// <returns>The envelope.</returns>
        public async Task<JsonElement> NextAsync()
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(RealtimeTestHost.Patience);
            return await _events.Reader.ReadAsync(timeout.Token);
        }

        /// <summary>Waits for the connection to close.</summary>
        /// <returns>The error it closed with, if any.</returns>
        public async Task<Exception?> ClosedAsync()
        {
            return await Closed.WaitAsync(RealtimeTestHost.Patience, TestContext.Current.CancellationToken);
        }

        /// <summary>Whether an event is waiting that was not read.</summary>
        /// <returns>True when one is.</returns>
        public bool HasUnread()
        {
            return _events.Reader.TryPeek(out _);
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Connection.DisposeAsync();
        }
    }

    /// <summary>Reads a received envelope.</summary>
    internal static class Envelope
    {
        /// <summary>The envelope's event name.</summary>
        /// <param name="envelope">The envelope.</param>
        /// <returns>The name.</returns>
        public static string Name(JsonElement envelope)
        {
            return envelope.GetProperty("name").GetString()!;
        }

        /// <summary>The envelope's tenant.</summary>
        /// <param name="envelope">The envelope.</param>
        /// <returns>The tenant id.</returns>
        public static int TenantId(JsonElement envelope)
        {
            return envelope.GetProperty("tenantId").GetInt32();
        }

        /// <summary>A string member of the envelope's payload.</summary>
        /// <param name="envelope">The envelope.</param>
        /// <param name="member">The member.</param>
        /// <returns>Its value.</returns>
        public static string? Payload(JsonElement envelope, string member)
        {
            return envelope.GetProperty("payload").GetProperty(member).GetString();
        }
    }
}
