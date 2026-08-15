// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>
    ///     The email pipeline's instruments. Emitted by the pipeline rather than by the adapters, so
    ///     every transport is measured identically; the one slice adapters emit themselves is the
    ///     foreign-deployment delivery-event count, which never reaches the dispatcher.
    /// </summary>
    /// <remarks>
    ///     Recipient addresses, subjects, and body content never appear in any of these tags. Every
    ///     dimension is a small closed set except the owner key, which is bounded by the number of
    ///     registered handlers — an event naming an unregistered key is tagged with a literal rather
    ///     than with what arrived on the wire, so a forged correlation cannot inflate it. Tenant id is
    ///     deliberately absent: it multiplies every other dimension for a question the structured
    ///     logs answer better.
    /// </remarks>
    internal sealed class EmailMetrics
    {
        private readonly Counter<long> _sentMessages;
        private readonly Histogram<double> _sendDuration;
        private readonly Histogram<int> _sendBatchSize;
        private readonly Counter<long> _deliveryEvents;
        private readonly Histogram<double> _deliveryEventLag;

        /// <summary>Creates the instruments on the shared email meter.</summary>
        /// <param name="meterFactory">The host's meter factory. Instruments created through it are
        ///     what test collectors and OpenTelemetry pipelines can actually observe.</param>
        public EmailMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            // The meter stays a local: IMeterFactory owns its lifetime, so holding it in a field
            // would make this type look like it owns a disposable it must never dispose.
            Meter meter = meterFactory.Create(EmailTelemetryNames.MeterName);

            _sentMessages = meter.CreateCounter<long>(
                EmailTelemetryNames.SentMessagesInstrument,
                EmailTelemetryNames.MessageUnit,
                "Messages leaving the email pipeline, by outcome, audience, and wire mechanism.");

            _sendDuration = meter.CreateHistogram<double>(
                EmailTelemetryNames.SendDurationInstrument,
                EmailTelemetryNames.SecondUnit,
                "Wall-clock duration of one transport call.");

            _sendBatchSize = meter.CreateHistogram<int>(
                EmailTelemetryNames.SendBatchSizeInstrument,
                EmailTelemetryNames.MessageUnit,
                "Number of messages carried by one transport call.");

            _deliveryEvents = meter.CreateCounter<long>(
                EmailTelemetryNames.DeliveryEventsInstrument,
                EmailTelemetryNames.EventUnit,
                "Inbound provider delivery events, by type and routing outcome.");

            _deliveryEventLag = meter.CreateHistogram<double>(
                EmailTelemetryNames.DeliveryEventLagInstrument,
                EmailTelemetryNames.SecondUnit,
                "Webhook arrival time minus the event's provider timestamp.");
        }

        /// <summary>Records one message's outcome.</summary>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="outcome">What happened to the message.</param>
        /// <param name="audience">Whose inbox the message targeted.</param>
        /// <param name="delivery">The wire mechanism: live, sandbox, or withheld.</param>
        /// <param name="ownerKey">The correlation's owner key, or null when uncorrelated.</param>
        /// <param name="tenantIsSandbox">Whether the ambient tenant is a sandbox.</param>
        public void RecordSentMessage(
            string transport,
            EmailSendOutcome outcome,
            EmailAudience audience,
            string delivery,
            string? ownerKey,
            bool tenantIsSandbox)
        {
            // TagList's inline capacity is eight, so a six-tag measurement stays off the heap.
            TagList tags = new()
            {
                { EmailTelemetryNames.TransportTag, transport },
                { EmailTelemetryNames.OutcomeTag, EmailDiagnostics.ToTagValue(outcome) },
                { EmailTelemetryNames.AudienceTag, EmailDiagnostics.ToTagValue(audience) },
                { EmailTelemetryNames.DeliveryTag, delivery },
                { EmailTelemetryNames.OwnerTag, ownerKey ?? EmailTelemetryNames.NoOwner },
                {
                    EmailTelemetryNames.TenantCategoryTag,
                    tenantIsSandbox
                        ? EmailTelemetryNames.SandboxTenantCategory
                        : EmailTelemetryNames.LiveTenantCategory
                },
            };

            _sentMessages.Add(1, tags);
        }

        /// <summary>Records how long one transport call took.</summary>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="seconds">The elapsed time.</param>
        /// <param name="errorType">The exception type name when the call threw, else null.</param>
        public void RecordSendDuration(string transport, double seconds, string? errorType)
        {
            if (errorType is null)
            {
                _sendDuration.Record(seconds, new KeyValuePair<string, object?>(EmailTelemetryNames.TransportTag, transport));
                return;
            }

            _sendDuration.Record(
                seconds,
                new KeyValuePair<string, object?>(EmailTelemetryNames.TransportTag, transport),
                new KeyValuePair<string, object?>(EmailTelemetryNames.ErrorTypeTag, errorType));
        }

        /// <summary>Records how many messages one transport call carried.</summary>
        /// <param name="transport">The active transport's configuration name.</param>
        /// <param name="count">The message count.</param>
        public void RecordSendBatchSize(string transport, int count)
        {
            _sendBatchSize.Record(count, new KeyValuePair<string, object?>(EmailTelemetryNames.TransportTag, transport));
        }

        /// <summary>Records one inbound delivery event and what the pipeline did with it.</summary>
        /// <param name="transport">The transport whose receiver produced the event.</param>
        /// <param name="type">The platform classification of the event.</param>
        /// <param name="routing">Routed, uncorrelated, or unknown-owner.</param>
        /// <param name="ownerKey">The correlation's owner key, or null when uncorrelated.</param>
        public void RecordDeliveryEvent(
            string transport, EmailDeliveryEventType type, string routing, string? ownerKey)
        {
            TagList tags = new()
            {
                { EmailTelemetryNames.TransportTag, transport },
                { EmailTelemetryNames.EventTypeTag, EmailDiagnostics.ToTagValue(type) },
                { EmailTelemetryNames.EventRoutingTag, routing },
                { EmailTelemetryNames.OwnerTag, ownerKey ?? EmailTelemetryNames.NoOwner },
            };

            _deliveryEvents.Add(1, tags);
        }

        /// <summary>Records how far behind the provider's timestamp an event arrived.</summary>
        /// <remarks>
        ///     Called only for events that carried a provider timestamp, so the histogram's count is
        ///     a count of measurable events rather than of all of them.
        /// </remarks>
        /// <param name="transport">The transport whose receiver produced the event.</param>
        /// <param name="type">The platform classification of the event.</param>
        /// <param name="seconds">The lag; never negative, because a provider clock running ahead
        ///     would otherwise poison the histogram.</param>
        public void RecordDeliveryEventLag(string transport, EmailDeliveryEventType type, double seconds)
        {
            _deliveryEventLag.Record(
                Math.Max(0, seconds),
                new KeyValuePair<string, object?>(EmailTelemetryNames.TransportTag, transport),
                new KeyValuePair<string, object?>(EmailTelemetryNames.EventTypeTag, EmailDiagnostics.ToTagValue(type)));
        }
    }
}
