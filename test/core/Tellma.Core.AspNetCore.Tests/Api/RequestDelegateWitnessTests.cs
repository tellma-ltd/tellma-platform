// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Access;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Api
{
    /// <summary>
    ///     The access witness on a guarded group's endpoint that is a plain request delegate, which no
    ///     endpoint filter reaches: a success without its witness never leaves, a witnessed success
    ///     and a refusal pass.
    /// </summary>
    public sealed class RequestDelegateWitnessTests
    {
        /// <summary>The securable every endpoint of the suite requires.</summary>
        private const string Action = "Read";

        [Fact]
        public async Task A_success_that_writes_nothing_without_its_witness_is_an_internal_error()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> missing = Missing(host);

            using HttpResponseMessage response = await SendAsync(host, "quiet");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("internal", (await Problem.ReadAsync(response)).Code);
            Assert.Equal(1, missing.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        [Fact]
        public async Task A_success_that_writes_its_body_without_its_witness_loses_its_connection()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> missing = Missing(host);

            // The body never reaches the caller: the connection ends before the status line leaves.
            _ = await Assert.ThrowsAnyAsync<Exception>(() => SendAsync(host, "stream"));

            Assert.Equal(1, missing.GetMeasurementSnapshot().Sum(static measurement => measurement.Value));
        }

        [Fact]
        public async Task A_witnessed_success_answers()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> missing = Missing(host);

            using HttpResponseMessage response = await SendAsync(host, "witnessed");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("ok", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Empty(missing.GetMeasurementSnapshot());
        }

        [Fact]
        public async Task A_refusal_needs_no_witness()
        {
            await using TellmaTestHost host = await StartAsync();
            using MetricCollector<long> missing = Missing(host);

            using HttpResponseMessage response = await SendAsync(host, "refused");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Empty(missing.GetMeasurementSnapshot());
        }

        /// <summary>Starts a host with the projection suite's model and the plain request delegates on the web group.</summary>
        /// <returns>The started host.</returns>
        private static Task<TellmaTestHost> StartAsync()
        {
            return TellmaTestHost.StartAsync(static options =>
            {
                options.Compose = ProjectionModel.Compose;
                options.ConfigureServices = WebApiProjectionTests.StandIns;
                options.Map = static (_, endpoints) =>
                {
                    // --- Plain request delegates: no endpoint filter runs for any of them ---
                    Guarded(endpoints.Web.MapGet("/raw/quiet", static http =>
                    {
                        http.Response.StatusCode = StatusCodes.Status204NoContent;
                        return Task.CompletedTask;
                    }));
                    Guarded(endpoints.Web.MapGet("/raw/stream", static http => http.Response.WriteAsync("secret", http.RequestAborted)));
                    Guarded(endpoints.Web.MapGet("/raw/witnessed", static http =>
                    {
                        http.RequestServices.GetRequiredService<AccessWitness>().Record(ProjectionModel.Centers, Action);
                        return http.Response.WriteAsync("ok", http.RequestAborted);
                    }));
                    Guarded(endpoints.Web.MapGet("/raw/refused", static http =>
                    {
                        http.Response.StatusCode = StatusCodes.Status404NotFound;
                        return Task.CompletedTask;
                    }));
                };
            });

            // Each states a read and requires the model's securable.
            static void Guarded(IEndpointConventionBuilder endpoint)
            {
                _ = endpoint.WithMutation(false).RequireSecurable(ProjectionModel.Centers, Action);
            }
        }

        /// <summary>Collects the missing-witness counter.</summary>
        /// <param name="host">The host.</param>
        /// <returns>The collector.</returns>
        private static MetricCollector<long> Missing(TellmaTestHost host)
        {
            return new MetricCollector<long>(
                host.Services.GetRequiredService<IMeterFactory>(), AccessTelemetryNames.MeterName, AccessTelemetryNames.WitnessMissing);
        }

        /// <summary>Sends a GET to one of the suite's endpoints, as the SPA does.</summary>
        /// <param name="host">The host.</param>
        /// <param name="name">The endpoint's last segment.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> SendAsync(TellmaTestHost host, string name)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/raw/" + name, host.Cookie());
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }
    }
}
