// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     A user's lifecycle state. A user removed from their last distribution is not deleted:
    ///     they are orphaned, preserving audit history and credentials for painless re-invitation.
    ///     Only <see cref="Active" /> users can obtain tokens.
    /// </summary>
    public enum UserLifecycleState
    {
        /// <summary>A normal user able to sign in and obtain tokens.</summary>
        Active = 0,

        /// <summary>
        ///     No longer a member of any distribution; credentials and history are retained for
        ///     re-invitation, but sign-in and token issuance are refused.
        /// </summary>
        Orphaned = 1,

        /// <summary>Administratively disabled; sign-in and token issuance are refused.</summary>
        Disabled = 2,

        /// <summary>
        ///     Erased: personal data anonymized in place and credentials removed, while the row —
        ///     and therefore the <c>sub</c> — survives for audit referential integrity.
        /// </summary>
        Purged = 3,
    }
}
