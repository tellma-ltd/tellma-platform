// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Tests.Infrastructure;
using Tellma.Testing.WebHost;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The client telemetry relay through the host: the CSRF control, the session and the rate
    ///     policy in front of it, the body limit, the envelope refused whole, items and properties
    ///     dropped one by one, the records it writes with the client's and the server's dimensions and
    ///     the client's time corrected, what an exporter sees of them, its counters' closed tags, and
    ///     the switch that turns it off.
    /// </summary>
    public sealed class ClientTelemetryRelayTests(ClientTelemetryRelayTests.Host fixture) : IClassFixture<ClientTelemetryRelayTests.Host>
    {
        /// <summary>The relay's route.</summary>
        private const string Route = "/api/client-telemetry";

        /// <summary>The version the test host's SPA requests state in <c>Tellma-Client</c>.</summary>
        private const string Version = "1.0.0";

        /// <summary>A tab's id, a random UUID as the SPA's tab bus makes it.</summary>
        private const string TabId = "3f2b8c1e-9a4d-4c7e-b1f0-6d5a2e8c9b71";

        /// <summary>A request's trace id, as the SPA records it.</summary>
        private const string TraceId = "0af7651916cd43dd8448eb211c80319c";

        /// <summary>Another request's trace id.</summary>
        private const string OtherTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        /// <summary>The browser's user agent.</summary>
        private const string UserAgent = "Mozilla/5.0 (X11; Linux x86_64) TellmaTests/1.0";

        [Fact]
        public async Task A_batch_is_answered_204_and_each_item_logged_under_the_client_s_and_the_server_s_dimensions()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));

            // --- A tab whose clock runs an hour behind the server's ---
            DateTimeOffset now = host.Time.GetUtcNow();
            DateTimeOffset sentAt = now - TimeSpan.FromHours(1);
            JsonObject batch = Batch(
                sentAt,
                Item("timing", "tm.page.visit", sentAt - TimeSpan.FromSeconds(30), new JsonObject
                {
                    ["route"] = "/:t/centers",
                    ["pageKind"] = "search",
                    ["ms"] = 412,
                    ["outcome"] = "ready",
                    ["sampleRate"] = 0.5,
                }),
                Item("error", "tm.error.captured", sentAt - TimeSpan.FromSeconds(10), ErrorProperties(), TraceId));

            using HttpResponseMessage response = await PostAsync(host, batch.ToJsonString());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            DeploymentVersions versions = host.Services.GetRequiredService<DeploymentVersions>();
            string deploymentId = host.Services.GetRequiredService<DeploymentIdentity>().DeploymentId;

            // --- The timing: a custom event named by the attribute, its reserved properties renamed ---
            CapturedEvent visit = Assert.Single(capture.Events, static captured => captured.EventName == "ClientEvent");
            Assert.Equal(LogLevel.Information, visit.Level);
            Assert.Contains("tm.page.visit", visit.Message, StringComparison.Ordinal);
            Assert.Equal("tm.page.visit", visit.Members[ClientTelemetryLog.CustomEventNameAttribute]);
            Assert.Equal("sub-1", visit.Members["Subject"]);
            Assert.Equal(UserAgent, visit.Members["UserAgent"]);
            Assert.Equal(Version, visit.Members["ClientVersion"]);
            Assert.Equal((versions.DistributionVersion != Version).ToString(CultureInfo.InvariantCulture), visit.Members["ClientOutdated"]);
            Assert.Equal(TabId, visit.Members["ClientTabId"]);
            Assert.Equal("17", visit.Members["ClientTenantId"]);
            Assert.Equal(deploymentId, visit.Members["tellma.deployment.id"]);
            Assert.Equal(versions.DistributionVersion, visit.Members["DistributionVersion"]);
            Assert.Equal("/:t/centers", visit.Members["ClientRoute"]);
            Assert.Equal("search", visit.Members["ClientPageKind"]);
            Assert.Equal(0.5, double.Parse(visit.Members["SampleRate"]!, CultureInfo.InvariantCulture));
            Assert.Equal("412", visit.Members["ms"]);
            Assert.Equal("ready", visit.Members["outcome"]);
            Assert.False(visit.Members.ContainsKey("route"));
            Assert.False(visit.Members.ContainsKey("DroppedProperties"));

            // The client's time moved by its skew: thirty seconds before the server received the batch.
            Assert.Equal((now - TimeSpan.FromSeconds(30)).ToString("O", CultureInfo.InvariantCulture), visit.Members["ClientAt"]);

            // --- The error: a Warning trace naming its class and route, its extras under their own names ---
            CapturedEvent error = Assert.Single(capture.Events, static captured => captured.EventName == "ClientError");
            Assert.Equal(LogLevel.Warning, error.Level);
            Assert.Contains("Client error", error.Message, StringComparison.Ordinal);
            Assert.Equal("TmApiError", error.Members["ErrorClass"]);
            Assert.Equal("/:t/centers", error.Members["ClientRoute"]);
            Assert.Equal("api", error.Members["ErrorSource"]);
            Assert.Equal("validation", error.Members["ErrorKind"]);
            Assert.Equal("validation", error.Members["ErrorCode"]);
            Assert.Equal("422", error.Members["Status"]);
            Assert.Equal("f1a2b3c4", error.Members["Fingerprint"]);
            Assert.Equal("Failed to load <url> for <email>", error.Members["Message"]);
            Assert.Equal("at load (chunk-K3J7Q2M9.js:1:2345)", error.Members["Stack"]);
            Assert.Equal(OtherTraceId, error.Members["RecentTraceIds"]);
            Assert.Equal((now - TimeSpan.FromSeconds(10)).ToString("O", CultureInfo.InvariantCulture), error.Members["ClientAt"]);
            Assert.False(error.Members.ContainsKey("errorClass"));
            Assert.False(error.Members.ContainsKey("source"));

            // --- Nothing was dropped, so no drop record ---
            Assert.DoesNotContain(capture.Events, static captured => captured.EventName == "ClientTelemetryDropped");
        }

        [Fact]
        public async Task A_client_clock_ahead_of_the_server_s_is_corrected_back()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));

            DateTimeOffset now = host.Time.GetUtcNow();
            DateTimeOffset sentAt = now + TimeSpan.FromMinutes(7);
            using HttpResponseMessage response = await PostAsync(
                host, Batch(sentAt, Item("event", "tm.action.used", sentAt - TimeSpan.FromSeconds(5))).ToJsonString());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            CapturedEvent used = Assert.Single(capture.Events, static captured => captured.EventName == "ClientEvent");
            Assert.Equal((now - TimeSpan.FromSeconds(5)).ToString("O", CultureInfo.InvariantCulture), used.Members["ClientAt"]);
        }

        [Fact]
        public async Task A_client_on_the_deployment_s_own_version_is_not_outdated()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));
            string current = host.Services.GetRequiredService<DeploymentVersions>().DistributionVersion;
            DateTimeOffset now = host.Time.GetUtcNow();

            using HttpResponseMessage currentResponse = await PostAsync(host, Batch(now, current, Item("event", "tm.action.used", now)).ToJsonString(), clientVersion: current);
            using HttpResponseMessage olderResponse = await PostAsync(host, Batch(now, "0.0.1-old", Item("event", "tm.action.used", now)).ToJsonString(), clientVersion: "0.0.1-old");

            Assert.Equal(HttpStatusCode.NoContent, currentResponse.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, olderResponse.StatusCode);
            Assert.Equal(
                ["False", "True"],
                capture.Events.Where(static captured => captured.EventName == "ClientEvent").Select(static captured => captured.Members["ClientOutdated"]));
        }

        [Fact]
        public async Task Dropped_items_and_properties_still_answer_204_and_one_record_counts_the_items_by_reason()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));

            DateTimeOffset sentAt = host.Time.GetUtcNow();
            JsonObject batch = Batch(
                sentAt,
                Item("event", "tm.action.used", sentAt),
                Item("metric", "tm.action.used", sentAt),
                null,
                Item("event", "Tm.Action", sentAt),
                Item("error", "tm.error.other", sentAt),
                Item("event", "tm.action.used", sentAt - TimeSpan.FromHours(25)),
                Item("event", "tm.action.used", sentAt + TimeSpan.FromMinutes(2)),
                Item("event", "tm.action.used", sentAt, new JsonObject { ["nested"] = new JsonObject(), ["Upper"] = 1, ["kept"] = 1 }));

            using HttpResponseMessage response = await PostAsync(host, batch.ToJsonString());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            CapturedEvent[] kept = [.. capture.Events.Where(static captured => captured.EventName == "ClientEvent")];
            Assert.Equal(2, kept.Length);
            Assert.False(kept[0].Members.ContainsKey("DroppedProperties"));
            Assert.Equal("2", kept[1].Members["DroppedProperties"]);
            Assert.Equal("1", kept[1].Members["kept"]);

            CapturedEvent dropped = Assert.Single(capture.Events, static captured => captured.EventName == "ClientTelemetryDropped");
            Assert.Equal(LogLevel.Information, dropped.Level);
            Assert.Equal("Client telemetry dropped 6 items", dropped.Message);
            Assert.Equal("2", dropped.Members["DroppedKind"]);
            Assert.Equal("2", dropped.Members["DroppedName"]);
            Assert.Equal("2", dropped.Members["DroppedTime"]);
            Assert.Equal(TabId, dropped.Members["ClientTabId"]);
        }

        [Fact]
        public async Task An_exporter_sees_custom_events_and_warning_traces_under_the_item_s_trace_and_no_span()
        {
            TelemetryRecorder recorder = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options => options.ConfigureServices = recorder.AddTo);

            // --- Items with a trace id, and one whose trace id is not one, which is kept without it ---
            DateTimeOffset sentAt = host.Time.GetUtcNow();
            JsonObject batch = Batch(
                sentAt,
                Item("event", "tm.api.slow", sentAt, new JsonObject { ["api"] = "centers/query", ["durationMs"] = 4810, ["outcome"] = "ok" }, TraceId),
                Item("error", "tm.error.captured", sentAt, ErrorProperties(), OtherTraceId),
                Item("event", "tm.action.used", sentAt, traceId: TraceId.ToUpperInvariant()),
                Item("event", "tm.action.saved", sentAt, traceId: new string('0', 32)));
            using HttpResponseMessage response = await PostAsync(host, batch.ToJsonString());
            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            string relayTrace = Assert.Single(response.Headers.GetValues(TellmaHeaders.TraceId));

            const string Category = ClientTelemetryNames.LoggerCategory;
            ExportedLog[] relayed = [.. recorder.Logs.Where(static log => log.Category == Category)];
            Assert.Equal(4, relayed.Length);

            // --- The slow request: a custom event, written under the request's trace ---
            ExportedLog slow = relayed[0];
            Assert.Equal(LogLevel.Information, slow.Level);
            // The exporter reads each attribute's own value: text is text, unquoted, and a number is a number.
            Assert.Equal("tm.api.slow", Assert.IsType<string>(slow.Attributes[ClientTelemetryLog.CustomEventNameAttribute]));
            Assert.Equal(TraceId, slow.TraceId.ToHexString());
            Assert.Equal("centers/query", Assert.IsType<string>(slow.Attributes["api"]));
            Assert.Equal(4810L, Assert.IsType<long>(slow.Attributes["durationMs"]));
            Assert.Equal("sub-1", Assert.IsType<string>(slow.Attributes["Subject"]));
            Assert.Equal(17, Assert.IsType<int>(slow.Attributes["ClientTenantId"]));
            Assert.Equal(Category, slow.Category);

            // --- The error: a Warning trace with its extras, under its own trace ---
            ExportedLog error = relayed[1];
            Assert.Equal(LogLevel.Warning, error.Level);
            Assert.Null(error.Text(ClientTelemetryLog.CustomEventNameAttribute));
            Assert.Equal(OtherTraceId, error.TraceId.ToHexString());
            Assert.Equal("api", error.Text("ErrorSource"));
            Assert.Equal("TmApiError", error.Text("ErrorClass"));

            // --- A trace id that is not one is ignored: the item is kept, under no trace of the client's ---
            Assert.Equal("tm.action.used", relayed[2].Text(ClientTelemetryLog.CustomEventNameAttribute));
            Assert.Equal("tm.action.saved", relayed[3].Text(ClientTelemetryLog.CustomEventNameAttribute));
            Assert.All(relayed[2..], static log => Assert.NotEqual(TraceId, log.TraceId.ToHexString()));

            // --- No span: neither the items' activities nor the relay's request ---
            // A traced request after the relay's proves the recorder sees spans.
            using HttpResponseMessage probe = await host.Client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
            string probeTrace = Assert.Single(probe.Headers.GetValues(TellmaHeaders.TraceId));
            await Patience.UntilAsync(() => recorder.Spans.Any(span => span.TraceId.ToHexString() == probeTrace), "the probe's span");
            Assert.DoesNotContain(recorder.Spans, static span => span.OperationName == ClientTelemetryWriter.ItemActivityName);
            Assert.DoesNotContain(recorder.Spans, span => span.TraceId.ToHexString() is TraceId or OtherTraceId || span.TraceId.ToHexString() == relayTrace);
        }

        [Fact]
        public async Task The_counters_take_closed_tag_values_alone()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            IMeterFactory meters = host.Services.GetRequiredService<IMeterFactory>();
            using MetricCollector<long> errors = new(meters, ClientTelemetryNames.MeterName, ClientTelemetryNames.Errors);
            using MetricCollector<long> slow = new(meters, ClientTelemetryNames.MeterName, ClientTelemetryNames.SlowRequests);

            DateTimeOffset sentAt = host.Time.GetUtcNow();
            JsonObject batch = Batch(
                sentAt,
                Item("error", "tm.error.captured", sentAt, new JsonObject { ["source"] = "api", ["kind"] = "validation" }),
                Item("error", "tm.error.captured", sentAt, new JsonObject { ["source"] = "render" }),
                Item("error", "tm.error.captured", sentAt, new JsonObject { ["source"] = "evil", ["kind"] = "evil" }),
                Item("error", "tm.error.captured", sentAt, new JsonObject { ["source"] = 5, ["kind"] = true }),
                Item("error", "tm.error.captured", sentAt, new JsonObject { ["kind"] = "server" }),
                Item("event", "tm.api.slow", sentAt, new JsonObject { ["outcome"] = "pending" }),
                Item("event", "tm.api.slow", sentAt, new JsonObject { ["outcome"] = "slowish" }),
                Item("event", "tm.api.slow", sentAt),
                Item("event", "tm.page.visit", sentAt, new JsonObject { ["outcome"] = "ok" }));

            using HttpResponseMessage response = await PostAsync(host, batch.ToJsonString());

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.Equal(
                [("api", "validation"), ("render", "none"), ("other", "other"), ("other", "other"), ("other", "server")],
                errors.GetMeasurementSnapshot().Select(static measurement =>
                    ((string)measurement.Tags[ClientTelemetryNames.SourceTag]!, (string)measurement.Tags[ClientTelemetryNames.KindTag]!)));
            Assert.Equal(
                ["pending", "other", "other"],
                slow.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[ClientTelemetryNames.OutcomeTag]!));

            // Each tag value is the set's own string, never the client's.
            string clientKind = (string)errors.GetMeasurementSnapshot()[0].Tags[ClientTelemetryNames.KindTag]!;
            Assert.Same(ClientTelemetryMetrics.ErrorKinds.Single(static kind => kind == "validation"), clientKind);
        }

        [Fact]
        public async Task A_batch_renews_neither_the_session_s_cookie_nor_its_row()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            TestSession session = host.Session(accessTokenLifetime: TimeSpan.FromDays(30));
            DateTimeOffset before = host.Rows.ExpiresAt(session.Key)!.Value;
            host.Time.Advance(TimeSpan.FromDays(4));
            DateTimeOffset sentAt = host.Time.GetUtcNow();

            using HttpResponseMessage response = await PostAsync(host, Batch(sentAt, Item("event", "tm.action.used", sentAt)).ToJsonString(), session.Cookie);

            Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
            Assert.False(SignInFlow.Parse(response).ContainsKey(host.SessionCookieName));
            Assert.Equal(before, host.Rows.ExpiresAt(session.Key));
            Assert.Equal(0, host.Rows.Renewals);
        }

        [Fact]
        public async Task The_rate_policy_refuses_a_user_s_batches_past_the_minute_s_limit()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(static options =>
                options.Settings["Tellma:Api:TelemetryBatchesPerMinutePerUser"] = "1");
            DateTimeOffset sentAt = host.Time.GetUtcNow();
            string body = Batch(sentAt, Item("event", "tm.action.used", sentAt)).ToJsonString();

            using HttpResponseMessage first = await PostAsync(host, body);
            using HttpResponseMessage second = await PostAsync(host, body);
            using HttpResponseMessage other = await PostAsync(host, body, host.Cookie("sub-2"));

            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
            Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
            Assert.Equal("rate-limited", (await Problem.ReadAsync(second)).Code);
            Assert.NotNull(second.Headers.RetryAfter?.Delta);
            Assert.Equal(HttpStatusCode.NoContent, other.StatusCode);
        }

        [Fact]
        public async Task A_relay_turned_off_answers_204_unread_writes_nothing_and_tells_the_spa()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
            {
                options.Settings["Tellma:ClientTelemetry:Enabled"] = "false";
                options.Settings["Tellma:ClientTelemetry:SampleRate"] = "0.25";
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture);
            });
            DateTimeOffset sentAt = host.Time.GetUtcNow();

            using HttpResponseMessage malformed = await PostAsync(host, "{");
            using HttpResponseMessage sound = await PostAsync(host, Batch(sentAt, Item("error", "tm.error.captured", sentAt)).ToJsonString());

            Assert.Equal(HttpStatusCode.NoContent, malformed.StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, sound.StatusCode);
            Assert.DoesNotContain(capture.Events, static captured => captured.EventName is "ClientEvent" or "ClientError" or "ClientTelemetryDropped");

            // --- The information document carries the switch and the rate to the SPA ---
            using HttpResponseMessage info = await host.Client.GetAsync(new Uri("/api/distribution-info", UriKind.Relative), TestContext.Current.CancellationToken);
            using var document = JsonDocument.Parse(await info.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            JsonElement telemetry = document.RootElement.GetProperty("telemetry");
            Assert.False(telemetry.GetProperty("enabled").GetBoolean());
            Assert.Equal(0.25, telemetry.GetProperty("sampleRate").GetDouble());
        }

        [Theory]
        [InlineData("no-client", HttpStatusCode.Forbidden, "csrf-rejected")]
        [InlineData("cross-site", HttpStatusCode.Forbidden, "csrf-rejected")]
        [InlineData("text", HttpStatusCode.UnsupportedMediaType, "csrf-rejected")]
        [InlineData("no-session", HttpStatusCode.Unauthorized, "unauthenticated")]
        public async Task The_csrf_control_and_the_session_guard_the_relay(string request, HttpStatusCode status, string code)
        {
            TellmaTestHost host = fixture.Started;
            DateTimeOffset sentAt = host.Time.GetUtcNow();
            using HttpRequestMessage message = TellmaTestHost.SpaRequest(
                HttpMethod.Post, Route, request == "no-session" ? null : host.Cookie(), Batch(sentAt, Item("event", "tm.action.used", sentAt)).ToJsonString());
            switch (request)
            {
                case "no-client":
                    _ = message.Headers.Remove(TellmaHeaders.Client);
                    break;
                case "cross-site":
                    _ = message.Headers.Remove("Sec-Fetch-Site");
                    message.Headers.Add("Sec-Fetch-Site", "cross-site");
                    break;
                case "text":
                    message.Content!.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
                    break;
                default:
                    break;
            }

            using HttpResponseMessage response = await host.Client.SendAsync(message, TestContext.Current.CancellationToken);

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(code, (await Problem.ReadAsync(response)).Code);
        }

        [Theory]
        [InlineData("version")]
        [InlineData("tab-id")]
        [InlineData("tenant-zero")]
        [InlineData("tenant-negative")]
        [InlineData("too-many")]
        [InlineData("no-time")]
        [InlineData("no-items")]
        [InlineData("null")]
        public async Task A_broken_envelope_is_refused_whole_as_a_bad_request(string broken)
        {
            TellmaTestHost host = fixture.Started;
            DateTimeOffset sentAt = host.Time.GetUtcNow();
            JsonObject batch = Batch(sentAt, Item("event", "tm.action.used", sentAt));
            switch (broken)
            {
                case "version":
                    batch["version"] = "1.0.1";
                    break;
                case "tab-id":
                    batch["tabId"] = "short";
                    break;
                case "tenant-zero":
                    batch["tenantId"] = 0;
                    break;
                case "tenant-negative":
                    batch["tenantId"] = -1;
                    break;
                case "too-many":
                    batch["items"] = Items([.. Enumerable.Range(0, 101).Select(_ => Item("event", "tm.action.used", sentAt))]);
                    break;
                case "no-time":
                    _ = batch.Remove("sentAt");
                    break;
                case "no-items":
                    _ = batch.Remove("items");
                    break;
                default:
                    break;
            }

            using HttpResponseMessage response = await PostAsync(host, broken == "null" ? "null" : batch.ToJsonString());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("bad-request", problem.Code);
            Assert.Null(problem.Argument("reason"));
        }

        [Theory]
        [InlineData("{")]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData(/*lang=json*/ """{ "version": "1.0.0", "version": "1.0.0" }""")]
        [InlineData(/*lang=json*/ """{ "version": "1.0.0", "tabId": "k3J7q2M9xA", "tenantId": "17", "sentAt": "2026-10-20T11:03:12.512Z", "items": [] }""")]
        [InlineData(/*lang=json*/ """{ "version": "1.0.0", "tabId": "k3J7q2M9xA", "sentAt": "2026-10-20 11:03", "items": [] }""")]
        public async Task A_body_that_is_not_a_batch_s_json_is_a_malformed_request(string body)
        {
            using HttpResponseMessage response = await PostAsync(fixture.Started, body);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Problem problem = await Problem.ReadAsync(response);
            Assert.Equal("bad-request", problem.Code);
            Assert.Equal("malformed-json", problem.Argument("reason"));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_body_past_32_KB_is_refused_and_one_of_exactly_32_KB_read(bool sized)
        {
            TellmaTestHost host = fixture.Started;
            DateTimeOffset sentAt = host.Time.GetUtcNow();
            string batch = Batch(sentAt, Item("event", "tm.action.used", sentAt)).ToJsonString();

            // White space pads a sound batch to the limit, then one byte past it.
            string full = batch + new string(' ', ClientTelemetryRules.MaxBodyBytes - Encoding.UTF8.GetByteCount(batch));
            using HttpResponseMessage accepted = await PostAsync(host, full, content: Body(full, sized));
            using HttpResponseMessage refused = await PostAsync(host, full + " ", content: Body(full + " ", sized));

            Assert.Equal(HttpStatusCode.NoContent, accepted.StatusCode);
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, refused.StatusCode);
            Assert.Equal("limit-exceeded", (await Problem.ReadAsync(refused)).Code);
        }

        /// <summary>A batch's JSON in the test host's SPA version.</summary>
        /// <param name="sentAt">When the client sent it.</param>
        /// <param name="items">Its items; a null stands for a <c>null</c> in the list.</param>
        /// <returns>The batch.</returns>
        private static JsonObject Batch(DateTimeOffset sentAt, params JsonObject?[] items)
        {
            return Batch(sentAt, Version, items);
        }

        /// <summary>A batch's JSON.</summary>
        /// <param name="sentAt">When the client sent it.</param>
        /// <param name="version">The UI version it states.</param>
        /// <param name="items">Its items; a null stands for a <c>null</c> in the list.</param>
        /// <returns>The batch.</returns>
        private static JsonObject Batch(DateTimeOffset sentAt, string version, params JsonObject?[] items)
        {
            return new JsonObject
            {
                ["version"] = version,
                ["tabId"] = TabId,
                ["tenantId"] = 17,
                ["sentAt"] = Iso(sentAt),
                ["items"] = Items(items),
            };
        }

        /// <summary>A list of items' JSON.</summary>
        /// <param name="items">The items; a null stands for a <c>null</c> in the list.</param>
        /// <returns>The list.</returns>
        private static JsonArray Items(IEnumerable<JsonObject?> items)
        {
            JsonArray list = [];
            foreach (JsonObject? item in items)
            {
                list.Add(item);
            }

            return list;
        }

        /// <summary>An item's JSON.</summary>
        /// <param name="kind">Its kind.</param>
        /// <param name="name">Its name.</param>
        /// <param name="at">When the client recorded it.</param>
        /// <param name="properties">Its properties, if any.</param>
        /// <param name="traceId">Its trace id, if any.</param>
        /// <returns>The item.</returns>
        private static JsonObject Item(string kind, string name, DateTimeOffset at, JsonObject? properties = null, string? traceId = null)
        {
            JsonObject item = new() { ["kind"] = kind, ["name"] = name, ["at"] = Iso(at) };
            if (traceId is not null)
            {
                item["traceId"] = traceId;
            }

            if (properties is not null)
            {
                item["properties"] = properties;
            }

            return item;
        }

        /// <summary>An API error's properties, as the SPA's error capture records them.</summary>
        /// <returns>The properties.</returns>
        private static JsonObject ErrorProperties()
        {
            return new JsonObject
            {
                ["route"] = "/:t/centers",
                ["pageKind"] = "search",
                ["source"] = "api",
                ["errorClass"] = "TmApiError",
                ["kind"] = "validation",
                ["code"] = "validation",
                ["status"] = 422,
                ["fingerprint"] = "f1a2b3c4",
                ["message"] = "Failed to load https://acme.app.tellma.com/17/centers?q=jed for farah@acme.com",
                ["stack"] = "at load (chunk-K3J7Q2M9.js:1:2345)",
                ["recentTraceIds"] = OtherTraceId,
            };
        }

        /// <summary>A time as the SPA writes it: UTC to the millisecond.</summary>
        /// <param name="time">The time.</param>
        /// <returns>The text.</returns>
        private static string Iso(DateTimeOffset time)
        {
            return time.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>A JSON body, with or without a stated length.</summary>
        /// <param name="json">The body.</param>
        /// <param name="sized">Whether it states its length.</param>
        /// <returns>The content.</returns>
        private static HttpContent Body(string json, bool sized)
        {
            HttpContent content = sized ? new StringContent(json, Encoding.UTF8) : new UnsizedContent(Encoding.UTF8.GetBytes(json));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
            return content;
        }

        /// <summary>Posts a body to the relay as the SPA does.</summary>
        /// <param name="host">The host.</param>
        /// <param name="json">The body.</param>
        /// <param name="cookie">The session cookie; the test host's own when null.</param>
        /// <param name="clientVersion">The version <c>Tellma-Client</c> states; the test host's SPA's when null.</param>
        /// <param name="content">The body's content in place of the JSON's own; null for the JSON's.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> PostAsync(
            TellmaTestHost host, string json, string? cookie = null, string? clientVersion = null, HttpContent? content = null)
        {
            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Post, Route, cookie ?? host.Cookie(), json);
            if (content is not null)
            {
                request.Content!.Dispose();
                request.Content = content;
            }

            if (clientVersion is not null)
            {
                _ = request.Headers.Remove(TellmaAuthentication.CsrfHeaderName);
                request.Headers.Add(TellmaAuthentication.CsrfHeaderName, "web/" + clientVersion);
            }

            _ = request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            return await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     One Production host for the cases that send a request and read its answer alone: the
        ///     refusals, which write no item, and the body limit's two batches. A case that reads logs,
        ///     instruments, sessions or the clock, or changes the configuration, starts a host of its own.
        /// </summary>
        public sealed class Host : IAsyncLifetime
        {
            /// <summary>The host, once started.</summary>
            private TellmaTestHost? _host;

            /// <summary>The started host.</summary>
            internal TellmaTestHost Started => _host ?? throw new InvalidOperationException("The host has not started.");

            /// <inheritdoc />
            public async ValueTask InitializeAsync()
            {
                _host = await TellmaTestHost.StartAsync();
            }

            /// <inheritdoc />
            public async ValueTask DisposeAsync()
            {
                if (_host is not null)
                {
                    await _host.DisposeAsync();
                }
            }
        }

        /// <summary>A body that does not state its length, so the server reads it to its end.</summary>
        /// <param name="bytes">The body.</param>
        private sealed class UnsizedContent(byte[] bytes) : HttpContent
        {
            /// <inheritdoc />
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            {
                return stream.WriteAsync(bytes, 0, bytes.Length);
            }

            /// <inheritdoc />
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }
        }
    }
}
