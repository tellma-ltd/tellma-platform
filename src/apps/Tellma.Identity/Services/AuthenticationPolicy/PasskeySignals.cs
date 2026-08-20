// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;

namespace Tellma.Identity.Services.AuthenticationPolicy
{
    /// <summary>
    ///     The single reading of the WebAuthn device-bound signal, shared by sign-in, enrollment,
    ///     and the management list so the classification cannot drift between them.
    /// </summary>
    public static class PasskeySignals
    {
        /// <summary>
        ///     Whether a passkey is device-bound (hardware, non-synced): a credential that is not
        ///     backup-eligible. The backup-state flag alone would misclassify a syncable passkey
        ///     that has not yet synced, over-asserting the aal3 tier.
        /// </summary>
        /// <param name="passkey">The stored or freshly asserted passkey.</param>
        /// <returns>True when the credential cannot be synced.</returns>
        public static bool IsDeviceBound(UserPasskeyInfo passkey)
        {
            ArgumentNullException.ThrowIfNull(passkey);

            return !passkey.IsBackupEligible;
        }
    }
}
