// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Limits
{
    /// <summary>
    ///     The rate and concurrency policies through the real pipeline, their limits lowered through
    ///     configuration: a refusal is 429 <c>rate-limited</c> with <c>Retry-After</c>, counted by
    ///     reason and lane, and tells a missing tenant from a refused one no better than the answer
    ///     it replaces; one account's rate is capped across tenant ids; a later surface's endpoint
    ///     takes a policy through its metadata alone.
    /// </summary>
    public sealed class RateLimitingTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_user_past_the_rate_is_refused_with_retry_after_and_logged_as_a_rejection()
        {
            CapturingLoggerProvider logs = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Settings["Tellma:Api:RequestsPerMinutePerUser"] = "1";
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(logs);
            });
            string cookie = host.Cookie();

            using HttpResponseMessage first = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), Token);
            using HttpResponseMessage second = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), Token);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal("rate-limited", (await Problem.ReadAsync(second)).Code);
            TimeSpan retryAfter = Assert.IsType<TimeSpan>(second.Headers.RetryAfter?.Delta);
            Assert.InRange(retryAfter, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(1));

            // --- The refusal's log line is the rejection event every refusal before the endpoint shares ---
            CapturedEvent logged = Assert.Single(logs.Events, static captured => captured.EventName == "ApiRequestRejected");
            Assert.Equal(LogLevel.Information, logged.Level);
            Assert.Equal("rate_limited", logged.Members["Reason"]);
            Assert.Equal(TellmaRatePolicies.User, logged.Members["RatePolicy"]);
            Assert.Equal("none", logged.Members["RateLane"]);
        }

        [Fact]
        public async Task One_account_spread_over_many_tenant_ids_is_capped_across_them()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
            {
                options.Settings["Tellma:Api:RequestsPerMinutePerUser"] = "2";
                options.Settings["Tellma:Api:RequestsPerMinutePerUserAcrossTenants"] = "3";
            });
            string stranger = host.Cookie(TellmaTestHost.Stranger);

            // --- Each tenant id would grant a fresh per-tenant window; the account's own cap stops the walk ---
            var statuses = new HttpStatusCode[4];
            for (int i = 0; i < statuses.Length; i++)
            {
                using HttpResponseMessage response = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, $"/{100 + i}/api/web/probe", stranger), Token);
                statuses[i] = response.StatusCode;
            }

            Assert.Equal([HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.TooManyRequests], statuses);

            // --- Another account is untouched ---
            using HttpResponseMessage other = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie()), Token);
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        }

        [Fact]
        public async Task Another_user_and_another_surface_keep_their_own_rate()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Settings["Tellma:Api:RequestsPerMinutePerUser"] = "1");
            string cookie = host.Cookie();
            using HttpResponseMessage spent = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie), Token);

            // --- Another user of the tenant, and the public API, which this host's web policies do not cover ---
            using HttpResponseMessage other = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie("sub-2")), Token);
            using HttpResponseMessage api = await host.Client.SendAsync(TellmaTestHost.BearerRequest(HttpMethod.Get, "/1/api/v1/probe", TellmaTestHost.UserToken()), Token);

            Assert.Equal(HttpStatusCode.OK, spent.StatusCode);
            Assert.Equal(HttpStatusCode.OK, other.StatusCode);
            Assert.Equal(HttpStatusCode.OK, api.StatusCode);
        }

        [Fact]
        public async Task A_refusal_tells_a_missing_tenant_from_a_refused_member_no_better_than_the_answer_it_replaces()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Settings["Tellma:Api:RequestsPerMinutePerUser"] = "1");
            string stranger = host.Cookie(TellmaTestHost.Stranger);

            // --- Each partition spent by its first request, refused on its second ---
            var refused = new HttpResponseMessage[2];
            string[] paths = ["/1/api/web/probe", "/99/api/web/probe"];
            for (int i = 0; i < paths.Length; i++)
            {
                using HttpResponseMessage spent = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, paths[i], stranger), Token);
                Assert.Equal(HttpStatusCode.NotFound, spent.StatusCode);
                refused[i] = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, paths[i], stranger), Token);
            }

            // --- The same status, headers and members, the trace aside ---
            using HttpResponseMessage existing = refused[0];
            using HttpResponseMessage absent = refused[1];
            Assert.Equal(HttpStatusCode.TooManyRequests, existing.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, absent.StatusCode);
            Assert.Equal(HeaderNames(existing), HeaderNames(absent));
            Assert.Equal(existing.Headers.RetryAfter?.Delta, absent.Headers.RetryAfter?.Delta);
            Assert.Equal(WithoutInstance(await Problem.ReadAsync(existing)), WithoutInstance(await Problem.ReadAsync(absent)));
        }

        [Fact]
        public async Task A_refusal_is_counted_as_rate_limited_and_names_the_lane_on_the_report_policy()
        {
            TaskCompletionSource running = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource batchRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Settings["Tellma:Api:ConcurrentReportsPerUser"] = "1";
                options.Settings["Tellma:Api:ReportQueuePerUser"] = "0";

                // The held report outlives the test's other requests whatever their pace: the web
                // timeout, a real timer, is not what this test pins.
                options.Settings["Tellma:Api:RequestTimeout"] = "01:00:00";
                options.Settings["Tellma:Api:LongRequestTimeout"] = "01:00:00";

                // A later surface's report endpoint: its policy is metadata alone. Each run says it
                // started, by its lane, and holds its permit until the test releases it.
                options.Map = (_, endpoints) => endpoints.Web.MapGet("/report", async (HttpContext context) =>
                {
                    (context.Request.Headers[TellmaHeaders.Priority] == "low" ? batchRunning : running).SetResult();
                    await release.Task;
                    return Results.NoContent();
                })
                .WithMutation(false)
                .AllowMember()
                .RequireRatePolicy(TellmaRatePolicies.Reports, honorsPriority: true);
            });
            using MetricCollector<long> rejected = new(host.Services.GetRequiredService<IMeterFactory>(), ApiTelemetryNames.MeterName, ApiTelemetryNames.RequestsRejected);
            string cookie = host.Cookie();

            // --- One report holds the interactive permit; a second is refused, a tile's batch run is not ---
            // The first request is the host's first, which runs cold: the test waits for each run to
            // start, however long that takes, and hears at once when a request ends without it.
            Task<HttpResponseMessage> holding = host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/report", cookie), Token);
            HttpResponseMessage? second = null;
            Task<HttpResponseMessage>? batch = null;
            using HttpRequestMessage tile = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/report", cookie);
            tile.Headers.Add(TellmaHeaders.Priority, "low");
            try
            {
                await Patience.SignalledAsync(running.Task, holding);
                second = await host.Client.SendAsync(TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/report", cookie), Token);
                batch = host.Client.SendAsync(tile, Token);
                await Patience.SignalledAsync(batchRunning.Task, batch);
            }
            finally
            {
                // Released on a failed wait too, so the held runs end and the host stops without them.
                release.TrySetResult();
            }

            using HttpResponseMessage refused = second;
            using HttpResponseMessage first = await Patience.WaitAsync(holding);
            using HttpResponseMessage tiled = await Patience.WaitAsync(batch);

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, refused.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, tiled.StatusCode);
            CollectedMeasurement<long> measurement = Assert.Single(rejected.GetMeasurementSnapshot());
            Assert.Equal("rate_limited", measurement.Tags[ApiTelemetryNames.ReasonTag]);
            Assert.Equal("interactive", measurement.Tags[ApiTelemetryNames.LaneTag]);
        }

        [Fact]
        public async Task An_anonymous_public_document_is_limited_by_client_address()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options => options.Settings["Tellma:Api:AnonymousRequestsPerMinutePerIp"] = "1");

            using HttpResponseMessage first = await host.Client.GetAsync("/health/live", Token);
            using HttpResponseMessage second = await host.Client.GetAsync("/health/live", Token);

            Assert.Equal(HttpStatusCode.OK, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal("rate-limited", (await Problem.ReadAsync(second)).Code);
        }

        [Fact]
        public async Task The_tenant_groups_and_the_public_documents_carry_their_policies()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            RouteEndpoint[] endpoints = [.. host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()];

            Assert.Equal([TellmaRatePolicies.User, TellmaRatePolicies.Tenant], Policies(endpoints, "/{tenantId:int:min(1)}/api/web/probe"));
            Assert.Equal([TellmaRatePolicies.User, TellmaRatePolicies.Tenant], Policies(endpoints, "/{tenantId:int:min(1)}/blobs/{kind}/{id:int}"));
            Assert.Empty(Policies(endpoints, "/{tenantId:int:min(1)}/api/v{version:apiVersion}/probe"));
            Assert.Empty(Policies(endpoints, "/{tenantId:int:min(1)}/hub/negotiate"));
            Assert.Equal([TellmaRatePolicies.Anonymous], Policies(endpoints, "/api/distribution-info"));
            Assert.Equal([TellmaRatePolicies.Anonymous], Policies(endpoints, "/health/live"));
            Assert.Equal([TellmaRatePolicies.Anonymous], Policies(endpoints, "/health/ready"));
        }

        /// <summary>The policy names an endpoint's metadata carries, in order.</summary>
        /// <param name="endpoints">The host's endpoints.</param>
        /// <param name="pattern">The endpoint's route pattern.</param>
        /// <returns>The names.</returns>
        private static string[] Policies(RouteEndpoint[] endpoints, string pattern)
        {
            RouteEndpoint endpoint = endpoints.First(endpoint => endpoint.RoutePattern.RawText == pattern);
            return [.. endpoint.Metadata.GetOrderedMetadata<IRatePolicyMetadata>().Select(static policy => policy.Policy)];
        }

        /// <summary>A response's header names, sorted.</summary>
        /// <param name="response">The response.</param>
        /// <returns>The names.</returns>
        private static string[] HeaderNames(HttpResponseMessage response)
        {
            return [.. response.Headers.Concat(response.Content.Headers).Select(static header => header.Key).Order(StringComparer.OrdinalIgnoreCase)];
        }

        /// <summary>A problem's members without its trace.</summary>
        /// <param name="problem">The problem.</param>
        /// <returns>The members, serialized.</returns>
        private static string WithoutInstance(Problem problem)
        {
            var members = problem.Document.EnumerateObject()
                .Where(static member => member.Name != "instance")
                .ToDictionary(static member => member.Name, static member => member.Value, StringComparer.Ordinal);
            return JsonSerializer.Serialize(members);
        }
    }
}
