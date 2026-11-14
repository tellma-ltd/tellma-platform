// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>How far the latest invitation the distribution raised for a subject got.</summary>
    public enum IdentityDeliveryState
    {
        /// <summary>
        ///     No invitation this distribution raised: the subject was invited by another distribution,
        ///     its invitation answered <c>Active</c>, or it does not exist. The server answers all three
        ///     alike, so the endpoint cannot be used to probe its directory.
        /// </summary>
        NotFound,

        /// <summary>Raised, but the mail has not reached a transport yet.</summary>
        Pending,

        /// <summary>Accepted by a transport; terminal when no delivery events are expected.</summary>
        Sent,

        /// <summary>Accepted by the recipient's mail server.</summary>
        Delivered,

        /// <summary>Rejected by the recipient's mail server, or withheld by the provider.</summary>
        Bounced,

        /// <summary>Delivered, then reported as spam by the recipient.</summary>
        Complained,

        /// <summary>Refused permanently by the transport, typically an undeliverable address.</summary>
        Rejected,

        /// <summary>Retried to the limit without ever being accepted.</summary>
        Abandoned,

        /// <summary>The recipient opened the link.</summary>
        Accepted,
    }
}
