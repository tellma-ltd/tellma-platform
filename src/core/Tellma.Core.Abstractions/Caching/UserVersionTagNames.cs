// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     The two version tags each user carries, as columns of the user's stamp row
    ///     (<c>PermissionsTag</c>, <c>PreferencesTag</c>).
    /// </summary>
    public enum UserVersionTagNames
    {
        /// <summary>
        ///     The user's own permissions: bumped by writes to the user's role memberships. A
        ///     permissions cache entry is current only when this tag and the tenant's
        ///     <see cref="VersionTagNames.Permissions" /> tag both match.
        /// </summary>
        Permissions,

        /// <summary>The user's profile and preferences: bumped by writes to the user's own row and preference tables.</summary>
        Preferences,
    }
}
