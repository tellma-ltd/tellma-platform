// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Access
{
    // The connector is spec 0014 §7.1; the caches it feeds are §6.2 and §6.3.
    /// <summary>
    ///     Connects a scope's principal to its user in the tenant through the connect prologue, and
    ///     keeps the process's connect, permission and profile caches from what each prologue read.
    ///     One per scope.
    /// </summary>
    public interface IUserConnector
    {
        /// <summary>
        ///     Connects a request's caller by the subject the scope is bound to: the cached connection
        ///     when there is one, else one prologue-only round trip, which concurrent first requests
        ///     of one subject on one instance share.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The connected user.</returns>
        /// <exception cref="TenantNotFoundException">No active user of the tenant has the subject.</exception>
        /// <exception cref="InvalidOperationException">The scope is not a user's or a service account's.</exception>
        Task<ConnectedUser> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Connects a user by id: one prologue-only round trip, which never stamps the user's
        ///     activity and never records a first request. The scope is connected as the user only
        ///     when its context is bound to that user, as a background scope that runs as them is;
        ///     for any other user (another user's permissions loaded to explain or evaluate them) the
        ///     user is returned and cached, and the scope's own connected user is left as it was.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The user with that id.</returns>
        /// <exception cref="TenantNotFoundException">No active user of the tenant has the id.</exception>
        /// <exception cref="InvalidOperationException">The scope is not a user's or a service account's.</exception>
        Task<ConnectedUser> ConnectAsUserAsync(int userId, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Connects the platform's own work: the system user with the system's permission set, and
        ///     the tenant's settings when the scope's cache lacks them.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The connected system user.</returns>
        /// <exception cref="InvalidOperationException">The scope's principal is not the platform's own.</exception>
        Task<ConnectedUser> ConnectAsSystemAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Marks a fresh batch as carrying the connect prologue and adds the prologue's text and
        ///     parameters for the premises: the version-tag prelude and the settings cold load yield to
        ///     it, and the batch's body runs only when its guard passes.
        /// </summary>
        /// <param name="batch">The batch, before its first statement; a batch of the scope's tenant database.</param>
        /// <param name="premises">What the prologue assumes.</param>
        /// <returns>What the prologue read, complete once the batch executed.</returns>
        /// <exception cref="ArgumentException">The batch is not one of the platform's tenant batches.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The batch already has statements, or is a maintenance batch; or the premises are the
        ///     system's and the scope is not the platform's own.
        /// </exception>
        BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises);

        /// <summary>
        ///     Applies what a prologue read: the connect, permission and profile cache entries are
        ///     replaced from it, and the scope's connected user becomes the one it describes when that
        ///     is the user the scope's context is bound to (or the scope is not bound yet).
        /// </summary>
        /// <param name="result">A result <see cref="Contribute" /> of this connector returned.</param>
        /// <returns>The connected user the result describes.</returns>
        /// <exception cref="TenantNotFoundException">
        ///     The result names no user, or an inactive one, whose entries are dropped; or the system
        ///     user outside the platform's own scope.
        /// </exception>
        /// <exception cref="ArgumentException">The result is not one of this connector's.</exception>
        ConnectedUser Apply(ConnectResult result);
    }
}
