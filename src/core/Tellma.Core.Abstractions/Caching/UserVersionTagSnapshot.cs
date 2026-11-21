// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Caching
{
    /// <summary>
    ///     One user's two version tags as a caller's batch read them from the user's stamp row,
    ///     overlaid with that batch's own bump of either.
    /// </summary>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="UserId">The user, an id within the tenant.</param>
    /// <param name="Preferences">The user's <see cref="UserVersionTagNames.Preferences" /> tag.</param>
    /// <param name="Permissions">The user's <see cref="UserVersionTagNames.Permissions" /> tag.</param>
    /// <param name="ReadAtUtc">The instance clock when the batch read the row.</param>
    public sealed record UserVersionTagSnapshot(
        int TenantId, int UserId, VersionTag Preferences, VersionTag Permissions, DateTimeOffset ReadAtUtc);
}
