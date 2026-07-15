// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     A user in the global directory. The row id (a GUID string) is the stable, opaque
    ///     <c>sub</c> claim — the durable cross-distribution identity key; it is never the email.
    ///     The server stores no tenant membership: distributions map <c>sub</c> to their own
    ///     users and roles.
    /// </summary>
    public sealed class TellmaIdentityUser : IdentityUser
    {
        /// <summary>The display name emitted as the <c>name</c> claim.</summary>
        public string? DisplayName { get; set; }

        /// <summary>
        ///     The preferred language (BCP 47), set at invitation and emitted as the
        ///     <c>locale</c> claim; drives localized UI and email.
        /// </summary>
        public string Locale { get; set; } = "en";

        /// <summary>The lifecycle state; only <see cref="UserLifecycleState.Active" /> users obtain tokens.</summary>
        public UserLifecycleState LifecycleState { get; set; }

        /// <summary>When the user record was created.</summary>
        public DateTimeOffset CreatedUtc { get; set; }

        /// <summary>When the user was orphaned (removed from their last distribution), if ever.</summary>
        public DateTimeOffset? OrphanedUtc { get; set; }

        /// <summary>When the user was administratively disabled, if ever.</summary>
        public DateTimeOffset? DisabledUtc { get; set; }

        /// <summary>When the user's personal data was purged, if ever.</summary>
        public DateTimeOffset? PurgedUtc { get; set; }

        /// <summary>The last successful interactive sign-in.</summary>
        public DateTimeOffset? LastSignInUtc { get; set; }
    }
}
