// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics;
using System.Net;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Identity;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Identity
{
    /// <summary>
    ///     What the client reports: each call counted and timed once with its endpoint and outcome and
    ///     logged once as <c>IdentityCall</c>, each chunk a trace activity, and never a token, a secret
    ///     or an Authorization header in any log, the HTTP client factory's own included.
    /// </summary>
    public sealed class IdentityServerClientTelemetryTests : IDisposable
    {
        /// <summary>The client over the fake server.</summary>
        private readonly IdentityClientHarness _harness = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public void Dispose()
        {
            _harness.Dispose();
        }

        // --- Instruments and the call log ---

        [Fact]
        public async Task A_call_is_counted_timed_and_logged_once_with_its_endpoint_and_outcome()
        {
            using MetricCollector<long> calls = _harness.Collector<long>(UsersTelemetryNames.IdentityCalls);
            using MetricCollector<double> duration = _harness.Collector<double>(UsersTelemetryNames.IdentityCallDuration);
            _harness.Server.Api = (request, _) =>
            {
                _harness.Time.Advance(TimeSpan.FromMilliseconds(250));
                return Task.FromResult(FakeIdentityServer.Echo(request));
            };

            _ = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1500), Cancellation);

            CollectedMeasurement<long> call = Assert.Single(calls.GetMeasurementSnapshot());
            Assert.Equal(1, call.Value);
            Assert.Equal(Tags(UsersTelemetryNames.EndpointInvite, UsersTelemetryNames.OutcomeOk), call.Tags);
            CollectedMeasurement<double> timed = Assert.Single(duration.GetMeasurementSnapshot());
            Assert.Equal(0.5, timed.Value, 6);
            Assert.Equal(Tags(UsersTelemetryNames.EndpointInvite, UsersTelemetryNames.OutcomeOk), timed.Tags);

            FakeLogRecord logged = Assert.Single(_harness.Calls);
            Assert.Equal(LogLevel.Information, logged.Level);
            Assert.Equal(
                ("invite", "1500", "ok", "500"),
                (logged.GetStructuredStateValue("Endpoint"), logged.GetStructuredStateValue("Count"), logged.GetStructuredStateValue("Outcome"), logged.GetStructuredStateValue("ElapsedMs")));
        }

        [Fact]
        public async Task Each_endpoint_and_outcome_is_tagged()
        {
            using MetricCollector<long> calls = _harness.Collector<long>(UsersTelemetryNames.IdentityCalls);
            _harness.Server.Api = static (request, _) => Task.FromResult(request.Method == HttpMethod.Delete
                ? new HttpResponseMessage(HttpStatusCode.Conflict)
                : request.Uri.AbsolutePath.EndsWith("/delivery-status", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : FakeIdentityServer.Echo(request));

            _ = await _harness.Client.CreateServiceAccountAsync("Nightly import", Cancellation);
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.DeleteServiceAccountAsync("acme-sa-1", Cancellation));
            _ = await Assert.ThrowsAsync<DependencyUnavailableException>(() => _harness.Client.GetInvitationDeliveryAsync(["s-1"], Cancellation));

            Assert.Equal(
                [
                    Tags(UsersTelemetryNames.EndpointServiceAccount, UsersTelemetryNames.OutcomeOk),
                    Tags(UsersTelemetryNames.EndpointServiceAccount, UsersTelemetryNames.OutcomeError),
                    Tags(UsersTelemetryNames.EndpointDelivery, UsersTelemetryNames.OutcomeUnavailable),
                ],
                calls.GetMeasurementSnapshot().Select(static measurement => measurement.Tags));
        }

        // --- Activities ---

        [Fact]
        public async Task Each_invite_chunk_is_an_activity_tagged_with_its_count_and_outcome()
        {
            using ActivityRecorder activities = new();
            _harness.Server.Api = static (request, _) => Task.FromResult(request.Json()["users"]!.AsArray().Count == 500
                ? new HttpResponseMessage(HttpStatusCode.BadGateway)
                : FakeIdentityServer.Echo(request));

            _ = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(2500), Cancellation);

            Assert.Equal(
                [(UsersTelemetryNames.IdentityInviteActivity, 1000, "ok"), (UsersTelemetryNames.IdentityInviteActivity, 1000, "ok"), (UsersTelemetryNames.IdentityInviteActivity, 500, "unavailable")],
                activities.Stopped.Select(Describe));
        }

        [Fact]
        public async Task Each_delivery_chunk_is_an_activity_of_its_own_name()
        {
            using ActivityRecorder activities = new();

            _ = await _harness.Client.GetInvitationDeliveryAsync([.. Enumerable.Range(0, 1001).Select(static i => $"s-{i}")], Cancellation);

            Assert.Equal(
                [(UsersTelemetryNames.IdentityDeliveryActivity, 1000, "ok"), (UsersTelemetryNames.IdentityDeliveryActivity, 1, "ok")],
                activities.Stopped.Select(Describe));
        }

        // --- Secrets ---

        [Fact]
        public async Task No_token_secret_or_authorization_header_reaches_a_log_or_an_exception()
        {
            // A refused token, a retried 401, a refused token fetch, an outage and a created account:
            // every path that handles a credential.
            int api = 0;
            _harness.Server.Api = (request, _) => Task.FromResult(Interlocked.Increment(ref api) switch
            {
                1 => new HttpResponseMessage(HttpStatusCode.Unauthorized),
                3 => new HttpResponseMessage(HttpStatusCode.InternalServerError),
                _ => FakeIdentityServer.Echo(request),
            });
            List<Exception> raised = [];

            _ = await _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);
            raised.Add(await Assert.ThrowsAsync<DependencyUnavailableException>(() => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation)));
            _ = await _harness.Client.CreateServiceAccountAsync("Nightly import", Cancellation);
            _harness.Time.Advance(TimeSpan.FromHours(2));
            _harness.Server.Token = static (_, _, _) => Task.FromResult(FakeIdentityServer.JsonAnswer(
                HttpStatusCode.BadRequest,
                new JsonObject { ["error"] = "invalid_client", ["error_description"] = IdentityClientHarness.Secret }));
            raised.Add(await Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation)));

            Assert.Equal(4, _harness.Calls.Count);
            AssertNoCredential(_harness.Logs);
            Assert.All(raised, static exception => AssertNoCredential(exception.ToString()));
        }

        [Fact]
        public async Task The_composed_http_client_writes_no_credential_to_any_log_at_trace()
        {
            // The composed client, with every category logged at Trace, over the fake server.
            using FakeIdentityServer server = new();
            int api = 0;
            server.Api = (request, _) => Task.FromResult(Interlocked.Increment(ref api) == 1
                ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
                : FakeIdentityServer.Echo(request));
            IServiceCollection services = TestComposition.Compose(
                static _ => { },
                TestComposition.Configuration(TestComposition.Sound(("Tellma:Identity:ServiceClientSecret", IdentityClientHarness.Secret))));
            _ = services
                .AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace))
                .AddFakeLogging()
                .AddHttpClient(IdentityServerClient.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => server);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            IIdentityServerClient client = provider.GetRequiredService<IIdentityServerClient>();

            _ = await client.InviteAsync(IdentityClientHarness.Invitations(1), Cancellation);
            _ = await client.CreateServiceAccountAsync("Nightly import", Cancellation);

            // The factory's own request logging, whose structured header values would carry the bearer
            // token verbatim, writes nothing for the client; the client's call log is all there is.
            IReadOnlyList<FakeLogRecord> logs = provider.GetFakeLogCollector().GetSnapshot();
            Assert.DoesNotContain(logs, static record => record.Category is string category
                && category.StartsWith("System.Net.Http.HttpClient." + IdentityServerClient.HttpClientName, StringComparison.Ordinal));
            Assert.Equal(2, logs.Count(static record => record.Id.Name == "IdentityCall"));
            Assert.Equal(2, server.TokenRequests.Count);
            AssertNoCredential(logs);
        }

        /// <summary>The tags of one call's measurement.</summary>
        /// <param name="endpoint">The endpoint.</param>
        /// <param name="outcome">The outcome.</param>
        /// <returns>The tags.</returns>
        private static Dictionary<string, object?> Tags(string endpoint, string outcome)
        {
            return new() { [UsersTelemetryNames.EndpointTag] = endpoint, [UsersTelemetryNames.OutcomeTag] = outcome };
        }

        /// <summary>An activity's name, count and outcome.</summary>
        /// <param name="activity">The activity.</param>
        /// <returns>The triple.</returns>
        private static (string, int, string) Describe(Activity activity)
        {
            return (
                activity.OperationName,
                (int)activity.GetTagItem(UsersTelemetryNames.IdentityCountTag)!,
                (string)activity.GetTagItem(UsersTelemetryNames.IdentityOutcomeTag)!);
        }

        /// <summary>Asserts no record carries a credential in its message, its state or its exception.</summary>
        /// <param name="logs">The records.</param>
        private static void AssertNoCredential(IReadOnlyList<FakeLogRecord> logs)
        {
            Assert.NotEmpty(logs);
            foreach (FakeLogRecord record in logs)
            {
                AssertNoCredential(record.Message);
                AssertNoCredential(record.Exception?.ToString() ?? string.Empty);
                foreach (KeyValuePair<string, string?> pair in record.StructuredState ?? [])
                {
                    AssertNoCredential(pair.Value ?? string.Empty);
                }
            }
        }

        /// <summary>Asserts a text carries no token, secret or bearer credential.</summary>
        /// <param name="text">The text.</param>
        private static void AssertNoCredential(string text)
        {
            Assert.DoesNotContain(IdentityClientHarness.Secret, text, StringComparison.Ordinal);
            Assert.DoesNotContain("sa-secret-3b9e", text, StringComparison.Ordinal);
            Assert.DoesNotContain("token-", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bearer", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
