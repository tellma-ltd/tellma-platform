// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Data.Entities;

namespace Tellma.Identity.Services.Sessions
{
    /// <summary>
    ///     The <c>sid</c>-keyed session registry: which SSO sessions exist and which
    ///     distributions hold tokens under each. Backed by SQL behind this interface so a
    ///     distributed cache can substitute later as a configuration change.
    /// </summary>
    public interface ISessionRegistry
    {
        /// <summary>Creates or refreshes a session row at interactive sign-in.</summary>
        /// <param name="sid">The session identifier.</param>
        /// <param name="userId">The signed-in user.</param>
        /// <param name="userAgent">The browser's user-agent, truncated for display.</param>
        /// <param name="ipAddress">The observed client IP.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>A task that completes when the row is stored.</returns>
        Task UpsertSessionAsync(string sid, string userId, string? userAgent, string? ipAddress, CancellationToken cancellationToken);

        /// <summary>Records that a client obtained tokens under a session (fan-out target list).</summary>
        /// <param name="sid">The session identifier.</param>
        /// <param name="clientId">The client that obtained tokens.</param>
        /// <param name="authorizationId">The OpenIddict authorization backing the grant.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>A task that completes when the registration is stored.</returns>
        Task RegisterClientAsync(string sid, string clientId, string? authorizationId, CancellationToken cancellationToken);

        /// <summary>Marks a session terminated and returns its registered clients for fan-out.</summary>
        /// <param name="sid">The session identifier.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The clients registered under the session (empty when none or unknown).</returns>
        Task<IReadOnlyList<IdentitySessionClient>> TerminateAsync(string sid, CancellationToken cancellationToken);

        /// <summary>Terminates every active session of a user ("sign out everywhere").</summary>
        /// <param name="userId">The user.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>All client registrations across the terminated sessions, with their sids.</returns>
        Task<IReadOnlyList<IdentitySessionClient>> TerminateAllAsync(string userId, CancellationToken cancellationToken);

        /// <summary>Lists a user's active (non-terminated) sessions for the self-service page.</summary>
        /// <param name="userId">The user.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The active sessions, most recent first.</returns>
        Task<IReadOnlyList<IdentitySession>> GetActiveSessionsAsync(string userId, CancellationToken cancellationToken);

        /// <summary>Records a successful back-channel logout delivery.</summary>
        /// <param name="sid">The session identifier.</param>
        /// <param name="clientId">The notified client.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>A task that completes when the acknowledgment is stored.</returns>
        Task MarkNotifiedAsync(string sid, string clientId, CancellationToken cancellationToken);
    }
}
