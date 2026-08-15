// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Webhook
{
    /// <summary>
    ///     The Event Grid receiver, over payloads recorded in the shapes Azure actually posts —
    ///     including the subscription handshake, which is the one call that must answer with a body.
    /// </summary>
    public class AcsEmailEventsWebhookReceiverTests
    {
        private const string Token = "a-subscription-token";

        private readonly RecordingDispatcher _dispatcher = new();

        [Fact]
        public async Task Echoes_the_validation_code_so_the_subscription_can_be_created()
        {
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Request(Payload("subscription-validation.json")), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Accepted, result.Outcome);
            Assert.Equal("application/json", result.ResponseContentType);
            Assert.Equal(
                /*lang=json,strict*/"""{"validationResponse":"512d38b6-c7b8-40c8-89fe-f46f9e9622b6"}""",
                Encoding.UTF8.GetString(result.ResponseBody!.Value.Span));

            // A handshake is not an email event.
            Assert.Empty(_dispatcher.Batches);
        }

        [Fact]
        public async Task Correlates_a_delivery_report_from_the_message_id_it_echoes_back()
        {
            EmailCorrelation correlation = new("outbox", "42", 3);
            Assert.True(AcsMessageIdCodec.TryEncode(correlation, "tellma.com", out string? messageId));

            AcsEmailEventsWebhookReceiver receiver = Receiver();
            string payload = Payload("delivery-report-delivered.json").Replace("__MESSAGE_ID__", messageId, StringComparison.Ordinal);

            WebhookResult result = await receiver.HandleAsync(Request(payload), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Accepted, result.Outcome);
            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));

            Assert.Equal(correlation, @event.Correlation);
            Assert.Equal(EmailDeliveryEventType.Delivered, @event.Type);
            Assert.Equal("Delivered", @event.RawType);
            Assert.Equal("recipient@example.com", @event.Recipient);
            Assert.Equal("DestinationMailboxFull", @event.Reason);

            // The Event Grid event id is stable across redeliveries, which is what makes it the
            // dedupe key rather than anything ACS puts in the payload.
            Assert.Equal("5b9b4a2e-8a0f-4d3b-9f9b-1f2f5a1c6f11", @event.ProviderEventId);
            Assert.Equal("acs-email", _dispatcher.Transports.Single());
        }

        [Fact]
        public async Task Falls_back_to_the_envelope_time_when_the_delivery_timestamp_is_absent()
        {
            // Microsoft's own published samples spell this field differently from the SDK's
            // deserializer, so the envelope time is what covers the spelling mismatch. It only
            // covers that case — an envelope carrying no time of its own is the sibling case below.
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            await receiver.HandleAsync(
                Request(Payload("delivery-report-no-timestamp.json")), TestContext.Current.CancellationToken);

            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));
            Assert.Equal(
                new DateTimeOffset(2026, 1, 1, 0, 2, 0, TimeSpan.Zero), @event.Timestamp);

            // An ACS-generated message id is not one this codec produced, so the event is
            // uncorrelated and flows through the pipeline's metering.
            Assert.Null(@event.Correlation);
            Assert.Equal(EmailDeliveryEventType.Bounced, @event.Type);
        }

        [Fact]
        public async Task Leaves_the_timestamp_unknown_when_the_envelope_carries_no_time_either()
        {
            // EventGridEvent.EventTime is a non-nullable DateTimeOffset and ParseMany does not
            // require the field, so an envelope without one deserializes to 0001-01-01 rather than
            // to anything absent. Substituting that reaches the lag histogram as two millennia of
            // arrival delay — far worse than a missing sample, and enough on its own to trip the
            // p95 alert. The translator has to recognize it as "no time" and say so.
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            await receiver.HandleAsync(
                Request(Payload("delivery-report-no-envelope-time.json")), TestContext.Current.CancellationToken);

            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));
            Assert.Null(@event.Timestamp);

            // Still a real event, still translated and routed — only the lag is unknown.
            Assert.Equal(EmailDeliveryEventType.Bounced, @event.Type);
        }

        [Fact]
        public async Task Translates_an_engagement_report_as_uncorrelated_by_design()
        {
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            await receiver.HandleAsync(
                Request(Payload("engagement-report.json")), TestContext.Current.CancellationToken);

            EmailDeliveryEvent @event = Assert.Single(Assert.Single(_dispatcher.Batches));

            Assert.Equal(EmailDeliveryEventType.Clicked, @event.Type);

            // Engagement events carry no internet message id in either contract, so they are
            // uncorrelated by design — and they omit the recipient on multi-recipient mail.
            Assert.Null(@event.Correlation);
            Assert.Null(@event.Recipient);
            Assert.Null(@event.Reason);
        }

        [Theory]
        [InlineData("Delivered", EmailDeliveryEventType.Delivered)]
        [InlineData("delivered", EmailDeliveryEventType.Delivered)]
        [InlineData("Bounced", EmailDeliveryEventType.Bounced)]
        [InlineData("Suppressed", EmailDeliveryEventType.Dropped)]
        [InlineData("Failed", EmailDeliveryEventType.Failed)]
        [InlineData("Quarantined", EmailDeliveryEventType.Failed)]
        [InlineData("FilteredSpam", EmailDeliveryEventType.Failed)]
        // Documented by ACS but absent from the SDK's enum, which is why the raw string is switched on.
        [InlineData("Expanded", EmailDeliveryEventType.Other)]
        [InlineData("SomeFutureStatus", EmailDeliveryEventType.Other)]
        public void Classifies_each_delivery_status(string status, EmailDeliveryEventType expected)
        {
            Assert.Equal(expected, AcsEmailEventsWebhookReceiver.ClassifyDeliveryStatus(status));
        }

        [Theory]
        [InlineData("View", EmailDeliveryEventType.Opened)]
        [InlineData("view", EmailDeliveryEventType.Opened)]
        [InlineData("Click", EmailDeliveryEventType.Clicked)]
        [InlineData("click", EmailDeliveryEventType.Clicked)]
        [InlineData("SomeFutureEngagement", EmailDeliveryEventType.Other)]
        public void Classifies_each_engagement_type(string engagement, EmailDeliveryEventType expected)
        {
            Assert.Equal(expected, AcsEmailEventsWebhookReceiver.ClassifyEngagement(engagement));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("the-wrong-token")]
        public async Task Refuses_a_request_whose_token_is_absent_or_unrecognized(string? token)
        {
            // Event Grid does not sign its deliveries, so this shared secret is the only
            // authentication the endpoint has.
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Request(Payload("subscription-validation.json"), token), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Unauthorized, result.Outcome);
        }

        [Fact]
        public async Task Accepts_any_configured_token_so_one_can_be_rotated()
        {
            AcsEmailEventsWebhookReceiver receiver = new(
                ["old-token", "new-token"], _dispatcher, NullLogger<AcsEmailEventsWebhookReceiver>.Instance);

            foreach (string token in new[] { "old-token", "new-token" })
            {
                WebhookResult result = await receiver.HandleAsync(
                    Request(Payload("engagement-report.json"), token), TestContext.Current.CancellationToken);

                Assert.Equal(WebhookOutcome.Accepted, result.Outcome);
            }
        }

        [Fact]
        public async Task Refuses_a_method_other_than_post()
        {
            AcsEmailEventsWebhookReceiver receiver = Receiver();
            WebhookRequest request = Request(Payload("engagement-report.json")) with { Method = "GET" };

            Assert.Equal(
                WebhookOutcome.Invalid,
                (await receiver.HandleAsync(request, TestContext.Current.CancellationToken)).Outcome);
        }

        [Fact]
        public async Task Rejects_a_payload_that_is_not_an_event_grid_batch()
        {
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Request("{ not json"), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Invalid, result.Outcome);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-a-date")]
        [InlineData("2026-13-45T99:99:99Z")]
        public async Task Rejects_a_batch_whose_envelope_carries_an_unparsable_date(string eventTime)
        {
            // Well-formed JSON in the right shape, with one field the SDK cannot turn into a date.
            // That is a malformed payload like any other and must answer Invalid — letting it throw
            // would report a caller's bad request as this receiver crashing.
            AcsEmailEventsWebhookReceiver receiver = Receiver();
            string payload = /*lang=json,strict*/ """
                [{"id":"1","topic":"t","subject":"s","data":{},
                  "eventType":"Microsoft.Communication.EmailDeliveryReportReceived",
                  "dataVersion":"1.0","eventTime":"@@"}]
                """.Replace("@@", eventTime, StringComparison.Ordinal);

            WebhookResult result = await receiver.HandleAsync(
                Request(payload), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.Invalid, result.Outcome);
            Assert.Empty(_dispatcher.Batches);
        }

        [Fact]
        public async Task Reports_a_dispatch_failure_as_transient_so_event_grid_redelivers()
        {
            _dispatcher.ThrowOnDispatch = new InvalidOperationException("the database is down");
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Request(Payload("engagement-report.json")), TestContext.Current.CancellationToken);

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
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            WebhookResult result = await receiver.HandleAsync(
                Request(Payload("engagement-report.json")), TestContext.Current.CancellationToken);

            Assert.Equal(WebhookOutcome.TransientFailure, result.Outcome);
        }

        [Fact]
        public async Task Propagates_cancellation_when_the_caller_really_did_abandon_the_request()
        {
            // The other side of the same filter: once the caller's token is cancelled there is no
            // response worth composing, so the cancellation propagates instead of being reported.
            _dispatcher.ThrowOnDispatch = new OperationCanceledException("the caller went away");
            AcsEmailEventsWebhookReceiver receiver = Receiver();

            using CancellationTokenSource cancelled = new();
            await cancelled.CancelAsync();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => receiver.HandleAsync(Request(Payload("engagement-report.json")), cancelled.Token));
        }

        private AcsEmailEventsWebhookReceiver Receiver()
        {
            return new AcsEmailEventsWebhookReceiver(
                [Token], _dispatcher, NullLogger<AcsEmailEventsWebhookReceiver>.Instance);
        }

        private static string Payload(string fileName)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhook", "RecordedPayloads", fileName));
        }

        private static WebhookRequest Request(string body, string? token = Token)
        {
            Dictionary<string, IReadOnlyList<string>> query = new(StringComparer.OrdinalIgnoreCase);
            if (token is not null)
            {
                query[AcsEmailEventsWebhookReceiver.TokenQueryParameter] = [token];
            }

            return new WebhookRequest(
                "POST",
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase),
                query,
                Encoding.UTF8.GetBytes(body));
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
