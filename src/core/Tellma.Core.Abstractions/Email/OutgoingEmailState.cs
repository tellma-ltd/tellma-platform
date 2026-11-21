// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     Where an outgoing email stands: the verdict of its row, which its sending job records and an
    ///     administrator reads. Stored by name, <c>varchar(9)</c>.
    /// </summary>
    /// <remarks>
    ///     A row is <em>settled</em> in <see cref="Sent" />, <see cref="Sandboxed" />,
    ///     <see cref="Rejected" />, <see cref="Failed" /> or <see cref="Cancelled" />, and
    ///     <em>unsettled</em> in <see cref="Pending" /> or <see cref="Sending" />.
    /// </remarks>
    public enum OutgoingEmailState
    {
        /// <summary>Committed and waiting for its job: not yet handed to a provider. Every row starts here.</summary>
        Pending,

        /// <summary>
        ///     Being sent now. A row still here after its job ended was interrupted mid-send and may have
        ///     reached the wire, so it is never sent again on its own.
        /// </summary>
        Sending,

        /// <summary>The provider accepted the message; delivery events may follow.</summary>
        Sent,

        /// <summary>The message never reached a real recipient: it went through the transport's sandbox channel or was withheld.</summary>
        Sandboxed,

        /// <summary>The provider or the outbox refused the message permanently; the row's error code says why.</summary>
        Rejected,

        /// <summary>The send ran out of attempts or was interrupted; an administrator may send it again.</summary>
        Failed,

        /// <summary>Cancelled before it was sent.</summary>
        Cancelled,
    }
}
