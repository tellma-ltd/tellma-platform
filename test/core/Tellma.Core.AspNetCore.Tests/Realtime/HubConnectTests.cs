// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Net;
using System.Security.Claims;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;
using Tellma.Testing.WebHost.Realtime;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The hub's front door and its connect: who connects and who is refused at the negotiate,
    ///     what the connection's principal carries, and what the hub's scope sees.
    /// </summary>
    public sealed class HubConnectTests
    {
        [Fact]
        public async Task A_member_connects_and_is_tracked_counted_and_logged()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => CaptureDebug(options, logs));
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);
            using MetricCollector<long> connections = Collect(host, RealtimeTelemetryNames.Connections);

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());

            Assert.Equal(HubConnectionState.Connected, client.Connection.State);
            await WaitUntilAsync(() => host.Services.GetRequiredService<HubConnectionTracker>().Count == 1);
            Assert.Contains(connects.GetMeasurementSnapshot(), static m => m.Value == 1 && Equals(m.Tags[RealtimeTelemetryNames.OutcomeTag], RealtimeMetrics.ConnectedOutcome));
            Assert.Contains(connections.GetMeasurementSnapshot(), static m => m.Value == 1 && Equals(m.Tags[RealtimeTelemetryNames.ModeTag], "in-process"));
            Assert.Contains(logs.Events, static e => e.EventName == "HubConnected" && e.Level == LogLevel.Debug);

            // --- The close: forgotten, counted down, logged ---
            await client.Connection.StopAsync(TestContext.Current.CancellationToken);
            await WaitUntilAsync(() => host.Services.GetRequiredService<HubConnectionTracker>().Count == 0);
            Assert.Contains(connections.GetMeasurementSnapshot(), static m => m.Value == -1 && Equals(m.Tags[RealtimeTelemetryNames.ModeTag], "in-process"));
            await WaitUntilAsync(() => logs.Events.Any(static e => e.EventName == "HubDisconnected" && e.Level == LogLevel.Debug));
        }

        [Theory]
        [InlineData(RealtimeTestHost.ReadOnly)]
        [InlineData(RealtimeTestHost.Frozen)]
        public async Task A_read_only_or_frozen_tenant_accepts_a_connection(int tenantId)
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, tenantId, host.Cookie());

            Assert.Equal(HubConnectionState.Connected, client.Connection.State);
        }

        [Theory]
        [InlineData("stranger", RealtimeTestHost.Active, 404, TenantProblemCodes.NotFound)]
        [InlineData("member", RealtimeTestHost.Suspended, 403, TenantProblemCodes.Suspended)]
        [InlineData("member", 99, 404, TenantProblemCodes.NotFound)]
        [InlineData("anonymous", RealtimeTestHost.Active, 401, "unauthenticated")]
        public async Task The_negotiate_refuses_who_may_not_read_the_tenant(string caller, int tenantId, int status, string code)
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            string? cookie = caller switch
            {
                "member" => host.Cookie(),
                "stranger" => host.Cookie(TellmaTestHost.Stranger),
                _ => null,
            };

            using HttpResponseMessage response = await RealtimeTestHost.NegotiateAsync(host, tenantId, cookie);

            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
        }

        [Fact]
        public async Task A_refused_negotiate_is_counted_and_logged_with_its_code()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => CaptureDebug(options, logs));
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);

            using HttpResponseMessage response = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, host.Cookie(TellmaTestHost.Stranger));

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Contains(connects.GetMeasurementSnapshot(), static m => m.Value == 1 && Equals(m.Tags[RealtimeTelemetryNames.OutcomeTag], RealtimeMetrics.RefusedOutcome));
            CapturedEvent refused = Assert.Single(logs.Events, static e => e.EventName == "HubConnectionRefused");
            Assert.Equal(LogLevel.Information, refused.Level);
            Assert.Equal(TenantProblemCodes.NotFound, refused.Members["Reason"]);
        }

        [Fact]
        public async Task A_user_deactivated_since_the_connect_was_cached_is_refused_by_the_prologue()
        {
            // The connect admits the subject, as a warm cache would; the prologue's fresh read does not.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            string cookie = host.Cookie("sub-gone");
            host.Users.Deactivated[(RealtimeTestHost.Active, "sub-gone")] = true;
            int before = host.Users.Prologues;

            using HttpResponseMessage response = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, cookie);

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(TenantProblemCodes.NotFound, (await Problem.ReadAsync(response)).Code);
            Assert.Equal(before + 1, host.Users.Prologues);
        }

        [Fact]
        public async Task The_negotiate_and_the_connect_each_run_the_prologue()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            int before = host.Users.Prologues;

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());

            Assert.Equal(before + 2, host.Users.Prologues);
        }

        [Fact]
        public async Task A_cross_origin_negotiate_is_refused()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using HttpRequestMessage request = new(HttpMethod.Post, $"/{RealtimeTestHost.Active}/hub/negotiate?negotiateVersion=1");
            request.Headers.Add("Cookie", host.Cookie());
            request.Headers.Add("Sec-Fetch-Site", "cross-site");
            request.Headers.Add("Origin", "https://evil.example");

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        [Fact]
        public async Task The_connection_carries_the_hub_claims_and_the_hub_scope_stays_tenantless()
        {
            ConnectProbe probe = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options =>
                options.ConfigureServices += services => services.Configure<HubOptions<TellmaHub>>(hub => hub.AddFilter(probe)));
            host.Users.UserIds[(RealtimeTestHost.Active, "sub-7")] = 7;
            TestSession session = host.Session("sub-7");

            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);

            ClaimsPrincipal user = await probe.User.Task.WaitAsync(RealtimeTestHost.Patience, TestContext.Current.CancellationToken);
            Assert.Equal("sub-7", user.FindFirst("sub")?.Value);
            Assert.Equal("1", Assert.Single(user.FindAll(TellmaHubClaims.TenantId)).Value);
            Assert.Equal("7", Assert.Single(user.FindAll(TellmaHubClaims.UserId)).Value);
            Assert.Equal(session.Handle, Assert.Single(user.FindAll(TellmaHubClaims.SessionHandle)).Value);

            // The service's token would carry these four and nothing else of the cookie's principal.
            Assert.Equal(
                [("sub", "sub-7"), (TellmaHubClaims.TenantId, "1"), (TellmaHubClaims.UserId, "7"), (TellmaHubClaims.SessionHandle, session.Handle)],
                TellmaHubClaims.Select(user).Select(static claim => (claim.Type, claim.Value)));

            // SignalR runs the hub in a scope of its own: nothing bound it.
            Assert.Null(await probe.Tenant.Task.WaitAsync(RealtimeTestHost.Patience, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_connection_without_the_hub_claims_is_closed_and_refused()
        {
            // The hub mapped outside the Hub group, so no front door mints its claims.
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options =>
            {
                CaptureDebug(options, logs);
                options.Map = static (app, _) => app.MapHub<TellmaHub>("/bypass").AllowAnonymous().AsTenantlessEndpoint("test");
            });
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);
            HubConnection connection = new HubConnectionBuilder()
                .WithUrl(new Uri(host.Server.BaseAddress, "/bypass"), options =>
                {
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling;
                    options.HttpMessageHandlerFactory = _ => host.Server.CreateHandler();
                })
                .Build();
            await using HubClient client = new(connection);

            await connection.StartAsync(TestContext.Current.CancellationToken);
            _ = await client.ClosedAsync();

            Assert.Equal(0, host.Services.GetRequiredService<HubConnectionTracker>().Count);
            Assert.Contains(connects.GetMeasurementSnapshot(), static m => Equals(m.Tags[RealtimeTelemetryNames.OutcomeTag], RealtimeMetrics.RefusedOutcome));
            Assert.Contains(logs.Events, static e => e.EventName == "HubConnectionRefused" && e.Members["Reason"] == "claims-missing");
        }

        [Fact]
        public async Task A_client_invocation_is_refused_and_counted()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());

            // An invocation and a stream: both name a method the hub does not declare.
            HubException invoked = await Assert.ThrowsAsync<HubException>(() => client.Connection.InvokeAsync("JoinGroup", "t2", TestContext.Current.CancellationToken));
            Assert.DoesNotContain("t2", invoked.Message, StringComparison.Ordinal);
            await Assert.ThrowsAsync<HubException>(async () =>
            {
                await foreach (int _ in client.Connection.StreamAsync<int>("Stream", TestContext.Current.CancellationToken))
                {
                }
            });

            Assert.Equal(2, connects.GetMeasurementSnapshot().Count(static m => Equals(m.Tags[RealtimeTelemetryNames.OutcomeTag], RealtimeMetrics.InvocationRefusedOutcome)));
            Assert.Equal(HubConnectionState.Connected, client.Connection.State);
        }

        [Fact]
        public async Task A_frame_over_four_kilobytes_closes_the_connection_without_detail()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, host.Cookie());

            // --- Under the limit: refused as an invocation, and the connection stays open ---
            _ = await Assert.ThrowsAsync<HubException>(() => client.Connection.InvokeAsync("JoinGroup", new string('a', 3_000), TestContext.Current.CancellationToken));
            Assert.Equal(HubConnectionState.Connected, client.Connection.State);

            // --- Over it: the server closes the connection, naming no limit and no server type ---
            _ = await Assert.ThrowsAnyAsync<Exception>(() => client.Connection.InvokeAsync("JoinGroup", new string('a', 5_000), TestContext.Current.CancellationToken));
            Exception closed = Assert.IsType<HubException>(await client.ClosedAsync());
            Assert.DoesNotContain("4096", closed.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("InvalidDataException", closed.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("anonymous", 401, "unauthenticated")]
        [InlineData("cross-origin", 403, "csrf-rejected")]
        public async Task A_negotiate_refused_before_the_front_door_is_counted_and_logged_once_with_its_code(string caller, int status, string code)
        {
            // An expired session and a cross-site request are the refusals a stale client and a probe meet first.
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(options => CaptureDebug(options, logs));
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);
            using HttpRequestMessage request = new(HttpMethod.Post, $"/{RealtimeTestHost.Active}/hub/negotiate?negotiateVersion=1");
            if (caller == "cross-origin")
            {
                request.Headers.Add("Cookie", host.Cookie());
                request.Headers.Add("Sec-Fetch-Site", "cross-site");
                request.Headers.Add("Origin", "https://evil.example");
            }

            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
            CollectedMeasurement<long> refused = Assert.Single(connects.GetMeasurementSnapshot());
            Assert.Equal(RealtimeMetrics.RefusedOutcome, refused.Tags[RealtimeTelemetryNames.OutcomeTag]);
            Assert.Equal(code, Assert.Single(logs.Events, static e => e.EventName == "HubConnectionRefused").Members["Reason"]);
        }

        [Fact]
        public async Task A_refusal_by_the_front_door_is_counted_once()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            using MetricCollector<long> connects = Collect(host, RealtimeTelemetryNames.Connects);

            using HttpResponseMessage response = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Suspended, host.Cookie());

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
            CollectedMeasurement<long> refused = Assert.Single(connects.GetMeasurementSnapshot());
            Assert.Equal(RealtimeMetrics.RefusedOutcome, refused.Tags[RealtimeTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task A_connection_closes_when_its_session_ticket_expires()
        {
            // The framework checks expiry on the real clock, once a second.
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            TestSession session = host.Session(expiresAt: DateTimeOffset.UtcNow.AddSeconds(3));
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);

            _ = await client.ClosedAsync();

            Assert.Equal(HubConnectionState.Disconnected, client.Connection.State);
        }

        [Fact]
        public async Task Neither_the_negotiate_nor_the_connect_renews_or_stamps_the_session()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            DateTimeOffset before = host.Rows.ExpiresAt(session.Key)!.Value;
            host.Time.Advance(TimeSpan.FromDays(4));

            // --- The negotiate: no cookie written ---
            using HttpResponseMessage negotiate = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, session.Cookie);
            Assert.Equal(HttpStatusCode.OK, negotiate.StatusCode);
            Assert.False(SignInFlow.Parse(negotiate).ContainsKey(host.SessionCookieName));
            Assert.False(SignInFlow.Parse(negotiate).ContainsKey(TellmaAuthentication.ProfileCookieName));

            // --- The connect: the row neither renewed nor stamped active ---
            await using HubClient client = await RealtimeTestHost.ConnectAsync(host, RealtimeTestHost.Active, session.Cookie);
            Assert.Equal(before, host.Rows.ExpiresAt(session.Key));
            Assert.Equal(0, host.Rows.Renewals);
        }

        /// <summary>Lets debug events through and captures every event.</summary>
        /// <param name="options">The host's options.</param>
        /// <param name="logs">The provider that keeps them.</param>
        internal static void CaptureDebug(TestHostOptions options, CapturingLoggerProvider logs)
        {
            options.Settings["Serilog:MinimumLevel:Default"] = "Debug";
            options.Settings["Logging:LogLevel:Default"] = "Debug";
            options.ConfigureServices += services => services.AddSingleton<ILoggerProvider>(logs);
        }

        /// <summary>Collects one of the hub's instruments.</summary>
        /// <param name="host">The host.</param>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        internal static MetricCollector<long> Collect(TellmaTestHost host, string instrument)
        {
            return new MetricCollector<long>(host.Services.GetRequiredService<IMeterFactory>(), RealtimeTelemetryNames.MeterName, instrument);
        }

        /// <summary>Waits until a condition holds, which the hub reaches on its own threads.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes when it holds.</returns>
        internal static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(RealtimeTestHost.Patience);
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(20), timeout.Token);
            }
        }

        /// <summary>A hub filter that records what the hub's connect sees: the principal and the scope's tenant.</summary>
        private sealed class ConnectProbe : IHubFilter
        {
            /// <summary>The connection's principal.</summary>
            public TaskCompletionSource<ClaimsPrincipal> User { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The tenant the hub's scope is bound to; null when tenantless.</summary>
            public TaskCompletionSource<int?> Tenant { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc />
            public Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
            {
                _ = User.TrySetResult(context.Context.User!);
                _ = Tenant.TrySetResult(context.ServiceProvider.GetRequiredService<IRequestContextAccessor>().Current.Tenant?.Id);
                return next(context);
            }
        }
    }
}
