// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Notifications;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     The fixture's notification types and its one channel: a mutable type that targets a
    ///     shipment, a non-mutable warning without a target, a channel-only type, and a channel on by
    ///     default. The suites raise them; the fixture registers no sender, so a host that delivers
    ///     adds its own.
    /// </summary>
    public static class FixtureNotifications
    {
        /// <summary>A mutable type that targets <see cref="ShipmentResource" />.</summary>
        public const string Shipped = "fixture.shipment.shipped";

        /// <summary>A non-mutable warning without a target.</summary>
        public const string Held = "fixture.shipment.held";

        /// <summary>A channel-only type, which writes no inbox row.</summary>
        public const string Texted = "fixture.shipment.texted";

        /// <summary>The resource <see cref="Shipped" /> targets: the fixture's shipments, keyed by a <see cref="long" />.</summary>
        public const string ShipmentResource = "fixture.Shipment";

        /// <summary>The fixture's channel, on for every type until a user turns it off.</summary>
        public const string SmsChannel = "sms";

        /// <summary>The types, in registration order.</summary>
        public static IReadOnlyList<NotificationTypeDescriptor> Types { get; } =
        [
            new(Shipped, "shipments", Mutable: true, TargetResource: ShipmentResource),
            new(Held, "shipments", Mutable: false, TargetResource: null, Severity: NotificationSeverity.Warning),
            new(Texted, "shipments", Mutable: false, TargetResource: null, Inbox: false),
        ];

        /// <summary>The channel.</summary>
        public static NotificationChannelDescriptor Sms { get; } = new(SmsChannel, DefaultEnabled: true);
    }
}
