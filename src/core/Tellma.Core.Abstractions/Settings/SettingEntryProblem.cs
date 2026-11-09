// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>Why <see cref="TenantSettings.Get{T}" /> read a stored setting as its default.</summary>
    public enum SettingEntryProblem
    {
        /// <summary>The stored value does not read as the key's type, or the key's check refuses it.</summary>
        Invalid,

        /// <summary>
        ///     A secret's stored value cannot be decrypted: the host has no data protection, or another
        ///     key ring, application or tenant protected it (a clone, a restore under another id).
        /// </summary>
        SecretUnreadable,
    }
}
