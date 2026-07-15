// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;

namespace Tellma.Identity.Data
{
    /// <summary>
    ///     Identity store settings that shape the EF Core model. Shared by the runtime
    ///     registration and the design-time migrations factory: if the two ever disagreed, the
    ///     passkeys table (gated on schema version 3) would silently vanish from generated
    ///     migrations.
    /// </summary>
    public static class TellmaIdentityModelDefaults
    {
        /// <summary>Applies the store options that affect the shape of the EF Core model.</summary>
        /// <param name="options">The Identity options to configure.</param>
        public static void ConfigureStoreOptions(IdentityOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            // Schema version 3 adds the AspNetUserPasskeys table backing WebAuthn credentials.
            options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
        }
    }
}
