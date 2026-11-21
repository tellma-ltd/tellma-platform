// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Realtime;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Defaults.Azure.AspNetCore.IntegrationTests;
using Tellma.Testing.WebHost;
using Tellma.Testing.WebHost.Realtime;

namespace Tellma.Defaults.Azure.Tests
{
    /// <summary>
    ///     The hub composed for Azure SignalR Service by the Azure web defaults, with no service to
    ///     reach: the host starts without a job worker, the front door still refuses before the
    ///     service's SDK answers a negotiate, and a member's negotiate passes the door to the SDK.
    /// </summary>
    public sealed class AzureSignalRCompositionTests
    {
        /// <summary>A connection string of the shape the service issues, for a host that resolves to nothing.</summary>
        private const string OfflineConnectionString = "Endpoint=https://offline.invalid;AccessKey=dGVzdC1rZXktdGVzdC1rZXktdGVzdC1rZXk=;Version=1.0;";

        [Fact]
        public async Task On_the_service_the_host_starts_without_the_job_worker_and_the_front_door_refuses_a_stranger_and_the_anonymous()
        {
            // The hosting check stays: on the service every instance is reached, so no worker is needed.
            await using TellmaTestHost host = await AzureSignalRTestHost.StartAsync(OfflineConnectionString, "Production");
            using MetricCollector<long> connects = new(host.Services.GetRequiredService<IMeterFactory>(), RealtimeTelemetryNames.MeterName, RealtimeTelemetryNames.Connects);

            RealtimeHosting hosting = host.Services.GetRequiredService<RealtimeHosting>();
            Assert.Equal(RealtimeMode.Azure, hosting.Mode);
            Assert.True(hosting.ReachesEveryInstance);

            // --- Refused by the tenant middleware, before the SDK's negotiate would mint a service token ---
            // The problem code and the count tell a front-door refusal apart from a route the SDK lost.
            using HttpResponseMessage stranger = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, host.Cookie(TellmaTestHost.Stranger));
            Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
            string body = await stranger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("accessToken", body, StringComparison.Ordinal);
            using (var problem = JsonDocument.Parse(body))
            {
                Assert.Equal(TenantProblemCodes.NotFound, problem.RootElement.GetProperty("code").GetString());
            }

            CollectedMeasurement<long> refused = Assert.Single(connects.GetMeasurementSnapshot());
            Assert.Equal(RealtimeMetrics.RefusedOutcome, refused.Tags[RealtimeTelemetryNames.OutcomeTag]);
            using HttpResponseMessage anonymous = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, cookie: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            // --- A member passes the door and reaches the SDK, which answers it has no server connection yet ---
            int prologues = host.Users.Prologues;
            using HttpResponseMessage member = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, host.Cookie());
            Assert.Equal(HttpStatusCode.InternalServerError, member.StatusCode);
            Assert.Equal(prologues + 1, host.Users.Prologues);
        }
    }
}
