// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tellma.Core.AspNetCore.Realtime;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Realtime
{
    /// <summary>
    ///     The hub composed for Azure SignalR Service by the Azure web defaults, with no service to
    ///     reach: the host starts without a job worker, and the front door still refuses before the
    ///     service's SDK answers a negotiate.
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

            RealtimeHosting hosting = host.Services.GetRequiredService<RealtimeHosting>();
            Assert.Equal(RealtimeMode.Azure, hosting.Mode);
            Assert.True(hosting.ReachesEveryInstance);

            // --- Refused by the tenant middleware, before the SDK's negotiate would mint a service token ---
            using HttpResponseMessage stranger = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, host.Cookie(TellmaTestHost.Stranger));
            Assert.Equal(HttpStatusCode.NotFound, stranger.StatusCode);
            Assert.DoesNotContain("accessToken", await stranger.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            using HttpResponseMessage anonymous = await RealtimeTestHost.NegotiateAsync(host, RealtimeTestHost.Active, cookie: null);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }
    }
}
