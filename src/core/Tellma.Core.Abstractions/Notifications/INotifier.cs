// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Abstractions.Notifications
{
    /// <summary>
    ///     Raises and withdraws notifications. A scoped service; every service, job handler and
    ///     platform feature that tells a user something calls it.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A notification never adds a round trip: <see cref="Notify" /> and
    ///         <see cref="Withdraw" /> append fixed statements to the batch the caller already sends,
    ///         so the rows commit, or roll back, with the write they describe. After that batch
    ///         commits, every user whose inbox changed receives one <c>inbox.changed</c> client event,
    ///         however many requests targeted them.
    ///     </para>
    ///     <para>
    ///         Every request is checked before anything is appended, and a request that breaks a rule
    ///         of <see cref="NotificationRequest" /> or <see cref="WithdrawRequest" />, or names a type
    ///         this instance did not register, is refused with <see cref="InvalidOperationException" />:
    ///         requests are composed by code, so a violation is a programming error. A call carries
    ///         any number of requests, and several calls on one batch act as one call with their
    ///         union.
    ///     </para>
    /// </remarks>
    public interface INotifier
    {
        /// <summary>
        ///     Appends one statement per request to <paramref name="batch" />: the inbox rows (and, under
        ///     <see cref="DuplicatePolicy.Replace" />, the refresh of the duplicates), then the delivery
        ///     to the other channels. Returns at once; nothing runs until the batch executes.
        /// </summary>
        /// <param name="batch">The caller's persist batch.</param>
        /// <param name="requests">The requests.</param>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule, or names a type that is not registered.</exception>
        void Notify(IDataBatch batch, IReadOnlyList<NotificationRequest> requests);

        /// <summary>
        ///     Raises the requests in a persist batch of their own on the current scope's tenant
        ///     database, for a caller with no batch in flight, such as a hosted service or a post-commit
        ///     effect.
        /// </summary>
        /// <param name="requests">The requests.</param>
        /// <param name="cancellationToken">Cancels the call before the batch is sent.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule, or names a type that is not registered.</exception>
        Task NotifyAsync(IReadOnlyList<NotificationRequest> requests, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Appends one statement per request to <paramref name="batch" />, deleting the rows of each
        ///     request's key. Returns at once; nothing runs until the batch executes.
        /// </summary>
        /// <param name="batch">The caller's persist batch.</param>
        /// <param name="requests">The requests.</param>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule.</exception>
        void Withdraw(IDataBatch batch, IReadOnlyList<WithdrawRequest> requests);

        /// <summary>Withdraws in a persist batch of its own on the current scope's tenant database, as <see cref="NotifyAsync" /> raises.</summary>
        /// <param name="requests">The requests.</param>
        /// <param name="cancellationToken">Cancels the call before the batch is sent.</param>
        /// <returns>A task that completes once the batch committed.</returns>
        /// <exception cref="ArgumentNullException">An argument or a request is null.</exception>
        /// <exception cref="InvalidOperationException">A request breaks a rule.</exception>
        Task WithdrawAsync(IReadOnlyList<WithdrawRequest> requests, CancellationToken cancellationToken = default);
    }
}
