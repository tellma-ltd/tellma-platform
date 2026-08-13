// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Messaging.EventGrid;
using Azure.Messaging.EventGrid.SystemEvents;
using Microsoft.Extensions.Logging;
using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>
    ///     Receives the Event Grid subscription that carries ACS Email delivery and engagement
    ///     reports.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Event Grid does not sign its deliveries, so authenticity rests on a secret in the
    ///         subscription URL's <c>token</c> query parameter, compared in constant time against the
    ///         configured accepted tokens. The webhook fronting never logs query strings, which is
    ///         what keeps that token out of the log store.
    ///     </para>
    ///     <para>
    ///         Registered whenever tokens are configured, independent of the active send transport,
    ///         so a deployment migrating away keeps draining its in-flight reports.
    ///     </para>
    /// </remarks>
    internal sealed class AcsEmailEventsWebhookReceiver : IWebhookReceiver
    {
        /// <summary>The route segment this receiver answers on.</summary>
        internal const string ReceiverKey = "acs-email-events";

        /// <summary>The query parameter carrying the shared secret.</summary>
        internal const string TokenQueryParameter = "token";

        private readonly byte[][] _tokenHashes;
        private readonly IEmailDeliveryEventDispatcher _dispatcher;
        private readonly ILogger<AcsEmailEventsWebhookReceiver> _logger;

        /// <summary>Creates the receiver.</summary>
        /// <param name="acceptedTokens">The tokens the subscription URL may present.</param>
        /// <param name="dispatcher">Where translated events are handed off.</param>
        /// <param name="logger">Where skipped-event counts are logged.</param>
        public AcsEmailEventsWebhookReceiver(
            IReadOnlyList<string> acceptedTokens,
            IEmailDeliveryEventDispatcher dispatcher,
            ILogger<AcsEmailEventsWebhookReceiver> logger)
        {
            ArgumentNullException.ThrowIfNull(acceptedTokens);
            ArgumentNullException.ThrowIfNull(dispatcher);
            ArgumentNullException.ThrowIfNull(logger);

            // Hashed once so the comparison is over fixed-length values: comparing the raw tokens in
            // constant time would still leak the configured token's length through the early
            // length-mismatch return.
            _tokenHashes = [.. acceptedTokens
                .Where(static t => !string.IsNullOrEmpty(t))
                .Select(static t => SHA256.HashData(Encoding.UTF8.GetBytes(t)))];

            _dispatcher = dispatcher;
            _logger = logger;
        }

        /// <inheritdoc />
        public string Key => ReceiverKey;

        /// <inheritdoc />
        public async Task<WebhookResult> HandleAsync(WebhookRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);

            // Subscriptions are provisioned with the EventGridEvent schema, whose validation
            // handshake is a POST; the CloudEvents OPTIONS handshake is not used.
            if (!string.Equals(request.Method, "POST", StringComparison.Ordinal))
            {
                return new WebhookResult(WebhookOutcome.Invalid, "Only POST is accepted.");
            }

            if (!IsTokenAccepted(request))
            {
                return new WebhookResult(WebhookOutcome.Unauthorized, "The subscription token was absent or unrecognized.");
            }

            EventGridEvent[] events;
            try
            {
                events = EventGridEvent.ParseMany(BinaryData.FromBytes(request.Body));
            }
            catch (Exception exception) when (exception is JsonException or ArgumentException)
            {
                return new WebhookResult(WebhookOutcome.Invalid, "The payload is not an Event Grid batch.");
            }

            List<EmailDeliveryEvent> translated = new(events.Length);
            int skipped = 0;

            foreach (EventGridEvent gridEvent in events)
            {
                if (!gridEvent.TryGetSystemEventData(out object systemEvent))
                {
                    skipped++;
                    continue;
                }

                switch (systemEvent)
                {
                    case SubscriptionValidationEventData validation:
                        // The one place this receiver answers with a body: Event Grid completes the
                        // subscription only when the code is echoed back as JSON.
                        return new WebhookResult(
                            WebhookOutcome.Accepted,
                            "Subscription validation handshake.",
                            BuildValidationResponse(validation.ValidationCode),
                            "application/json");

                    case AcsEmailDeliveryReportReceivedEventData delivery:
                        translated.Add(TranslateDelivery(gridEvent, delivery));
                        break;

                    case AcsEmailEngagementTrackingReportReceivedEventData engagement:
                        translated.Add(TranslateEngagement(gridEvent, engagement));
                        break;

                    default:
                        // A system topic can carry more than email; anything else is not ours.
                        skipped++;
                        break;
                }
            }

            if (skipped > 0)
            {
                AcsEmailLog.NonEmailEventsSkipped(_logger, skipped);
            }

            try
            {
                await _dispatcher.DispatchAsync(
                    AcsEmailServiceCollectionExtensions.TransportName, translated, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Event Grid redelivers with backoff for up to 24 hours, which is the durable retry
                // at this tier.
                return new WebhookResult(WebhookOutcome.TransientFailure, exception.Message);
            }

            return new WebhookResult(WebhookOutcome.Accepted);
        }

        /// <summary>Maps an ACS delivery status onto the platform classification.</summary>
        /// <param name="status">The status verbatim, as ACS spelled it.</param>
        /// <returns>The platform type.</returns>
        /// <remarks>
        ///     Switched on the raw string rather than the SDK's extensible enum members: ACS documents
        ///     statuses the SDK has no member for, and an unknown status must classify as
        ///     <see cref="EmailDeliveryEventType.Other" /> rather than fail to compile against a
        ///     future SDK.
        /// </remarks>
        internal static EmailDeliveryEventType ClassifyDeliveryStatus(string? status)
        {
            // Compared case-insensitively because ACS's published samples and the SDK's own
            // extensible-enum values do not agree on casing, and a classification must not turn on
            // which of the two a given resource happens to emit.
            // "Suppressed" means the provider discarded it from its own managed suppression list.
            return status switch
            {
                _ when Matches(status, "Delivered") => EmailDeliveryEventType.Delivered,
                _ when Matches(status, "Bounced") => EmailDeliveryEventType.Bounced,
                _ when Matches(status, "Suppressed") => EmailDeliveryEventType.Dropped,
                _ when Matches(status, "Failed") => EmailDeliveryEventType.Failed,
                _ when Matches(status, "Quarantined") => EmailDeliveryEventType.Failed,
                _ when Matches(status, "FilteredSpam") => EmailDeliveryEventType.Failed,
                _ => EmailDeliveryEventType.Other,
            };
        }

        /// <summary>Maps an ACS engagement type onto the platform classification.</summary>
        /// <param name="engagement">The engagement verbatim.</param>
        /// <returns>The platform type.</returns>
        internal static EmailDeliveryEventType ClassifyEngagement(string? engagement)
        {
            return engagement switch
            {
                _ when Matches(engagement, "View") => EmailDeliveryEventType.Opened,
                _ when Matches(engagement, "Click") => EmailDeliveryEventType.Clicked,
                _ => EmailDeliveryEventType.Other,
            };
        }

        /// <summary>Writes the handshake echo Event Grid expects.</summary>
        /// <param name="validationCode">The code Event Grid sent, which is payload-controlled.</param>
        /// <returns>The response body.</returns>
        /// <remarks>
        ///     Written with a JSON writer rather than interpolated: the code arrives from the wire,
        ///     and a quote or backslash in it would produce a malformed body and a failed subscription
        ///     handshake that is near-impossible to diagnose from the Azure side.
        /// </remarks>
        private static byte[] BuildValidationResponse(string? validationCode)
        {
            ArrayBufferWriter<byte> buffer = new(128);
            using (Utf8JsonWriter writer = new(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("validationResponse", validationCode);
                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        private static bool Matches(string? value, string expected)
        {
            return string.Equals(value, expected, StringComparison.OrdinalIgnoreCase);
        }

        private static EmailDeliveryEvent TranslateDelivery(
            EventGridEvent gridEvent, AcsEmailDeliveryReportReceivedEventData delivery)
        {
            string rawStatus = delivery.Status?.ToString() ?? string.Empty;

            // An id this codec did not produce — an ACS-generated one, or mail sent outside the
            // platform — simply leaves the event uncorrelated, which the dispatcher meters.
            if (!AcsMessageIdCodec.TryParse(delivery.InternetMessageId, out EmailCorrelation? correlation))
            {
                correlation = null;
            }

            return new EmailDeliveryEvent(
                correlation,
                delivery.Recipient,
                ClassifyDeliveryStatus(rawStatus),
                rawStatus,
                delivery.DeliveryStatusDetails?.StatusMessage,
                // The Event Grid envelope's time is the fallback: the delivery timestamp is optional,
                // and public samples disagree with the SDK on its exact spelling.
                delivery.DeliveryAttemptTimestamp ?? gridEvent.EventTime,
                gridEvent.Id);
        }

        private static EmailDeliveryEvent TranslateEngagement(
            EventGridEvent gridEvent, AcsEmailEngagementTrackingReportReceivedEventData engagement)
        {
            string rawEngagement = engagement.Engagement?.ToString() ?? string.Empty;

            // Engagement events carry no internet message id in either the GA or the preview
            // contract, so they are uncorrelated by design: translated, metered, never routed.
            return new EmailDeliveryEvent(
                null,
                engagement.Recipient,
                ClassifyEngagement(rawEngagement),
                rawEngagement,
                null,
                engagement.UserActionTimestamp ?? gridEvent.EventTime,
                gridEvent.Id);
        }

        private bool IsTokenAccepted(WebhookRequest request)
        {
            if (!request.QueryParams.TryGetValue(TokenQueryParameter, out IReadOnlyList<string>? values)
                || values.Count == 0
                || string.IsNullOrEmpty(values[0]))
            {
                return false;
            }

            byte[] presented = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]));
            bool accepted = false;
            foreach (byte[] candidate in _tokenHashes)
            {
                // No early exit: every configured token is compared so the loop's duration does not
                // depend on which one matched.
                accepted |= CryptographicOperations.FixedTimeEquals(candidate, presented);
            }

            return accepted;
        }
    }
}
