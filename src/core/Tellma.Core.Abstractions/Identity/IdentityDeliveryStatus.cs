// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>The delivery state of the latest invitation the distribution raised for one subject.</summary>
    /// <param name="Subject">The subject asked about.</param>
    /// <param name="State">How far the invitation got.</param>
    /// <param name="ExpectsDeliveryEvents">
    ///     Whether a provider will report further on the message. False makes
    ///     <see cref="IdentityDeliveryState.Sent" /> the end of the story: a relay that reports nothing
    ///     back leaves no feedback, which never means that delivery failed.
    /// </param>
    /// <param name="SentUtc">When the message reached a transport; null until it has.</param>
    /// <param name="UpdatedUtc">When a provider last reported on it; null when none has.</param>
    /// <param name="Reason">
    ///     The provider's failure detail, present only for <see cref="IdentityDeliveryState.Bounced" />,
    ///     <see cref="IdentityDeliveryState.Rejected" />, <see cref="IdentityDeliveryState.Abandoned" />
    ///     and <see cref="IdentityDeliveryState.Complained" />.
    /// </param>
    public sealed record IdentityDeliveryStatus(
        string Subject,
        IdentityDeliveryState State,
        bool ExpectsDeliveryEvents,
        DateTimeOffset? SentUtc,
        DateTimeOffset? UpdatedUtc,
        string? Reason);
}
