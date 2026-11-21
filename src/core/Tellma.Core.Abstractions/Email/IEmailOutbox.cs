// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The one way mail enters the outbox: emails written in the transaction of the change that
    ///     produced them, so both commit or roll back together, and sent afterwards by a background job
    ///     each. A scoped service.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The call validates every request, stages the attachments as blobs, and enlists one
    ///         <see cref="OutgoingEmail" /> row per request with the given write host, any frame the
    ///         platform opened: an entity action's or a validation hook's host, an API action's, a job's
    ///         or a provisioning step's. The rows' sending jobs are enqueued in the frame's persist, so
    ///         the rows and their jobs commit with the frame's transaction. On a rollback only the staged
    ///         blobs remain, and their staging deadline expires them.
    ///     </para>
    ///     <para>
    ///         Staging runs before the frame persists, one database round trip and one store write per
    ///         file, and counts against the acting user's staging quota (a system scope is exempt). A
    ///         persist hook cannot enqueue, since its frame's rounds are over; it enqueues a job whose
    ///         handler calls the outbox instead.
    ///     </para>
    /// </remarks>
    public interface IEmailOutbox
    {
        /// <summary>
        ///     Validates the requests, stages their attachments, and enlists one row per request with the
        ///     frame, in request order.
        /// </summary>
        /// <param name="host">The frame the rows commit with.</param>
        /// <param name="requests">The emails; at least one.</param>
        /// <param name="cancellationToken">Cancels the staging before the rows are enlisted.</param>
        /// <returns>The enlisted save, whose rows carry their final ids once the task completed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="host" /> or <paramref name="requests" /> is null.</exception>
        /// <exception cref="ArgumentException">
        ///     <paramref name="requests" /> is empty, holds a null request, or holds one whose source is
        ///     neither <see cref="EmailEnqueueRequest.ApiSource" /> nor <see cref="EmailEnqueueRequest.NotificationSource" />.
        /// </exception>
        /// <exception cref="ValidationException">
        ///     A request is malformed, has too many recipients or is too large, or a blob it attaches
        ///     cannot be resolved. Nothing was staged.
        /// </exception>
        /// <exception cref="Blobs.BlobRejectedException">Staging an attachment was refused; the attachments staged before it stay staged and expire.</exception>
        /// <exception cref="InvalidOperationException">The frame does not admit enlistment now.</exception>
        Task<EnlistedSave<OutgoingEmail>> EnqueueAsync(IWriteHost host, IReadOnlyList<EmailEnqueueRequest> requests, CancellationToken cancellationToken = default);
    }
}
