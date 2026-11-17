// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Caching;
using Tellma.Core.Localization;

namespace Tellma.Core.Access
{
    // The seam is spec 0013 §9.3 step 1: the negotiation at Order 200 reads the four locale
    // preferences of the user the connection at Order 100 resolved, through the preferences cache.
    /// <summary>
    ///     The connected user's stored locale preferences: from the profile the scope connected with,
    ///     else from the profile cache under the user's current preferences tag.
    /// </summary>
    /// <param name="connector">The scope's user connector.</param>
    /// <param name="profiles">The process's profiles.</param>
    /// <param name="snapshots">The process's user tag snapshots.</param>
    /// <param name="accessor">The scope's request context.</param>
    internal sealed class ConnectedUserPreferences(
        UserConnector connector, ProfileCache profiles, IUserVersionTagSnapshots snapshots, IRequestContextAccessor accessor) : IUserLocalePreferences
    {
        /// <inheritdoc />
        public ValueTask<UserLocalePreferences?> GetAsync(int userId, CancellationToken cancellationToken)
        {
            // --- The scope's own connected user ---
            UserProfile? profile = connector.Current is { } current && current.UserId == userId ? current.Profile : null;

            // --- Otherwise the cached profile its stamp names, when current ---
            if (profile is null && accessor.Current.Tenant is TenantDescriptor tenant
                && snapshots.Current(tenant.Id, userId) is UserVersionTagSnapshot stamp)
            {
                profile = profiles.Peek(new TenantUserCacheKey(tenant.Id, userId), stamp.Preferences);
            }

            UserLocalePreferences? preferences = profile is null
                ? null
                : new UserLocalePreferences(profile.PreferredLanguage, profile.PreferredCalendar, profile.PreferredTimeZone, profile.PreferredRegion);
            return ValueTask.FromResult(preferences);
        }
    }
}
