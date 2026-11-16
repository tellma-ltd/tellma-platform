// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Localization
{
    /// <summary>
    ///     The connected user's stored locale preferences, as the user connection resolved them for
    ///     the scope. The locale negotiation reads them after the user connection ran; a composition
    ///     without a user connection registers none, and negotiation then goes from the request and
    ///     the tenant alone.
    /// </summary>
    internal interface IUserLocalePreferences
    {
        /// <summary>The preferences of a user of the scope's tenant.</summary>
        /// <param name="userId">The user the scope is connected as.</param>
        /// <param name="cancellationToken">Cancels a read the preferences need.</param>
        /// <returns>The preferences, or null when the user has none on record.</returns>
        ValueTask<UserLocalePreferences?> GetAsync(int userId, CancellationToken cancellationToken);
    }

    /// <summary>A user's stored locale preferences; each null when the user stated none.</summary>
    /// <param name="Language">The preferred language's code.</param>
    /// <param name="Calendar">The preferred calendar's code.</param>
    /// <param name="TimeZone">The preferred display zone, an IANA id.</param>
    /// <param name="Region">The preferred region, which picks the user's formats.</param>
    internal sealed record UserLocalePreferences(string? Language, string? Calendar, string? TimeZone, string? Region);
}
