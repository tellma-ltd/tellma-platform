// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.IntegrationTests.Infrastructure;

namespace Tellma.Core.AspNetCore.IntegrationTests.Surface
{
    /// <summary>
    ///     The web host's instruments and log scope over real requests: each instrument observed with
    ///     its declared tags on a path that records it, a refusal's and the child surfaces' log events,
    ///     and a problem's log event carrying the client, the resource and the operation beside the
    ///     tenant and the caller. Every case shares the host and
    ///     its background work, so each assertion looks for the measurement its own request made rather
    ///     than counting.
    /// </summary>
    /// <param name="deployment">The suite's deployment.</param>
    [Trait("Category", "Integration")]
    public sealed class ObservabilityTests(AcmeDeployment deployment)
    {
        private const int Tenant = AcmeDeployment.LiveTenant;

        /// <summary>How long a measurement recorded after the answer may take to arrive.</summary>
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

        [Fact]
        public async Task Every_web_api_instrument_is_recorded_with_its_declared_tags()
        {
            IMeterFactory meters = deployment.Host.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<double> durations = new(meters, ApiTelemetryNames.MeterName, ApiTelemetryNames.OperationDuration);
            using MetricCollector<long> problems = new(meters, ApiTelemetryNames.MeterName, ApiTelemetryNames.Problems);
            using MetricCollector<long> rejected = new(meters, ApiTelemetryNames.MeterName, ApiTelemetryNames.RequestsRejected);
            using MetricCollector<long> unknown = new(meters, ApiTelemetryNames.MeterName, ApiTelemetryNames.UnknownMembers);
            using MetricCollector<long> events = new(meters, ApiTelemetryNames.MeterName, ApiTelemetryNames.RealtimeEvents);
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            // --- An operation's duration, by resource, operation and the suite's client; a body member no type declares ---
            _ = (await admin.PostAsync(Tenant, "centers/query", new JsonObject { ["select"] = "Code", ["take"] = 1, ["notAMember"] = true })).Expect(HttpStatusCode.OK);
            await AwaitAsync(durations, static tags => Is(tags, ApiTelemetryNames.ResourceTag, "gl.Center") && Is(tags, ApiTelemetryNames.OperationTag, "query")
                && Is(tags, ApiTelemetryNames.ClientTag, "test"));
            await AwaitAsync(unknown, static tags => Is(tags, ApiTelemetryNames.TypeTag, nameof(QueryRequest)));

            // --- A problem, by status and code ---
            _ = (await admin.PostAsync(Tenant, "centers/get", new JsonObject { ["id"] = int.MaxValue })).ExpectProblem(HttpStatusCode.NotFound, "not-found");
            await AwaitAsync(problems, static tags => Is(tags, "status", "404") && Is(tags, "code", "not-found"));

            // --- A request refused before it ran, by reason ---
            _ = (await admin.PostAsync(Tenant, "centers/query", new JsonObject(), static request => request.Headers.Add(TellmaHeaders.Contract, "AAAAAAAAAAAA")))
                .ExpectProblem(HttpStatusCode.PreconditionFailed, "client-outdated");
            await AwaitAsync(rejected, static tags => Is(tags, ApiTelemetryNames.ReasonTag, "client_outdated"));
            LoggedEvent refusal = await AwaitEventAsync(static entry => entry.EventName == "ApiRequestRejected" && entry.Properties.GetValueOrDefault("Reason") == "client_outdated");
            Assert.Equal(LogLevel.Information, refusal.Level);
            Assert.Contains("/centers/query", refusal.Properties["Route"], StringComparison.Ordinal);

            // --- A role save moves the tenant's permissions, and the cache event it publishes is
            // handed to the hub, by name ---
            using SpaClient stepped = await deployment.SteppedUpAsync(deployment.Administrator);
            _ = (await stepped.PostAsync(Tenant, "roles/save", Wire.Save(new JsonObject
            {
                ["id"] = 0,
                ["name"] = "Observed role",
                ["permissions"] = new JsonArray(new JsonObject { ["id"] = 0, ["resource"] = "gl.Center", ["action"] = "Read" }),
            }))).Expect(HttpStatusCode.OK);
            await AwaitAsync(events, static tags => tags.TryGetValue(ApiTelemetryNames.EventTag, out object? name) && name is string { Length: > 0 });
        }

        [Fact]
        public async Task A_problem_s_log_event_carries_the_client_resource_and_operation_beside_the_tenant_and_the_caller()
        {
            using SpaClient admin = await deployment.SignInAsync(deployment.Administrator);

            ApiResponse missing = await admin.PostAsync(Tenant, "centers/get", new JsonObject { ["id"] = int.MaxValue - 1 });

            _ = missing.ExpectProblem(HttpStatusCode.NotFound, "not-found");
            LoggedEvent problem = await AwaitEventAsync(static entry => entry.EventName == "ApiProblem" && entry.Properties.GetValueOrDefault("Code") == "not-found"
                && entry.Properties.GetValueOrDefault("RequestPath") == "/1/api/web/centers/get");
            Assert.Equal(
                ("test", "gl.Center", "get", "1", deployment.Administrator.Subject),
                (problem.Properties["Client"], problem.Properties["Resource"], problem.Properties["Operation"], problem.Properties["TenantId"], problem.Properties["Subject"]));
        }

        [Fact]
        public async Task The_child_query_surfaces_the_host_projects_are_logged_at_startup()
        {
            LoggedEvent projected = await AwaitEventAsync(static entry => entry.EventName == "ApiChildSurfacesProjected");

            Assert.Equal(LogLevel.Information, projected.Level);
            Assert.Contains("(role-memberships, owner core.User)", projected.Properties["Surfaces"], StringComparison.Ordinal);
            Assert.Contains("(permissions, owner core.Role)", projected.Properties["Surfaces"], StringComparison.Ordinal);
        }

        /// <summary>Whether a measurement's tags hold a value.</summary>
        /// <param name="tags">The tags.</param>
        /// <param name="name">The tag's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>True when they do.</returns>
        private static bool Is(IReadOnlyDictionary<string, object?> tags, string name, string value)
        {
            return tags.TryGetValue(name, out object? actual) && string.Equals(Convert.ToString(actual, System.Globalization.CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
        }

        /// <summary>Waits until an instrument recorded a measurement whose tags match.</summary>
        /// <typeparam name="T">The measurement's type.</typeparam>
        /// <param name="collector">The instrument's collector.</param>
        /// <param name="match">Whether a measurement's tags are the expected ones.</param>
        /// <returns>A task that completes when one arrived, failing after the patience ran out.</returns>
        private static async Task AwaitAsync<T>(MetricCollector<T> collector, Func<IReadOnlyDictionary<string, object?>, bool> match)
            where T : struct
        {
            var elapsed = Stopwatch.StartNew();
            while (!collector.GetMeasurementSnapshot().Any(measurement => match(measurement.Tags)))
            {
                Assert.True(
                    elapsed.Elapsed < Patience,
                    $"No matching measurement; recorded: {string.Join("; ", collector.GetMeasurementSnapshot().Select(static m => string.Join(",", m.Tags.Select(static t => $"{t.Key}={t.Value}"))))}");
                await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Waits until the host logged a matching event.</summary>
        /// <param name="match">Whether an event is the expected one.</param>
        /// <returns>The newest matching event.</returns>
        private async Task<LoggedEvent> AwaitEventAsync(Func<LoggedEvent, bool> match)
        {
            var elapsed = Stopwatch.StartNew();
            while (true)
            {
                if (deployment.Logs.Events.LastOrDefault(match) is LoggedEvent found)
                {
                    return found;
                }

                Assert.True(elapsed.Elapsed < Patience, "The host logged no matching event.");
                await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
            }
        }
    }
}
