// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;

namespace Tellma.Core.Abstractions.Identity
{
    /// <summary>
    ///     The distribution's machine-to-machine client of its identity server's management API: bulk
    ///     invitation, bulk delivery status, and service accounts. One per distribution, shared by every
    ///     tenant; it carries no tenant and is never called inside a database transaction, because a
    ///     remote call under row locks would block every other save of the tenant.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The bulk members split their input into chunks of at most
    ///         <see cref="MaxChunkSize" /> items, one HTTP call each, sent one after the other, and
    ///         answer one result per input item in input order. A chunk completes atomically on the
    ///         server. When a chunk fails, or the caller's cancellation token fires, after at least one
    ///         chunk completed, the member returns the completed chunks' results and stops: the caller
    ///         tells this prefix from a complete answer by its length. No chunk is ever retried, because
    ///         the server's <c>Reinvited</c> answer queues a fresh email, so a blind retry would mail
    ///         users twice.
    ///     </para>
    ///     <para>
    ///         Before any chunk completed, a failure raises by its cause: an unreachable identity
    ///         server (a transport failure, a timeout, or a <c>5xx</c>) is a
    ///         <see cref="DependencyUnavailableException" />; an identity server that refuses the
    ///         distribution (its credentials or registration are wrong, which no retry fixes) is an
    ///         <see cref="InvalidOperationException" />; the caller's cancellation is an
    ///         <see cref="OperationCanceledException" />. The identity server's own <c>401</c> never
    ///         reaches the caller: it concerns the distribution's token, not the caller's session.
    ///     </para>
    ///     <para>Thread-safe; register as a singleton.</para>
    /// </remarks>
    public interface IIdentityServerClient
    {
        /// <summary>The most items one HTTP call of a bulk member carries.</summary>
        const int MaxChunkSize = 1000;

        /// <summary>
        ///     Creates or gets identity users by email and queues an invitation link to each that needs
        ///     one.
        /// </summary>
        /// <param name="invitations">The invitations, in the order the results are answered in.</param>
        /// <param name="cancellationToken">Stops the call; after a completed chunk the completed results are returned instead of an exception.</param>
        /// <returns>
        ///     One result per invitation, in input order; a shorter list is the prefix of the chunks
        ///     that completed before a failure or a cancellation. An empty input answers an empty list
        ///     without calling the server.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="invitations" /> is null.</exception>
        /// <exception cref="ArgumentException">An invitation is null.</exception>
        /// <exception cref="DependencyUnavailableException">The identity server is unreachable before any chunk completed.</exception>
        /// <exception cref="InvalidOperationException">The identity server refused the distribution before any chunk completed.</exception>
        /// <exception cref="OperationCanceledException">The call was cancelled before any chunk completed.</exception>
        Task<IReadOnlyList<IdentityInvitationResult>> InviteAsync(
            IReadOnlyList<IdentityInvitation> invitations, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Reads the delivery state of the latest invitation this distribution raised for each
        ///     subject. A subject invited by another distribution, or whose invitation answered
        ///     <c>Active</c>, is <see cref="IdentityDeliveryState.NotFound" />.
        /// </summary>
        /// <param name="subjects">The subjects, in the order the results are answered in.</param>
        /// <param name="cancellationToken">Stops the call; after a completed chunk the completed results are returned instead of an exception.</param>
        /// <returns>
        ///     One status per subject, in input order; a shorter list is the prefix of the chunks that
        ///     completed before a failure or a cancellation. An empty input answers an empty list
        ///     without calling the server.
        /// </returns>
        /// <exception cref="ArgumentNullException"><paramref name="subjects" /> is null.</exception>
        /// <exception cref="ArgumentException">A subject is null, empty or white space.</exception>
        /// <exception cref="DependencyUnavailableException">The identity server is unreachable before any chunk completed.</exception>
        /// <exception cref="InvalidOperationException">The identity server refused the distribution before any chunk completed.</exception>
        /// <exception cref="OperationCanceledException">The call was cancelled before any chunk completed.</exception>
        Task<IReadOnlyList<IdentityDeliveryStatus>> GetInvitationDeliveryAsync(
            IReadOnlyList<string> subjects, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Registers a confidential <c>client_credentials</c> client at the identity server whose
        ///     tokens are accepted by this distribution's bearer surface. The secret is answered once and
        ///     never logged.
        /// </summary>
        /// <param name="displayName">The account's display name at the identity server.</param>
        /// <param name="cancellationToken">Stops the call.</param>
        /// <returns>The new account's client id and its one-time secret.</returns>
        /// <exception cref="ArgumentException"><paramref name="displayName" /> is null, empty or white space.</exception>
        /// <exception cref="DependencyUnavailableException">The identity server is unreachable.</exception>
        /// <exception cref="InvalidOperationException">The identity server refused the distribution or the request.</exception>
        /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
        Task<IdentityServiceAccount> CreateServiceAccountAsync(string displayName, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Deletes a service account this distribution created. The server scopes the delete to the
        ///     distribution's own accounts, and an account it does not find counts as deleted.
        /// </summary>
        /// <param name="clientId">The account's client id.</param>
        /// <param name="cancellationToken">Stops the call.</param>
        /// <returns>A task that completes when the account no longer exists.</returns>
        /// <exception cref="ArgumentException"><paramref name="clientId" /> is null, empty or white space.</exception>
        /// <exception cref="DependencyUnavailableException">The identity server is unreachable.</exception>
        /// <exception cref="InvalidOperationException">The identity server refused the distribution or the request.</exception>
        /// <exception cref="OperationCanceledException">The call was cancelled.</exception>
        Task DeleteServiceAccountAsync(string clientId, CancellationToken cancellationToken = default);
    }
}
