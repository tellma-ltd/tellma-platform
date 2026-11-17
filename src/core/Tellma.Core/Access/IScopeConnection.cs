// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Access
{
    /// <summary>
    ///     The scope's connection as the guarded runner and the evaluator drive it: the connected
    ///     user, the premises of the next batch, the prologue's result applied, the cold re-connect
    ///     after a persist re-check found a moved tag, the deny re-check, and another user's
    ///     permissions loaded on their own. The user connector implements it; tests substitute a fake.
    /// </summary>
    internal interface IScopeConnection
    {
        /// <summary>What the scope's request asked for; empty for background work and before the connect initializer ran.</summary>
        RequestContextInputs Inputs { get; }

        /// <summary>The scope's connected user: the one already connected, else a connection by the scope's principal.</summary>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The connected user.</returns>
        /// <exception cref="TenantNotFoundException">The scope's caller is not an active user of the tenant.</exception>
        Task<ConnectedUser> CurrentAsync(CancellationToken cancellationToken);

        /// <summary>The premises of the next batch composed for a connected user, by the way the scope connected.</summary>
        /// <param name="user">The connected user the batch is composed for.</param>
        /// <returns>The premises.</returns>
        ConnectPremises PremisesFor(ConnectedUser user);

        /// <summary>Marks a fresh batch with the connect prologue for the premises; see <see cref="IUserConnector.Contribute" />.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="premises">The premises.</param>
        /// <returns>What the prologue read, complete once the batch executed.</returns>
        BatchResult<ConnectResult> Contribute(IDataBatch batch, ConnectPremises premises);

        /// <summary>
        ///     What a batch's prologue read: the completed handle's value, or the result read by a
        ///     round trip that failed afterwards and so never completed the handle.
        /// </summary>
        /// <param name="prologue">The handle <see cref="Contribute" /> returned.</param>
        /// <returns>The result; null when the round trip failed before the prologue was read.</returns>
        ConnectResult? ReadOf(BatchResult<ConnectResult> prologue);

        /// <summary>Applies what a prologue read, over the connected user the batch was composed for; see <see cref="IUserConnector.Apply" />.</summary>
        /// <param name="result">The prologue's result.</param>
        /// <param name="basis">The connected user the batch was composed for.</param>
        /// <returns>The connected user the result describes.</returns>
        /// <exception cref="TenantNotFoundException">The result names no user, or an inactive one.</exception>
        ConnectedUser Apply(ConnectResult result, ConnectedUser basis);

        /// <summary>
        ///     Re-connects with nothing cached: one prologue-only round trip that returns the fresh
        ///     rows, profile, settings and tags.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection.</param>
        /// <returns>The connected user.</returns>
        /// <exception cref="TenantNotFoundException">The scope's caller is no longer an active user of the tenant.</exception>
        Task<ConnectedUser> ReconnectColdAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Re-validates a connected user's permission set: one prologue-only round trip under the
        ///     scope's warm premises for the user, which never asks for the rows of a set merely old.
        ///     A passing prologue advances the set's validation time; a moved tag returns fresh rows,
        ///     applied into a new set.
        /// </summary>
        /// <param name="user">The scope's connected user.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The connected user the round trip describes.</returns>
        /// <exception cref="TenantNotFoundException">The user is no longer an active user of the tenant.</exception>
        Task<ConnectedUser> RevalidateAsync(ConnectedUser user, CancellationToken cancellationToken);

        /// <summary>
        ///     Loads another user's permission set on its own: one prologue-only round trip by the
        ///     user's id that expects nothing cached. The set is neither cached nor connected, and
        ///     the round trip reports none of the user's tags to the response.
        /// </summary>
        /// <param name="userId">The user.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The set; null when no user of the tenant has the id, or the user is inactive or the system user.</returns>
        Task<UserAccess?> LoadDetachedAsync(int userId, CancellationToken cancellationToken);

        /// <summary>Drops a user's entries after a persist re-check found the user deactivated, counting and logging the refusal.</summary>
        /// <param name="user">The user.</param>
        void Drop(ConnectedUser user);
    }
}
