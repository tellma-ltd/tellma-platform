// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>The outcome of one message in a send batch, positional to the input list.</summary>
    /// <param name="Outcome">What happened to the message.</param>
    /// <param name="ProviderMessageId">The transport's own identifier for the accepted message, when
    ///     it reports one (SendGrid's <c>X-Message-Id</c>, the SMTP Message-Id). Diagnostic and audit
    ///     data — stored for support lookups and as a fallback correlation; nothing branches on it.</param>
    /// <param name="Error">Human-readable failure detail when <paramref name="Outcome" /> is
    ///     <see cref="EmailSendOutcome.TransientFailure" /> or <see cref="EmailSendOutcome.Rejected" />.</param>
    /// <param name="ExpectsDeliveryEvents">True when delivery events may still arrive for this
    ///     message: it went out on the live channel of a transport whose delivery webhook is
    ///     configured, and it carries a correlation for events to return to. False otherwise —
    ///     sandboxed, uncorrelated, or eventless-transport mail is terminal at its send outcome.
    ///     Per message, because one batch can mix both (an internal and an external message of a
    ///     sandbox tenant).</param>
    public sealed record EmailSendResult(
        EmailSendOutcome Outcome,
        string? ProviderMessageId = null,
        string? Error = null,
        bool ExpectsDeliveryEvents = false);
}
