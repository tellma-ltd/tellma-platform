// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>
    ///     The one email instrument slice a connector adapter must emit itself: delivery events that
    ///     belong to another deployment, which this adapter drops at translation and which therefore
    ///     never reach the pipeline's dispatcher.
    /// </summary>
    /// <remarks>
    ///     The meter name, instrument name, unit, tags, and tag values all come from the shared
    ///     telemetry contract, so this measurement lands on the same instrument the pipeline emits the
    ///     other routing slices on and cannot drift away from it. Two meters with the same name are
    ///     one instrument to any collector — which is exactly why the event-type spelling here has to
    ///     be the pipeline's own table rather than a copy of it.
    /// </remarks>
    internal sealed class SendGridEmailMetrics
    {
        private readonly Counter<long> _deliveryEvents;

        /// <summary>Creates the counter on the shared email meter.</summary>
        /// <param name="meterFactory">The host's meter factory.</param>
        public SendGridEmailMetrics(IMeterFactory meterFactory)
        {
            ArgumentNullException.ThrowIfNull(meterFactory);

            Meter meter = meterFactory.Create(EmailTelemetryNames.MeterName);
            _deliveryEvents = meter.CreateCounter<long>(
                EmailTelemetryNames.DeliveryEventsInstrument,
                EmailTelemetryNames.EventUnit,
                "Inbound provider delivery events, by type and routing outcome.");
        }

        /// <summary>Records an event dropped because it belongs to another deployment.</summary>
        /// <param name="type">The platform classification of the dropped event.</param>
        public void RecordForeignEvent(EmailDeliveryEventType type)
        {
            TagList tags = new()
            {
                { EmailTelemetryNames.TransportTag, SendGridEmailServiceCollectionExtensions.TransportName },
                { EmailTelemetryNames.EventTypeTag, EmailTelemetryTagValues.ToTagValue(type) },
                { EmailTelemetryNames.EventRoutingTag, EmailTelemetryNames.ForeignEvent },
                { EmailTelemetryNames.OwnerTag, EmailTelemetryNames.NoOwner },
            };

            _deliveryEvents.Add(1, tags);
        }
    }
}
