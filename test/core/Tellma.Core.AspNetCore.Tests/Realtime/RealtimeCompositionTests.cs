// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Protocol;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Core.Notifications;
using Tellma.Testing.WebHost;
using Tellma.Testing.WebHost.Realtime;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     How the web host composes the hub: the services, the hub's options and mapping, the switch
    ///     that turns it off, and the hosting check.
    /// </summary>
    public sealed class RealtimeCompositionTests
    {
        [Fact]
        public async Task With_the_hub_on_the_publisher_and_the_status_are_the_hub_s()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();
            AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await using (scope)
            {
                Assert.IsType<SignalRClientEventPublisher>(scope.ServiceProvider.GetRequiredService<IClientEventPublisher>());
            }

            Assert.True(host.Services.GetRequiredService<IRealtimeStatus>().Enabled);
            Assert.IsType<TellmaUserIdProvider>(host.Services.GetRequiredService<IUserIdProvider>());
        }

        [Fact]
        public async Task With_the_hub_off_no_hub_is_mapped_and_the_publisher_and_status_are_null()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(static options => options.Settings["Tellma:Realtime:Enabled"] = "false");

            using HttpResponseMessage response = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, host.Cookie());

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.DoesNotContain(
                host.Services.GetRequiredService<MappedEndpoints>().Endpoints().OfType<RouteEndpoint>(),
                static endpoint => endpoint.Metadata.GetMetadata<HubMetadata>() is not null);
            AsyncServiceScope scope = host.Services.CreateAsyncScope();
            await using (scope)
            {
                Assert.IsType<NullClientEventPublisher>(scope.ServiceProvider.GetRequiredService<IClientEventPublisher>());
            }

            Assert.False(host.Services.GetRequiredService<IRealtimeStatus>().Enabled);
        }

        [Fact]
        public async Task The_hub_is_mapped_on_the_hub_group_as_a_member_read_that_stamps_no_activity_and_closes_at_ticket_expiry()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync();

            RouteEndpoint[] hub = [.. host.Services.GetRequiredService<MappedEndpoints>().Endpoints()
                .OfType<RouteEndpoint>()
                .Where(static endpoint => endpoint.Metadata.GetMetadata<HubMetadata>()?.HubType == typeof(TellmaHub))];

            // The connect route is the group's own, with the trailing slash an empty pattern leaves; it matches without one.
            Assert.Equal(["/{tenantId:int:min(1)}/hub/", "/{tenantId:int:min(1)}/hub/negotiate"], hub.Select(static endpoint => endpoint.RoutePattern.RawText).Order(StringComparer.Ordinal));
            Assert.All(hub, static endpoint =>
            {
                Assert.Equal(TellmaGroup.Hub, endpoint.Metadata.GetRequiredMetadata<TellmaGroupMetadata>().Group);
                Assert.Equal(new TenantEndpointMetadata(TenantSurface.Hub, IsMutation: false, AllowWhenReadOnly: false), endpoint.Metadata.GetRequiredMetadata<TenantEndpointMetadata>());
                Assert.NotNull(endpoint.Metadata.GetMetadata<NoActivityStampMetadata>());
                Assert.NotNull(endpoint.Metadata.GetMetadata<MemberEndpointMetadata>());
            });

            // The framework carries the connection options on the negotiate, where Azure SignalR
            // Service's SDK reads them too.
            RouteEndpoint negotiate = Assert.Single(hub, static endpoint => endpoint.RoutePattern.RawText!.EndsWith("/negotiate", StringComparison.Ordinal));
            Assert.True(negotiate.Metadata.GetRequiredMetadata<HttpConnectionDispatcherOptions>().CloseOnAuthenticationExpiration);
        }

        [Fact]
        public async Task The_hub_s_options_come_from_the_realtime_options_with_one_json_protocol()
        {
            await using TellmaTestHost host = await RealtimeTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Realtime:KeepAliveInterval"] = "00:00:20";
                options.Settings["Tellma:Realtime:ClientTimeoutInterval"] = "00:01:00";
            });

            HubOptions<TellmaHub> hub = host.Services.GetRequiredService<IOptions<HubOptions<TellmaHub>>>().Value;

            Assert.Equal(TimeSpan.FromSeconds(20), hub.KeepAliveInterval);
            Assert.Equal(TimeSpan.FromMinutes(1), hub.ClientTimeoutInterval);
            Assert.Equal(4096, hub.MaximumReceiveMessageSize);
            Assert.False(hub.EnableDetailedErrors);
            Assert.Equal(["json"], hub.SupportedProtocols);
            IHubProtocol protocol = Assert.Single(host.Services.GetServices<IHubProtocol>(), static protocol => protocol.Name == "json");
            Assert.IsType<TellmaJsonHubProtocol>(protocol);
        }

        [Fact]
        public async Task A_client_timeout_shorter_than_two_keep_alives_fails_startup()
        {
            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => RealtimeTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Realtime:KeepAliveInterval"] = "00:00:30";
                options.Settings["Tellma:Realtime:ClientTimeoutInterval"] = "00:00:59";
            }));

            Assert.Contains(failure.Problems, static problem => problem.Problem.Contains("Tellma:Realtime:ClientTimeoutInterval", StringComparison.Ordinal));
        }

        [Fact]
        public async Task An_in_process_hub_without_the_job_worker_fails_the_hosting_check()
        {
            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => RealtimeTestHost.StartAsync(hostingCheck: true));

            CompositionProblem problem = Assert.Single(failure.Problems, static problem => problem.Source == RealtimeHostingCheck.CheckName);
            Assert.Contains("Tellma:Jobs:Enabled is false", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_azure_connection_string_without_azure_signalr_fails_the_hosting_check()
        {
            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(() => RealtimeTestHost.StartAsync(
                static options => options.Settings[RealtimeHostingCheck.AzureConnectionStringKey] = "Endpoint=https://acme.service.signalr.net;AccessKey=a2V5;Version=1.0;",
                hostingCheck: true));

            Assert.Contains(failure.Problems, static problem =>
                problem.Source == RealtimeHostingCheck.CheckName && problem.Problem.Contains("Azure:SignalR:ConnectionString is set", StringComparison.Ordinal));
        }

        [Theory]
        [InlineData("ExpectedInstances")]
        [InlineData("AppService")]
        public async Task Several_instances_without_a_backplane_warn(string signal)
        {
            CapturingLoggerProvider logs = new();
            (WebApplication app, _, _) = TellmaTestHost.Build(options =>
            {
                RealtimeTestHost.Configure(options, hostingCheck: true);
                options.Settings[signal == "AppService" ? RealtimeHostingCheck.AppServiceInstanceVariable : "Tellma:Realtime:ExpectedInstances"] = signal == "AppService" ? "instance-1" : "2";
                options.ConfigureServices += services => services.AddSingleton<ILoggerProvider>(logs);
            });
            await using (app)
            {
                // The check runs alone: these hosts run no job worker, which the check refuses on its own.
                IStartupCheck check = app.Services.GetServices<IStartupCheck>().Single(static check => check.Name == RealtimeHostingCheck.CheckName);
                _ = await check.CheckAsync(app.Services, TestContext.Current.CancellationToken);
            }

            CapturedEvent warning = Assert.Single(logs.Events, static e => e.EventName == "RealtimeHostingWarning");
            Assert.Equal(LogLevel.Warning, warning.Level);
        }

        [Fact]
        public async Task The_hosting_check_passes_and_warns_of_nothing_with_the_hub_off()
        {
            (WebApplication app, _, _) = TellmaTestHost.Build(static options =>
            {
                RealtimeTestHost.Configure(options, hostingCheck: true);
                options.Settings["Tellma:Realtime:Enabled"] = "false";
                options.Settings["Tellma:Realtime:ExpectedInstances"] = "3";
            });
            await using (app)
            {
                IStartupCheck check = app.Services.GetServices<IStartupCheck>().Single(static check => check.Name == RealtimeHostingCheck.CheckName);
                Assert.Empty(await check.CheckAsync(app.Services, TestContext.Current.CancellationToken));
            }
        }

        [Fact]
        public async Task Composing_the_hub_again_registers_nothing_twice_and_applies_every_delegate()
        {
            int calls = 0;
            (WebApplication app, _, _) = TellmaTestHost.Build(options =>
            {
                RealtimeTestHost.Configure(options, hostingCheck: true);
                options.ConfigureServices += services =>
                {
                    _ = services.AddTellmaRealtime(_ => calls++);

                    // A delegate that adds the framework's JSON protocol again.
                    _ = services.AddTellmaRealtime(builder =>
                    {
                        calls++;
                        _ = builder.AddJsonProtocol();
                    });
                };
            });
            await using (app)
            {
                Assert.Equal(2, calls);
                _ = Assert.Single(app.Services.GetServices<ISessionTerminationListener>().OfType<HubConnectionTracker>());
                _ = Assert.Single(app.Services.GetServices<IStartupCheck>().OfType<RealtimeHostingCheck>());
                IHubProtocol protocol = Assert.Single(app.Services.GetServices<IHubProtocol>(), static protocol => protocol.Name == "json");
                _ = Assert.IsType<TellmaJsonHubProtocol>(protocol);
            }
        }

        [Fact]
        public void The_hub_declares_no_method_a_client_could_invoke()
        {
            Assert.DoesNotContain(
                typeof(TellmaHub).GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly),
                static method => method.Name is not (nameof(Hub.OnConnectedAsync) or nameof(Hub.OnDisconnectedAsync)));
        }
    }
}
