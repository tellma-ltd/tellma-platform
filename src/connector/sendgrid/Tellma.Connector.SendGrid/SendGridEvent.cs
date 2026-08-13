// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.SendGrid
{
    /// <summary>One entry of an event-webhook batch.</summary>
    /// <param name="EventName">SendGrid's own event name ("delivered", "bounce", …), verbatim.</param>
    /// <param name="Email">The recipient address the event concerns.</param>
    /// <param name="Timestamp">When the event occurred at SendGrid.</param>
    /// <param name="EventId">The <c>sg_event_id</c>, documented unique and the recommended dedupe key.</param>
    /// <param name="MessageId">The <c>sg_message_id</c>, SendGrid's id for the message.</param>
    /// <param name="Reason">The failure detail, where the event carries one.</param>
    /// <param name="CustomArgs">Every unmapped top-level string field. SendGrid delivers a message's
    ///     custom arguments as top-level fields rather than a nested object, so this is where they
    ///     land — alongside whatever other string fields the event type happens to carry.</param>
    public sealed record SendGridEvent(
        string EventName,
        string? Email,
        DateTimeOffset Timestamp,
        string EventId,
        string? MessageId,
        string? Reason,
        IReadOnlyDictionary<string, string> CustomArgs);
}
