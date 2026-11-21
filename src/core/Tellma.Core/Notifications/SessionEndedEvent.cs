// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Notifications
{
    /// <summary>
    ///     The <c>session.ended</c> client event: the realtime hub sends it to connections it is
    ///     about to close, so the client stops reconnecting and says why. Core registers the name;
    ///     the web host's hub publishes it.
    /// </summary>
    internal static class SessionEndedEvent
    {
        /// <summary>The event's registered name.</summary>
        public const string Name = "session.ended";

        /// <summary>The reason of a session that ended: a sign-out, a back-channel logout or a revocation.</summary>
        public const string SessionTerminated = "session-terminated";

        /// <summary>The reason of a user whose access to the tenant was revoked: a deactivation.</summary>
        public const string AccessRevoked = "access-revoked";

        /// <summary>The reason of a tenant that is suspended, retired or gone.</summary>
        public const string TenantUnavailable = "tenant-unavailable";
    }

    /// <summary>The <c>session.ended</c> event's payload.</summary>
    /// <param name="Reason">Why the connection closes: one of the reasons of <see cref="SessionEndedEvent" />.</param>
    internal sealed record SessionEndedPayload(string Reason);
}
