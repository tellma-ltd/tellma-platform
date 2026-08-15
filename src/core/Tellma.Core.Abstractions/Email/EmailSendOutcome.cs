// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>The per-message outcome of a send attempt.</summary>
    public enum EmailSendOutcome
    {
        /// <summary>
        ///     Accepted by the transport for real delivery toward the recipient. Terminal unless the
        ///     result expects delivery events.
        /// </summary>
        Sent,

        /// <summary>
        ///     Failed in a way that may succeed later (throttling, connection loss mid-batch); the
        ///     caller may retry this message.
        /// </summary>
        TransientFailure,

        /// <summary>Rejected permanently (e.g. invalid address); the caller must not retry.</summary>
        Rejected,

        /// <summary>
        ///     Handled by the sandbox policy instead of being delivered: routed to the transport's
        ///     sandbox channel (a provider validation mode, a mail trap) or withheld with no wire
        ///     activity — either way, no real email went out to the recipient. Success-class and
        ///     terminal: workflows treat it as success (failure handling branches on
        ///     <see cref="TransientFailure" /> and <see cref="Rejected" />, never on inequality with
        ///     <see cref="Sent" />), while user-facing status may render it distinctly — a sandbox
        ///     tenant's outbox reads "sandboxed", not "sent". Emitted only by the platform routing
        ///     policy; transports never produce it.
        /// </summary>
        Sandboxed,
    }
}
