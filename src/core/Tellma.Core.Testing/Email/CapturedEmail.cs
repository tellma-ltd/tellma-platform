// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Testing.Email
{
    /// <summary>One message a <see cref="CapturingEmailSender" /> recorded.</summary>
    /// <param name="Message">The message exactly as it reached the sender — after the router's
    ///     sandbox marking, when the capturing sender is wired in as a transport.</param>
    /// <param name="Result">What the sender reported for it.</param>
    /// <param name="Timestamp">When it was captured.</param>
    /// <param name="Ordinal">The message's zero-based position in this sender's lifetime, across
    ///     every batch it has handled.</param>
    public sealed record CapturedEmail(
        EmailMessage Message,
        EmailSendResult Result,
        DateTimeOffset Timestamp,
        int Ordinal);
}
