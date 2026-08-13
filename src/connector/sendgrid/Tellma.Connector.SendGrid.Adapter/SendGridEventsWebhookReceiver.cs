// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>
    ///     Receives SendGrid's event webhook: verify the signature over the raw bytes, translate,
    ///     dispatch.
    /// </summary>
    /// <remarks>
    ///     Registered whenever verification keys are configured, independent of whether SendGrid is
    ///     the active send transport — so a deployment migrating to another transport keeps accepting
    ///     the tail of its in-flight events instead of losing them.
    /// </remarks>
    internal sealed class SendGridEventsWebhookReceiver : IWebhookReceiver
    {
        /// <summary>The route segment this receiver answers on.</summary>
        internal const string ReceiverKey = "sendgrid-events";

        private readonly SendGridWebhookVerifier _verifier;
        private readonly IEmailDeliveryEventDispatcher _dispatcher;
        private readonly DeploymentIdentity _deployment;
        private readonly SendGridEmailMetrics _metrics;
        private readonly ILogger<SendGridEventsWebhookReceiver> _logger;

        /// <summary>Creates the receiver.</summary>
        /// <param name="verifier">The configured signature verifier.</param>
        /// <param name="dispatcher">Where translated events are handed off.</param>
        /// <param name="deployment">This deployment, whose id the correlation envelope is matched against.</param>
        /// <param name="metrics">Records the events this receiver drops before dispatch.</param>
        /// <param name="logger">Where drop counts are logged.</param>
        public SendGridEventsWebhookReceiver(
            SendGridWebhookVerifier verifier,
            IEmailDeliveryEventDispatcher dispatcher,
            DeploymentIdentity deployment,
            SendGridEmailMetrics metrics,
            ILogger<SendGridEventsWebhookReceiver> logger)
        {
            ArgumentNullException.ThrowIfNull(verifier);
            ArgumentNullException.ThrowIfNull(dispatcher);
            ArgumentNullException.ThrowIfNull(deployment);
            ArgumentNullException.ThrowIfNull(metrics);
            ArgumentNullException.ThrowIfNull(logger);

            _verifier = verifier;
            _dispatcher = dispatcher;
            _deployment = deployment;
            _metrics = metrics;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Key => ReceiverKey;

        /// <inheritdoc />
        public async Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            // SendGrid performs no GET handshake, so nothing else is meaningful here.
            if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
            {
                return new WebhookResult(WebhookOutcome.Invalid, "Only POST is accepted.");
            }

            if (!TryGetHeader(request, SendGridWebhookVerifier.SignatureHeaderName, out string? signature)
                || !TryGetHeader(request, SendGridWebhookVerifier.TimestampHeaderName, out string? timestamp)
                || !_verifier.Verify(request.Body.Span, timestamp, signature))
            {
                // One non-specific detail for every failure mode: a forger must learn nothing from
                // the difference between a missing header and a wrong key.
                return new WebhookResult(WebhookOutcome.Unauthorized, "Signature verification failed.");
            }

            if (!SendGridEventParser.TryParse(request.Body.Span, out IReadOnlyList<SendGridEvent> events))
            {
                return new WebhookResult(WebhookOutcome.Invalid, "The payload is not a JSON array of events.");
            }

            List<EmailDeliveryEvent> translated = new(events.Count);
            int foreign = 0;

            foreach (SendGridEvent @event in events)
            {
                EmailDeliveryEventType type = ClassifyEvent(@event.EventName);

                if (!TryResolveCorrelation(@event, out EmailCorrelation? correlation, out bool isForeign))
                {
                    if (isForeign)
                    {
                        // Dropped per event, not per batch: under shared provider credentials one
                        // batch legitimately mixes deployments, and dropping it whole would lose
                        // this deployment's own events.
                        foreign++;
                        _metrics.RecordForeignEvent(type);
                        continue;
                    }
                }

                translated.Add(new EmailDeliveryEvent(
                    correlation,
                    @event.Email,
                    type,
                    @event.EventName,
                    @event.Reason,
                    @event.Timestamp,
                    @event.EventId));
            }

            if (foreign > 0)
            {
                SendGridEmailLog.ForeignEventsDropped(_logger, foreign);
            }

            try
            {
                await _dispatcher.DispatchAsync(
                    SendGridEmailServiceCollectionExtensions.TransportName, translated, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // SendGrid redelivers a failed batch for up to 24 hours, which is the only durable
                // retry that exists at this tier; handler-side deduplication makes it harmless.
                return new WebhookResult(WebhookOutcome.TransientFailure, exception.Message);
            }

            return new WebhookResult(WebhookOutcome.Accepted);
        }

        /// <summary>Maps a SendGrid event name onto the platform classification.</summary>
        /// <param name="eventName">SendGrid's own event name.</param>
        /// <returns>The platform type; unknown and unsubscribe-family names map to
        ///     <see cref="EmailDeliveryEventType.Other" />, where the raw name still carries the meaning.</returns>
        internal static EmailDeliveryEventType ClassifyEvent(string eventName)
        {
            return eventName switch
            {
                "delivered" => EmailDeliveryEventType.Delivered,
                "deferred" => EmailDeliveryEventType.Deferred,
                "bounce" => EmailDeliveryEventType.Bounced,
                "dropped" => EmailDeliveryEventType.Dropped,
                "open" => EmailDeliveryEventType.Opened,
                "click" => EmailDeliveryEventType.Clicked,
                "spamreport" => EmailDeliveryEventType.SpamReported,
                _ => EmailDeliveryEventType.Other,
            };
        }

        private bool TryResolveCorrelation(
            SendGridEvent @event, out EmailCorrelation? correlation, out bool isForeign)
        {
            correlation = null;
            isForeign = false;

            if (!@event.CustomArgs.TryGetValue(SendGridPayloadMapper.CorrelationCustomArg, out string? envelope)
                || string.IsNullOrEmpty(envelope))
            {
                // SendGrid documents that delayed bounces can arrive without the send's metadata, so
                // an uncorrelated event is expected background rather than an anomaly.
                return false;
            }

            // The deployment id is colon-free by construction, so the first colon splits the envelope
            // from the correlation unambiguously.
            int separator = envelope.IndexOf(':');
            if (separator < 0)
            {
                return false;
            }

            if (!envelope.AsSpan(0, separator).SequenceEqual(_deployment.DeploymentId))
            {
                isForeign = true;
                return false;
            }

            return EmailCorrelation.TryParse(envelope[(separator + 1)..], out correlation);
        }

        private static bool TryGetHeader(WebhookRequest request, string name, out string value)
        {
            if (request.Headers.TryGetValue(name, out IReadOnlyList<string>? values)
                && values.Count > 0
                && !string.IsNullOrEmpty(values[0]))
            {
                value = values[0];
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
