// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.Email
{
    /// <summary>One outgoing email.</summary>
    /// <param name="ToEmail">The recipient address.</param>
    /// <param name="ToDisplayName">The recipient display name, when known.</param>
    /// <param name="Subject">The localized subject.</param>
    /// <param name="TextBody">The plain-text body (always present).</param>
    public sealed record EmailMessage(string ToEmail, string? ToDisplayName, string Subject, string TextBody);

    /// <summary>
    ///     Sends email. The contract is batch-shaped so bulk operations (inviting hundreds of
    ///     users) hand the transport one call, not one per recipient. In the Development
    ///     environment a log sink implements this instead of a real transport — the only
    ///     security-relevant difference from a deployed instance.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>Sends a batch of messages.</summary>
        /// <param name="messages">The messages; may be a single-element list.</param>
        /// <param name="cancellationToken">Aborts the send.</param>
        /// <returns>A task that completes when the batch is handed to the transport.</returns>
        Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken);
    }
}
