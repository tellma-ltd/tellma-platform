// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Services.BackchannelLogout
{
    /// <summary>
    ///     Global logout: terminates SSO sessions in the registry, revokes the grants backing
    ///     each registered client's refresh tokens, and POSTs a signed <c>logout_token</c> to
    ///     every distribution's back-channel logout endpoint. Delivery is best-effort — failures
    ///     are audited, never blocking, because stamp revalidation and the short access-token
    ///     lifetime bound the damage.
    /// </summary>
    public interface IBackchannelLogoutService
    {
        /// <summary>Terminates one session and notifies its distributions.</summary>
        /// <param name="sid">The session identifier.</param>
        /// <param name="subject">The user the session belongs to.</param>
        /// <param name="cancellationToken">Aborts the operation (not individual deliveries).</param>
        /// <returns>A task that completes when termination and fan-out finish.</returns>
        Task TerminateSessionAsync(string sid, string subject, CancellationToken cancellationToken);

        /// <summary>Terminates every active session of a user ("sign out everywhere").</summary>
        /// <param name="subject">The user.</param>
        /// <param name="cancellationToken">Aborts the operation (not individual deliveries).</param>
        /// <returns>A task that completes when termination and fan-out finish.</returns>
        Task TerminateAllSessionsAsync(string subject, CancellationToken cancellationToken);
    }
}
