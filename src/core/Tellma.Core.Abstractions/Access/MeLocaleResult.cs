// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     The caller's locale members as stored after a locale change, the row's stamp, and the
    ///     caller's preferences tag as it travels in the version-tags header, so the calling tab adopts
    ///     them at once.
    /// </summary>
    /// <param name="PreferredLanguage">The stored preferred language; null for the tenant's default.</param>
    /// <param name="PreferredCalendar">The stored preferred calendar; null for the tenant's default.</param>
    /// <param name="PreferredTimeZone">The stored preferred time zone; null for the tenant's default.</param>
    /// <param name="PreferredRegion">The stored preferred region; null for the language's default.</param>
    /// <param name="PreferredDigits">The stored preferred digits; null for the language's default.</param>
    /// <param name="ModifiedAt">The row's stamp after the change.</param>
    /// <param name="PreferencesTag">The caller's preferences tag after the change, in its wire form.</param>
    public sealed record MeLocaleResult(
        string? PreferredLanguage,
        string? PreferredCalendar,
        string? PreferredTimeZone,
        string? PreferredRegion,
        DigitStyle? PreferredDigits,
        DateTimeOffset ModifiedAt,
        string PreferencesTag);
}
