// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Sends email on the wire. Implemented by connector adapters (SendGrid, SMTP, …) and by the
    ///     development log sink, but the implementation consumers receive via DI is the platform
    ///     router, which applies the sandbox policy and forwards to the active adapter — consumers
    ///     stay transport-agnostic either way. The contract is batch-shaped: bulk operations hand
    ///     the transport one call, never one call per message.
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>Sends a batch of messages and reports a per-message outcome.</summary>
        /// <remarks>
        ///     Implementations must return exactly one result per input message, in input order —
        ///     position is the correlation between input and outcome. Implementations may throw only
        ///     when no message went out (an up-front authentication or connection failure) or when
        ///     the caller cancels; once any message has gone out they must report per-message
        ///     outcomes instead of throwing, so callers can retry transient failures without
        ///     duplicating messages that already went out. Implementations must not retry beyond
        ///     sub-second transport pragmatics — durable retry policy belongs to the caller.
        /// </remarks>
        /// <param name="messages">The messages to send, in the caller's order.</param>
        /// <param name="cancellationToken">Abandons the batch; in-flight work is cancelled and
        ///     <see cref="OperationCanceledException" /> propagates even mid-batch.</param>
        /// <returns>One result per input message, in input order.</returns>
        Task<IReadOnlyList<EmailSendResult>> SendAsync(
            IReadOnlyList<EmailMessage> messages,
            CancellationToken cancellationToken);
    }
}
