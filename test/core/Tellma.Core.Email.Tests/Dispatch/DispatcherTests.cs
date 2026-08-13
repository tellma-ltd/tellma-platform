// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Testing.Email;

namespace Tellma.Core.Email.Tests.Dispatch
{
    /// <summary>
    ///     Routing delivery events is the half of the pipeline that runs on a provider's schedule, so
    ///     what it does with events that route nowhere matters as much as what it does with the rest.
    /// </summary>
    public class DispatcherTests : IDisposable
    {
        private const string Transport = "sendgrid";

        // The real factory, because MetricCollector matches on the meter's scope: a hand-rolled
        // factory that forgot to stamp it would silently collect nothing.
        private readonly ServiceProvider _metricsProvider =
            new ServiceCollection().AddMetrics().BuildServiceProvider();

        private readonly IMeterFactory _meterFactory;
        private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 5, 0, TimeSpan.Zero));
        private readonly FakeLogger<EmailDeliveryEventDispatcher> _logger = new();

        /// <summary>Creates the shared meter factory the collectors and metrics both use.</summary>
        public DispatcherTests()
        {
            _meterFactory = _metricsProvider.GetRequiredService<IMeterFactory>();
        }

        [Fact]
        public async Task Groups_by_owner_and_preserves_arrival_order_within_each_group()
        {
            RecordingHandler outbox = new("outbox");
            RecordingHandler identity = new("identity");
            EmailDeliveryEventDispatcher dispatcher = Create(outbox, identity);

            EmailCorrelation outboxCorrelation = new("outbox", "1", 3);
            EmailCorrelation identityCorrelation = new("identity", "2");

            EmailDeliveryEvent[] events =
            [
                .. DeliveryEvents.For(outboxCorrelation).Delivered().Build(),
                .. DeliveryEvents.For(identityCorrelation).Bounced().Build(),
                .. DeliveryEvents.For(outboxCorrelation).Opened().Build(),
            ];

            await dispatcher.DispatchAsync(Transport, events, TestContext.Current.CancellationToken);

            // Batch in, batch out: one call per owner, never one per event.
            IReadOnlyList<EmailDeliveryEvent> outboxBatch = Assert.Single(outbox.Batches);
            Assert.Equal(
                [EmailDeliveryEventType.Delivered, EmailDeliveryEventType.Opened],
                outboxBatch.Select(static e => e.Type));
            Assert.Equal(EmailDeliveryEventType.Bounced, Assert.Single(Assert.Single(identity.Batches)).Type);
        }

        [Fact]
        public async Task Meters_an_uncorrelated_event_instead_of_routing_it()
        {
            RecordingHandler outbox = new("outbox");
            EmailDeliveryEventDispatcher dispatcher = Create(outbox);
            using MetricCollector<long> events = DeliveryEventCollector();

            await dispatcher.DispatchAsync(
                Transport,
                DeliveryEvents.For(null).Bounced().Build(),
                TestContext.Current.CancellationToken);

            Assert.Empty(outbox.Batches);
            Assert.Equal(
                "uncorrelated",
                Assert.Single(events.GetMeasurementSnapshot()).Tags["email.event.routing"]);
        }

        [Fact]
        public async Task Warns_and_meters_an_event_whose_owner_nothing_handles()
        {
            EmailDeliveryEventDispatcher dispatcher = Create(new RecordingHandler("identity"));
            using MetricCollector<long> events = DeliveryEventCollector();

            await dispatcher.DispatchAsync(
                Transport,
                DeliveryEvents.For(new EmailCorrelation("outbox", "1")).Delivered().Build(),
                TestContext.Current.CancellationToken);

            // With foreign deployments already filtered at translation, this is a composition bug.
            Assert.Equal(
                "unknown_owner",
                Assert.Single(events.GetMeasurementSnapshot()).Tags["email.event.routing"]);
            Assert.Contains(
                _logger.Collector.GetSnapshot(),
                static r => r.Message.Contains("no registered handler owns", StringComparison.Ordinal));
        }

        [Fact]
        public async Task Records_the_arrival_lag_against_the_providers_timestamp()
        {
            EmailDeliveryEventDispatcher dispatcher = Create(new RecordingHandler("outbox"));
            using MetricCollector<double> lag = new(
                _meterFactory, EmailTelemetryNames.MeterName, EmailTelemetryNames.DeliveryEventLagInstrument);

            EmailDeliveryEvent stale = new(
                new EmailCorrelation("outbox", "1"),
                "recipient@example.com",
                EmailDeliveryEventType.Bounced,
                "bounce",
                null,
                _time.GetUtcNow() - TimeSpan.FromSeconds(90),
                "evt-1");

            await dispatcher.DispatchAsync(Transport, [stale], TestContext.Current.CancellationToken);

            Assert.Equal(90, Assert.Single(lag.GetMeasurementSnapshot()).Value);
        }

        [Fact]
        public async Task Lets_a_handler_failure_propagate_so_the_provider_redelivers()
        {
            // No event queue exists at this tier: the provider's redelivery is the only durable
            // retry, and swallowing the failure would lose the event instead.
            ThrowingHandler handler = new("outbox");
            EmailDeliveryEventDispatcher dispatcher = Create(handler);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => dispatcher.DispatchAsync(
                    Transport,
                    DeliveryEvents.For(new EmailCorrelation("outbox", "1")).Delivered().Build(),
                    TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task Does_nothing_for_an_empty_batch()
        {
            RecordingHandler outbox = new("outbox");
            EmailDeliveryEventDispatcher dispatcher = Create(outbox);

            await dispatcher.DispatchAsync(Transport, [], TestContext.Current.CancellationToken);

            Assert.Empty(outbox.Batches);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _metricsProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        private EmailDeliveryEventDispatcher Create(params IEmailDeliveryEventHandler[] handlers)
        {
            return new EmailDeliveryEventDispatcher(handlers, new EmailMetrics(_meterFactory), _time, _logger);
        }

        private MetricCollector<long> DeliveryEventCollector()
        {
            return new MetricCollector<long>(
                _meterFactory, EmailTelemetryNames.MeterName, EmailTelemetryNames.DeliveryEventsInstrument);
        }

        private sealed class RecordingHandler(string ownerKey) : IEmailDeliveryEventHandler
        {
            public string OwnerKey => ownerKey;

            public List<IReadOnlyList<EmailDeliveryEvent>> Batches { get; } = [];

            public Task HandleAsync(IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
            {
                Batches.Add(events);
                return Task.CompletedTask;
            }
        }

        private sealed class ThrowingHandler(string ownerKey) : IEmailDeliveryEventHandler
        {
            public string OwnerKey => ownerKey;

            public Task HandleAsync(IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
            {
                throw new InvalidOperationException("The row could not be updated.");
            }
        }
    }
}
