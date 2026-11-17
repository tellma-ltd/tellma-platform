// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Access
{
    // The expectations are spec 0014 §7.1 and §7.2.
    /// <summary>
    ///     The cached tags one connect prologue compares with the database's. A null tag is one
    ///     nothing is cached for, and that part reads as stale: the prologue returns it.
    /// </summary>
    /// <param name="PermissionsTag">The tenant <c>permissions</c> tag the cached permission set was built under.</param>
    /// <param name="UserPermissionsTag">The user's own permissions tag the cached set was built under.</param>
    /// <param name="PreferencesTag">The user's preferences tag the cached profile was read under.</param>
    /// <param name="SettingsTag">
    ///     The tenant <c>settings</c> tag of the settings the batch composes under; <see cref="Guid.Empty" />
    ///     when the scope holds none.
    /// </param>
    public sealed record ConnectExpectations(
        Guid? PermissionsTag,
        Guid? UserPermissionsTag,
        Guid? PreferencesTag,
        Guid SettingsTag);
}
