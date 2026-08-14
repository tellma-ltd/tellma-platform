// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.SendGrid.Adapter.Tests.Webhook
{
    /// <summary>
    ///     The receiver end to end, over payloads shaped the way SendGrid actually posts them.
    /// </summary>
    public class SendGridEventsWebhookReceiverTests : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ServiceProvider _metricsProvider =
            new ServiceCollection().AddMetrics().BuildServiceProvider();

        private readonly RecordingDispatcher _dispatcher = new();

        [Theory]
        [InlineData("delivered", EmailDeliveryEventType.Delivered)]
        [InlineData("deferred", EmailDeliveryEventType.Deferred)]
        [InlineData("bounce", EmailDeliveryEventType.Bounced)]
        [InlineData("dropped", EmailDeliveryEventType.Dropped)]
        [InlineData("open", EmailDeliveryEventType.Opened)]
        [InlineData("click", EmailDeliveryEventType.Clicked)]
        [InlineData("spamreport", EmailDeliveryEventType.SpamReported)]
        [InlineData("processed", EmailDeliveryEventType.Other)]
        [InlineData("group_unsubscribe", EmailDeliveryEventType.Other)]
        [InlineData("some_future_event", EmailDeliveryEventType.Other)]
        public async Task Classifies_each_documented_event_name(string eventName, EmailDeliveryEventType expected)
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            string body = $$"""
                [{"event":"{{eventName}}","sg_event_id":"evt-1","email":"r@example.com","timestamp":1767225600,
                  "tellma_correlation":"etpharma:outbox:3:42"}]
                """;

            WebhookResult result = await receiver.HandleAsync(
                Signed(body), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Accepted, result.Outcome);
            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));
            Assert.Equal(expected, @event.Type);

            // The provider's own name is kept verbatim — for an unclassified event it is the only
            // meaning the event has.
            Assert.Equal(eventName, @event.RawType);
        }

        [Fact]
        public async Task Translates_the_whole_event()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            const string Body = /*lang=json,strict*/ """
                [{"event":"bounce","sg_event_id":"evt-9","sg_message_id":"msg-9","email":"r@example.com",
                  "timestamp":1767225600,"reason":"550 unknown user","tellma_correlation":"etpharma:outbox:3:42"}]
                """;

            await receiver.HandleAsync(Signed(Body), TestContext.Current.CancellationToken);

            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));
            Assert.Equal("r@example.com", @event.Recipient);
            Assert.Equal("550 unknown user", @event.Reason);
            Assert.Equal("evt-9", @event.ProviderEventId);
            Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1767225600), @event.Timestamp);
            Assert.Equal(new EmailCorrelation("outbox", "42", 3), @event.Correlation);
            Assert.Equal("sendgrid", _dispatcher.Transports.Single());
        }

        [Fact]
        public async Task Passes_an_uncorrelated_event_through_for_metering()
        {
            // SendGrid documents that delayed bounces can arrive without the send's metadata, so an
            // uncorrelated event is background rather than an anomaly.
            SendGridEventsWebhookReceiver receiver = Receiver();
            const string Body = /*lang=json,strict*/ """[{"event":"bounce","sg_event_id":"evt-1","email":"r@example.com"}]""";

            await receiver.HandleAsync(Signed(Body), TestContext.Current.CancellationToken);

            Assert.Null(Assert.Single(Assert.Single(_dispatcher.Batches)).Correlation);
        }

        [Fact]
        public async Task Drops_and_meters_an_event_belonging_to_another_deployment()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            using MetricCollector<long> events = new(
                _metricsProvider.GetRequiredService<IMeterFactory>(),
                EmailTelemetryNames.MeterName,
                EmailTelemetryNames.DeliveryEventsInstrument);

            const string Body = /*lang=json,strict*/ """
                [{"event":"delivered","sg_event_id":"evt-1","tellma_correlation":"othershop:outbox:1:7"},
                 {"event":"delivered","sg_event_id":"evt-2","tellma_correlation":"etpharma:outbox:1:7"}]
                """;

            WebhookResult result = await receiver.HandleAsync(Signed(Body), TestContext.Current.CancellationToken);

            // Dropped per event, not per batch: under shared credentials one batch legitimately
            // mixes deployments, and dropping it whole would lose our own events.
            Assert.Equal(WebhookOutcome.Accepted, result.Outcome);
            Assert.Equal("evt-2", Assert.Single(Assert.Single(_dispatcher.Batches)).ProviderEventId);

            CollectedMeasurement<long> dropped = Assert.Single(events.GetMeasurementSnapshot());
            Assert.Equal(EmailTelemetryNames.ForeignEvent, dropped.Tags[EmailTelemetryNames.EventRoutingTag]);

            // The event-type spelling too, not just the routing: this measurement lands on the same
            // instrument the pipeline emits its other routing slices on, so an adapter that spelled
            // this dimension its own way would split one instrument in two without failing anything.
            Assert.Equal("delivered", dropped.Tags[EmailTelemetryNames.EventTypeTag]);
        }

        [Fact]
        public async Task Refuses_a_request_with_no_signature_headers()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            WebhookRequest request = new(
                "POST", Headers(), Empty(), Encoding.UTF8.GetBytes("[]"));

            WebhookResult result = await receiver.HandleAsync(request, TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Unauthorized, result.Outcome);
            Assert.Empty(_dispatcher.Batches);
        }

        [Fact]
        public async Task Refuses_a_forged_signature_with_a_detail_that_gives_nothing_away()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            WebhookRequest tampered = Signed("[]") with { Body = Encoding.UTF8.GetBytes(/*lang=json,strict*/ """[{"event":"x"}]""") };

            WebhookResult result = await receiver.HandleAsync(tampered, TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Unauthorized, result.Outcome);
            Assert.Equal("Signature verification failed.", result.Detail);
        }

        [Fact]
        public async Task Refuses_a_method_other_than_post()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();
            WebhookRequest request = Signed("[]") with { Method = "GET" };

            Assert.Equal(
                WebhookOutcome.Invalid,
                (await receiver.HandleAsync(request, TestContext.Current.CancellationToken)).Outcome);
        }

        [Fact]
        public async Task Rejects_a_verified_payload_that_is_not_an_event_array()
        {
            SendGridEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Signed("{ not json"), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Invalid, result.Outcome);
        }

        [Fact]
        public async Task Reports_a_dispatch_failure_as_transient_so_sendgrid_redelivers()
        {
            _dispatcher.ThrowOnDispatch = new InvalidOperationException("the database is down");
            SendGridEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Signed(/*lang=json,strict*/ """[{"event":"delivered","sg_event_id":"evt-1"}]"""),
                TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.TransientFailure, result.Outcome);
        }

        [Fact]
        public async Task Reports_a_handler_timeout_on_a_live_token_as_transient_rather_than_letting_it_escape()
        {
            // A handler's own timeout surfaces as a TaskCanceledException even though nobody
            // cancelled this request. It is an ordinary transient failure, not a cancellation, and
            // must be reported as one — letting it escape would reach the fronting as an unhandled
            // receiver crash, answering 500 and metering `error` for a routine database timeout.
            _dispatcher.ThrowOnDispatch = new TaskCanceledException("the handler's own timeout elapsed");
            SendGridEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Signed(/*lang=json,strict*/ """[{"event":"delivered","sg_event_id":"evt-1"}]"""),
                TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.TransientFailure, result.Outcome);
        }

        [Fact]
        public async Task Propagates_cancellation_when_the_caller_really_did_abandon_the_request()
        {
            // The other side of the same filter: once the caller's token is cancelled there is no
            // response worth composing, so the cancellation propagates instead of being reported.
            _dispatcher.ThrowOnDispatch = new OperationCanceledException("the caller went away");
            SendGridEventsWebhookReceiver receiver = Receiver();

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => receiver.HandleAsync(
                    Signed(/*lang=json,strict*/ """[{"event":"delivered","sg_event_id":"evt-1"}]"""),
                    cancelled.Token));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _key.Dispose();
            _metricsProvider.Dispose();
            GC.SuppressFinalize(this);
        }

        private SendGridEventsWebhookReceiver Receiver()
        {
            return new SendGridEventsWebhookReceiver(
                new SendGridWebhookVerifier([Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo())]),
                _dispatcher,
                new DeploymentIdentity("etpharma", "Production"),
                new SendGridEmailMetrics(_metricsProvider.GetRequiredService<IMeterFactory>()),
                NullLogger<SendGridEventsWebhookReceiver>.Instance);
        }

        private WebhookRequest Signed(string body)
        {
            const string Timestamp = "1767225600";
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            byte[] payload = [.. Encoding.UTF8.GetBytes(Timestamp), .. bytes];
            string signature = Convert.ToBase64String(
                _key.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence));

            return new WebhookRequest(
                "POST",
                Headers(
                    (SendGridWebhookVerifier.SignatureHeaderName, signature),
                    (SendGridWebhookVerifier.TimestampHeaderName, Timestamp)),
                Empty(),
                bytes);
        }

        private static Dictionary<string, IReadOnlyList<string>> Headers(
            params (string Name, string Value)[] headers)
        {
            Dictionary<string, IReadOnlyList<string>> result = new(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, string value) in headers)
            {
                result[name] = [value];
            }

            return result;
        }

        private static Dictionary<string, IReadOnlyList<string>> Empty()
        {
            return new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class RecordingDispatcher : IEmailDeliveryEventDispatcher
        {
            public List<IReadOnlyList<EmailDeliveryEvent>> Batches { get; } = [];

            public List<string> Transports { get; } = [];

            public Exception? ThrowOnDispatch { get; set; }

            public Task DispatchAsync(
                string transport, IReadOnlyList<EmailDeliveryEvent> events, CancellationToken cancellationToken)
            {
                if (ThrowOnDispatch is Exception failure)
                {
                    throw failure;
                }

                Transports.Add(transport);
                Batches.Add(events);
                return Task.CompletedTask;
            }
        }
    }
}
