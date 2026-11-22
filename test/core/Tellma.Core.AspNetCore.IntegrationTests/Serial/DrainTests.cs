// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;
using Tellma.Distro.Acme.TestSupport;

namespace Tellma.Core.AspNetCore.IntegrationTests.Serial
{
    /// <summary>
    ///     A second instance of the deployment, listening with Kestrel on a loopback port as a real
    ///     server does, and with a per-user rate of a few requests: a member past the rate refused with
    ///     the trace and build headers and no version tags, and the instance's stop waiting for a
    ///     request in flight, which still answers, before it completes. The request in flight waits on
    ///     a lock on the live tenant's centers, so the case runs alone.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class DrainTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>The second instance's requests per minute per user.</summary>
        private const int Rate = 3;

        [Fact]
        public async Task A_member_past_the_rate_is_refused_and_a_stopping_instance_finishes_the_request_in_flight()
        {
            Dictionary<string, string?> values = new(deployment.Acme.Values, StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:Api:RequestsPerMinutePerUser"] = Rate.ToString(CultureInfo.InvariantCulture),
            };
            AcmeHost instance = await AcmeHost.StartAsync(values, new AcmeHostOptions { UsesKestrel = true });
            await using (instance)
            {
                Assert.Equal(TimeSpan.FromSeconds(30), instance.Services.GetRequiredService<IOptions<HostOptions>>().Value.ShutdownTimeout);

                // --- Past the rate: refused before admission, traced, without version tags ---
                using (SpaClient restricted = new(instance, await WebSessions.SignInAsync(instance, deployment.Restricted)))
                {
                    ApiResponse refused = null!;
                    for (int i = 0; i <= Rate; i++)
                    {
                        refused = await restricted.PostAsync(Tenant, "users/me");
                    }

                    _ = refused.ExpectProblem(HttpStatusCode.TooManyRequests, "rate-limited");
                    Assert.NotNull(refused.Headers.RetryAfter?.Delta);
                    Assert.False(string.IsNullOrEmpty(refused.Header(TellmaHeaders.TraceId)));
                    Assert.False(string.IsNullOrEmpty(refused.Header(TellmaHeaders.Build)));
                    Assert.Null(refused.Header(TellmaHeaders.VersionTags));
                }

                // --- A read in flight, waiting on a lock, when the instance is told to stop ---
                using SpaClient admin = new(instance, await WebSessions.SignInAsync(instance, deployment.Administrator));
                Task stopping;
                Task<ApiResponse> read;
                await using (SqlLock schema = await SqlLock.HoldAsync(
                    deployment.Acme.TenantConnectionString(Tenant), "ALTER TABLE [gl].[Centers] SET (LOCK_ESCALATION = AUTO);"))
                {
                    read = admin.PostAsync(Tenant, "centers/query", new JsonObject { ["select"] = "Code", ["take"] = 1 });
                    _ = await schema.AwaitWaiterAsync();
                    stopping = instance.StopAsync();

                    // --- The stop waits for the request while the lock holds it ---
                    await Task.Delay(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
                    Assert.False(stopping.IsCompleted, "The instance stopped with a request in flight.");
                    Assert.False(read.IsCompleted, "The request in flight was answered while the lock held it.");
                }

                // --- Released: the request answers in full, then the stop completes ---
                _ = (await read).Expect(HttpStatusCode.OK);
                await stopping.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            }
        }
    }
}
