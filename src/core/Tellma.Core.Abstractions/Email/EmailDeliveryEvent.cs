// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     A transport-neutral delivery event, produced by a connector adapter from a provider
    ///     callback.
    /// </summary>
    /// <param name="Correlation">The correlation attached at send time, when present. Events without
    ///     one (mail sent outside the platform, recipient-level spam reports) are metered by the
    ///     dispatcher and never reach handlers.</param>
    /// <param name="Recipient">The address the event concerns, when the provider reports it — one
    ///     recipient of a multi-recipient message can bounce while another delivers.</param>
    /// <param name="Type">The platform's classification of the event.</param>
    /// <param name="RawType">The provider's own event name, verbatim — diagnostic detail, and the
    ///     event's only meaning when <paramref name="Type" /> is <see cref="EmailDeliveryEventType.Other" />.</param>
    /// <param name="Reason">Provider-reported detail, chiefly for failures (the bounce reason).</param>
    /// <param name="Timestamp">When the event occurred at the provider — not when the webhook
    ///     arrived; bounces can surface minutes later.</param>
    /// <param name="ProviderEventId">The provider's unique id for this event. Handlers deduplicate
    ///     on it, because providers deliver at least once.</param>
    public sealed record EmailDeliveryEvent(
        EmailCorrelation? Correlation,
        string? Recipient,
        EmailDeliveryEventType Type,
        string RawType,
        string? Reason,
        DateTimeOffset Timestamp,
        string ProviderEventId);
}
