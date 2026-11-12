// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Tests.Infrastructure;

namespace Tellma.Core.AspNetCore.Tests.Observability
{
    /// <summary>
    ///     The trace a request runs under and the headers that name it: a browser's trace id is
    ///     adopted without its sampling decision, every answer names its trace and build, and a
    ///     database span never carries query text.
    /// </summary>
    public sealed class ObservabilityTests
    {
        /// <summary>A W3C trace id.</summary>
        private const string TraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

        /// <summary>A W3C span id.</summary>
        private const string SpanId = "00f067aa0ba902b7";

        [Theory]
        [InlineData("01")]
        [InlineData("00")]
        public void Adopt_keeps_the_trace_and_drops_the_sampling_decision(string flags)
        {
            Assert.Equal($"00-{TraceId}-{SpanId}-00", BrowserTraceContextPropagator.Adopt($"00-{TraceId}-{SpanId}-{flags}"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01")]
        [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01")]
        public void Adopt_refuses_a_malformed_traceparent(string? traceparent)
        {
            Assert.Null(BrowserTraceContextPropagator.Adopt(traceparent));
        }

        [Fact]
        public async Task A_request_runs_under_the_trace_its_traceparent_names()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();
            using HttpRequestMessage request = new(HttpMethod.Get, "/api/distribution-info");
            request.Headers.Add("traceparent", $"00-{TraceId}-{SpanId}-01");
            request.Headers.Add("tracestate", "vendor=secret");
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(TraceId, Assert.Single(response.Headers.GetValues(TellmaHeaders.TraceId)));
        }

        [Fact]
        public async Task Every_answer_names_its_trace_and_build_early_refusals_included()
        {
            await using TellmaTestHost host = await TellmaTestHost.StartAsync();

            // An authentication refusal, a CSRF refusal and a not-found, none of which reaches a handler.
            using HttpRequestMessage unauthenticated = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", cookie: null);
            using HttpRequestMessage forged = TellmaTestHost.SpaRequest(HttpMethod.Post, "/1/api/web/probe", host.Cookie(), "{}");
            forged.Headers.Remove("Sec-Fetch-Site");
            forged.Headers.Add("Sec-Fetch-Site", "cross-site");
            using HttpRequestMessage missing = new(HttpMethod.Get, "/nothing/here");

            foreach ((HttpRequestMessage request, HttpStatusCode status) in new[]
            {
                (unauthenticated, HttpStatusCode.Unauthorized),
                (forged, HttpStatusCode.Forbidden),
                (missing, HttpStatusCode.NotFound),
            })
            {
                using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
                Assert.Equal(status, response.StatusCode);
                string traceId = Assert.Single(response.Headers.GetValues(TellmaHeaders.TraceId));
                Assert.Matches("^[0-9a-f]{32}$", traceId);
                Assert.False(string.IsNullOrEmpty(Assert.Single(response.Headers.GetValues(TellmaHeaders.Build))));

                // The problem's instance is the same trace the header names.
                Assert.Equal(traceId, (await Problem.ReadAsync(response)).Instance);
            }
        }

        [Fact]
        public async Task Each_event_reaches_every_logger_provider_once_and_the_host_defaults_none()
        {
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));

            // The host's defaults (console, debug, event source, event log) are gone: Serilog's sinks
            // replace them, and OpenTelemetry's provider is the one Serilog forwards to.
            string[] providers = [.. host.Services.GetServices<ILoggerProvider>()
                .Where(provider => provider != capture)
                .Select(static provider => provider.GetType().Name)];
            Assert.Equal(["OpenTelemetryLoggerProvider"], providers);

            const string Marker = "tellma-log-once-marker";
            host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Tellma.Tests").Log(LogLevel.Warning, default, Marker, null, static (state, _) => state);
            Assert.Equal(1, capture.Count(Marker));

            // The request log goes through the host's own logger too, once.
            using HttpResponseMessage response = await host.Client.GetAsync(new Uri("/health/live", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            CapturedEvent completed = Assert.Single(capture.Events, static captured => captured.Message.Contains(" responded 200", StringComparison.Ordinal));

            // A tenantless request's completion event names no tenant.
            Assert.False(completed.Members.ContainsKey("TenantId"));
        }

        [Fact]
        public async Task A_tenant_requests_completion_event_names_its_tenant_and_user()
        {
            // The event is written after the tenant middleware's logger scope has closed.
            CapturingLoggerProvider capture = new();
            await using TellmaTestHost host = await TellmaTestHost.StartAsync(options =>
                options.ConfigureServices = services => services.AddSingleton<ILoggerProvider>(capture));

            using HttpRequestMessage request = TellmaTestHost.SpaRequest(HttpMethod.Get, "/1/api/web/probe", host.Cookie());
            using HttpResponseMessage response = await host.Client.SendAsync(request, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            CapturedEvent completed = Assert.Single(capture.Events, static captured => captured.Message.Contains("/1/api/web/probe\" responded 200", StringComparison.Ordinal));
            Assert.Equal("1", completed.Members["TenantId"]);
            Assert.False(string.IsNullOrEmpty(completed.Members["TenantCategory"]));
            Assert.Equal("sub-1", completed.Members["Subject"]);
            Assert.Equal(TellmaTestHost.UserId.ToString(CultureInfo.InvariantCulture), completed.Members["UserId"]);
        }

        [Fact]
        public void A_database_span_keeps_no_query_text()
        {
            using Activity activity = new("sql");
            _ = activity.SetTag("db.query.text", "SELECT * FROM [Users] WHERE [Email] = 'a@b.c'");
            _ = activity.SetTag("db.query.summary", "SELECT Users");
            _ = activity.SetTag("db.statement", "SELECT 1");
            _ = activity.SetTag("db.system.name", "microsoft.sql_server");

            ObservabilityComposition.RemoveQueryText(activity);

            Assert.Null(activity.GetTagItem("db.query.text"));
            Assert.Null(activity.GetTagItem("db.query.summary"));
            Assert.Null(activity.GetTagItem("db.statement"));
            Assert.Equal("microsoft.sql_server", activity.GetTagItem("db.system.name"));
        }
    }
}
