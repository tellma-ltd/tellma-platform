// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Webhooks;
using Tellma.Core.Webhooks.Tests.Infrastructure;

namespace Tellma.Core.Webhooks.Tests.Observability
{
    /// <summary>
    ///     The webhook instruments, including the outcomes no receiver ever sees — an unknown key and
    ///     an oversized body are exactly the events an operator needs to be able to count.
    /// </summary>
    public class WebhookTelemetryTests
    {
        [Fact]
        public async Task Counts_a_handled_request_under_the_receivers_key()
        {
            StubWebhookReceiver receiver = new(
                "sendgrid-events", _ => new WebhookResult(WebhookOutcome.Unauthorized));
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);
            using MetricCollector<long> requests = Collector(host);

            using HttpResponseMessage response = await host.Client.PostAsync(
                new Uri("/api/webhooks/sendgrid-events", UriKind.Relative),
                new StringContent("{}"),
                TestContext.Current.CancellationToken);

            CollectedMeasurement<long> measurement = Assert.Single(requests.GetMeasurementSnapshot());
            Assert.Equal("sendgrid-events", measurement.Tags[WebhookTelemetryNames.KeyTag]);
            Assert.Equal(
                WebhookTelemetryNames.UnauthorizedOutcome, measurement.Tags[WebhookTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task Counts_an_unknown_key_under_a_literal_so_scanners_cannot_inflate_the_dimension()
        {
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([new StubWebhookReceiver("stub")]);
            using MetricCollector<long> requests = Collector(host);

            using HttpResponseMessage response = await host.Client.PostAsync(
                new Uri("/api/webhooks/wp-login", UriKind.Relative),
                new StringContent("{}"),
                TestContext.Current.CancellationToken);

            CollectedMeasurement<long> measurement = Assert.Single(requests.GetMeasurementSnapshot());
            Assert.Equal(WebhookTelemetryNames.UnknownKeyTagValue, measurement.Tags[WebhookTelemetryNames.KeyTag]);
            Assert.Equal(
                WebhookTelemetryNames.UnknownKeyOutcome, measurement.Tags[WebhookTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task Counts_an_oversized_body_that_was_never_dispatched()
        {
            await using WebhookTestHost host = await WebhookTestHost.StartAsync(
                [new StubWebhookReceiver("stub")],
                new Dictionary<string, string?> { ["Webhooks:MaxRequestBodyBytes"] = "1024" });

            using MetricCollector<long> requests = Collector(host);

            using HttpResponseMessage response = await host.Client.PostAsync(
                new Uri("/api/webhooks/stub", UriKind.Relative),
                new StringContent(new string('x', 4096)),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                WebhookTelemetryNames.TooLargeOutcome,
                Assert.Single(requests.GetMeasurementSnapshot()).Tags[WebhookTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task Counts_a_receiver_that_threw_as_an_error()
        {
            StubWebhookReceiver receiver = new("stub", _ => throw new InvalidOperationException("boom"));
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([receiver]);
            using MetricCollector<long> requests = Collector(host);

            using HttpResponseMessage response = await host.Client.PostAsync(
                new Uri("/api/webhooks/stub", UriKind.Relative),
                new StringContent("{}"),
                TestContext.Current.CancellationToken);

            Assert.Equal(
                WebhookTelemetryNames.ErrorOutcome,
                Assert.Single(requests.GetMeasurementSnapshot()).Tags[WebhookTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task Times_every_request_it_counts()
        {
            await using WebhookTestHost host = await WebhookTestHost.StartAsync([new StubWebhookReceiver("stub")]);
            using MetricCollector<double> duration = new(
                host.Services.GetRequiredService<IMeterFactory>(),
                WebhookTelemetryNames.MeterName,
                WebhookTelemetryNames.RequestDurationInstrument);

            using HttpResponseMessage response = await host.Client.PostAsync(
                new Uri("/api/webhooks/stub", UriKind.Relative),
                new StringContent("{}"),
                TestContext.Current.CancellationToken);

            Assert.Single(duration.GetMeasurementSnapshot());
        }

        private static MetricCollector<long> Collector(WebhookTestHost host)
        {
            return new MetricCollector<long>(
                host.Services.GetRequiredService<IMeterFactory>(),
                WebhookTelemetryNames.MeterName,
                WebhookTelemetryNames.RequestsInstrument);
        }
    }
}
