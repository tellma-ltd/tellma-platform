// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.Cookies;

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     The store of the BFF's cookie sessions: one row per session in the catalog, read through a
    ///     bounded per-instance cache, and the revocations that end sessions from outside a request.
    /// </summary>
    /// <remarks>
    ///     The session cookie carries only a random key; the ticket — the principal, the identity
    ///     server's session and the tokens — is kept protected in the row. Every revocation tells the
    ///     session-termination listeners which sessions ended, by their handles; a key never leaves the
    ///     store and the cookie.
    /// </remarks>
    public interface ICatalogSessionStore : ITicketStore
    {
        /// <summary>Ends every session the identity server's session started.</summary>
        /// <param name="sid">The identity server's session id.</param>
        /// <param name="cancellationToken">Cancels the revocation.</param>
        /// <returns>A task that completes when the sessions are deleted and the listeners told.</returns>
        Task RevokeBySidAsync(string sid, CancellationToken cancellationToken = default);

        /// <summary>Ends every session of a subject, across the distribution.</summary>
        /// <param name="subject">The subject.</param>
        /// <param name="cancellationToken">Cancels the revocation.</param>
        /// <returns>A task that completes when the sessions are deleted and the listeners told.</returns>
        Task RevokeBySubjectAsync(string subject, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Deletes the sessions that expired more than a day ago, in bounded batches. Idempotent and
        ///     safe on every instance at once.
        /// </summary>
        /// <param name="cancellationToken">Cancels the sweep between batches.</param>
        /// <returns>How many sessions were deleted.</returns>
        Task<int> SweepAsync(CancellationToken cancellationToken = default);
    }
}
