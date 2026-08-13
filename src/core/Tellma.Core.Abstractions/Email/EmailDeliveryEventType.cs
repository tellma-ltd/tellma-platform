// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>The platform's classification of a delivery event.</summary>
    /// <remarks>
    ///     There is deliberately no unsubscribe member yet: unsubscribe semantics belong with the
    ///     durable outbox and its suppression work. Until then, unsubscribe-family provider events
    ///     map to <see cref="Other" /> with the provider's own name in
    ///     <see cref="EmailDeliveryEvent.RawType" />, so nothing is lost — merely unclassified.
    /// </remarks>
    public enum EmailDeliveryEventType
    {
        /// <summary>Accepted by the recipient's mail server.</summary>
        Delivered,

        /// <summary>Temporarily refused; the provider keeps retrying.</summary>
        Deferred,

        /// <summary>Permanently refused by the recipient's mail server.</summary>
        Bounced,

        /// <summary>Discarded by the provider before sending (suppression list, invalid address).</summary>
        Dropped,

        /// <summary>
        ///     Terminally not delivered, for a reason that is neither a recipient-server refusal
        ///     (<see cref="Bounced" />) nor a pre-send discard (<see cref="Dropped" />) — a
        ///     provider-internal failure or a post-acceptance filtering verdict; detail in
        ///     <see cref="EmailDeliveryEvent.Reason" /> and <see cref="EmailDeliveryEvent.RawType" />.
        /// </summary>
        Failed,

        /// <summary>Opened by the recipient, where tracking is enabled.</summary>
        Opened,

        /// <summary>A link in the message was clicked, where tracking is enabled.</summary>
        Clicked,

        /// <summary>Reported as spam by the recipient.</summary>
        SpamReported,

        /// <summary>
        ///     A provider event with no platform classification; see
        ///     <see cref="EmailDeliveryEvent.RawType" />.
        /// </summary>
        Other,
    }
}
