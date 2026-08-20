// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data.Entities
{
    /// <summary>
    ///     One SSO session at the authority, keyed on the <c>sid</c> claim minted at interactive
    ///     sign-in. The registry powers back-channel logout fan-out, the self-service active
    ///     sessions page, and "sign out everywhere".
    /// </summary>
    public sealed class IdentitySession
    {
        /// <summary>The session identifier (the <c>sid</c> claim value).</summary>
        public string Sid { get; set; } = string.Empty;

        /// <summary>The signed-in user's id (<c>sub</c>).</summary>
        public string UserId { get; set; } = string.Empty;

        /// <summary>When the session was established.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>The last time the session was observed at the authority.</summary>
        public DateTimeOffset LastSeenUtc { get; set; }

        /// <summary>When the session was terminated (logout, revocation), if it has been.</summary>
        public DateTimeOffset? TerminatedUtc { get; set; }

        /// <summary>A truncated user-agent string, for the active-sessions display.</summary>
        public string? UserAgent { get; set; }

        /// <summary>The client IP observed at sign-in, for the active-sessions display.</summary>
        public string? IpAddress { get; set; }

        /// <summary>The distributions (clients) with an active session under this sid.</summary>
        public ICollection<IdentitySessionClient> Clients { get; } = [];
    }
}
