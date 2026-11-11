// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Told when sessions end and when a user loses access to a tenant, so whatever outlives a
    ///     request on the user's behalf, such as an open realtime connection, ends with them.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Both calls run on the instance that served the sign-out, the revocation or the
    ///         deactivation, after it took effect. The caller awaits each listener in registration
    ///         order and logs a listener that throws: a failing listener never fails the sign-out or
    ///         the deactivation it follows, and never keeps a later listener from being told.
    ///     </para>
    ///     <para>A collection service: every registration is called.</para>
    /// </remarks>
    [CollectionService]
    public interface ISessionTerminationListener
    {
        /// <summary>Reacts to sessions of one subject that ended: a sign-out, a back-channel logout or a revocation.</summary>
        /// <param name="subject">The subject whose sessions ended.</param>
        /// <param name="sessionHandles">
        ///     The handles of the ended sessions, as <see cref="RequestContext.SessionHandle" /> names
        ///     them. Never a session's key, which reaches no listener and no log.
        /// </param>
        /// <returns>A task that completes when the listener has reacted.</returns>
        Task SessionsTerminatedAsync(string subject, IReadOnlyList<string> sessionHandles);

        /// <summary>Reacts to a subject that lost access to one tenant: its user there was deactivated.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="subject">The subject that lost access.</param>
        /// <returns>A task that completes when the listener has reacted.</returns>
        Task TenantAccessRevokedAsync(int tenantId, string subject);
    }
}
