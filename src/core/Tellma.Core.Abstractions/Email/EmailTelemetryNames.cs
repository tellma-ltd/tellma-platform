// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The wire names of the platform's email telemetry: the meter and activity source, the
    ///     instruments, and the tags they carry.
    /// </summary>
    /// <remarks>
    ///     Almost all email telemetry is emitted by the pipeline, but one slice cannot be: a connector
    ///     adapter that drops a delivery event at translation (because the event belongs to another
    ///     deployment) records that event itself, since it never reaches the dispatcher. Adapters
    ///     reference only this assembly, so the names live here — shared constants rather than two
    ///     hand-written copies that drift apart and break a dashboard silently.
    /// </remarks>
    public static class EmailTelemetryNames
    {
        /// <summary>The meter every email instrument is created on.</summary>
        public const string MeterName = "Tellma.Email";

        /// <summary>The activity source the send and dispatch spans are created on.</summary>
        public const string ActivitySourceName = "Tellma.Email";

        /// <summary>Counter of messages leaving the pipeline, one measurement per message.</summary>
        public const string SentMessagesInstrument = "tellma.email.sent.messages";

        /// <summary>Histogram of how long one transport call took, in seconds.</summary>
        public const string SendDurationInstrument = "tellma.email.send.duration";

        /// <summary>Histogram of how many messages each transport call carried.</summary>
        public const string SendBatchSizeInstrument = "tellma.email.send.batch.size";

        /// <summary>Counter of inbound delivery events, one measurement per event.</summary>
        public const string DeliveryEventsInstrument = "tellma.email.delivery.events";

        /// <summary>
        ///     Histogram of webhook arrival time minus the event's provider timestamp, in seconds.
        /// </summary>
        public const string DeliveryEventLagInstrument = "tellma.email.delivery.event.lag";

        /// <summary>The unit of the message-counting instruments.</summary>
        public const string MessageUnit = "{message}";

        /// <summary>The unit of the event-counting instruments.</summary>
        public const string EventUnit = "{event}";

        /// <summary>The unit of the duration instruments.</summary>
        public const string SecondUnit = "s";

        /// <summary>Tag: the configuration name of the transport involved.</summary>
        public const string TransportTag = "email.transport";

        /// <summary>Tag: the <see cref="EmailSendOutcome" />, lowercase snake_case.</summary>
        public const string OutcomeTag = "email.outcome";

        /// <summary>Tag: the <see cref="EmailAudience" />, lowercase.</summary>
        public const string AudienceTag = "email.audience";

        /// <summary>Tag: the wire mechanism that carried (or withheld) the message.</summary>
        public const string DeliveryTag = "email.delivery";

        /// <summary>Tag: the correlation's owner key, or <see cref="NoOwner" />.</summary>
        public const string OwnerTag = "email.owner";

        /// <summary>Tag: the category of the tenant the ambient work belongs to.</summary>
        public const string TenantCategoryTag = "tenant.category";

        /// <summary>Tag: the <see cref="EmailDeliveryEventType" />, lowercase snake_case.</summary>
        public const string EventTypeTag = "email.event.type";

        /// <summary>Tag: what the pipeline did with a delivery event.</summary>
        public const string EventRoutingTag = "email.event.routing";

        /// <summary>Tag: the exception type when a batch threw instead of reporting outcomes.</summary>
        public const string ErrorTypeTag = "error.type";

        /// <summary><see cref="DeliveryTag" />: sent on the transport's live channel.</summary>
        public const string LiveDelivery = "live";

        /// <summary><see cref="DeliveryTag" />: sent on the transport's sandbox channel.</summary>
        public const string SandboxDelivery = "sandbox";

        /// <summary><see cref="DeliveryTag" />: never put on a wire at all.</summary>
        public const string WithheldDelivery = "withheld";

        /// <summary><see cref="TenantCategoryTag" />: the ambient tenant is live.</summary>
        public const string LiveTenantCategory = "live";

        /// <summary><see cref="TenantCategoryTag" />: the ambient tenant is a sandbox.</summary>
        public const string SandboxTenantCategory = "sandbox";

        /// <summary><see cref="OwnerTag" />: the message or event carries no correlation.</summary>
        public const string NoOwner = "none";

        /// <summary>
        ///     <see cref="OwnerTag" />: the event named an owner key nothing handles. A literal is
        ///     recorded rather than the key itself, because that key came off the wire and would
        ///     otherwise make this dimension unbounded; the key that was seen goes to the log.
        /// </summary>
        public const string UnknownOwnerTagValue = "unknown";

        /// <summary><see cref="EventRoutingTag" />: delivered to its owning handler.</summary>
        public const string RoutedEvent = "routed";

        /// <summary><see cref="EventRoutingTag" />: carried no correlation to route by.</summary>
        public const string UncorrelatedEvent = "uncorrelated";

        /// <summary><see cref="EventRoutingTag" />: named an owner key nothing handles.</summary>
        public const string UnknownOwnerEvent = "unknown_owner";

        /// <summary>
        ///     <see cref="EventRoutingTag" />: belonged to another deployment and was dropped at
        ///     translation. Only an adapter can record this slice.
        /// </summary>
        public const string ForeignEvent = "foreign";
    }
}
