// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.DataProtection;
using System.Globalization;

namespace Tellma.Core.Settings
{
    // The protector is spec 0013 §5.3's last rule but two: one purpose, scoped by the tenant id.
    /// <summary>
    ///     The data protector of secret settings: one purpose, narrowed by the tenant's id, so a
    ///     secret read in another tenant's database, or under another key ring or application name,
    ///     does not unprotect.
    /// </summary>
    internal static class SettingSecrets
    {
        /// <summary>The protector's purpose.</summary>
        public const string Purpose = "Tellma.Core.Settings.Secret";

        /// <summary>The protector of a tenant's secret settings.</summary>
        /// <param name="provider">The host's data protection.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The protector.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="provider" /> is null.</exception>
        public static IDataProtector For(IDataProtectionProvider provider, int tenantId)
        {
            ArgumentNullException.ThrowIfNull(provider);
            return provider.CreateProtector(Purpose, tenantId.ToString(CultureInfo.InvariantCulture));
        }
    }
}
