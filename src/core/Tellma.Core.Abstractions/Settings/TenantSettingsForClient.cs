// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Settings
{
    /// <summary>
    ///     Everything any member of the tenant may know about its settings: the client's view. It is
    ///     cached beside the tenant's settings as UTF-8 JSON serialized once per load, so serving it
    ///     never serializes.
    /// </summary>
    /// <remarks>
    ///     It never carries a server-only or secret value and no related entity: a referenced name may
    ///     change while the settings tag does not.
    /// </remarks>
    /// <param name="TenantId">The tenant.</param>
    /// <param name="Names">The tenant's display name in each content language, by position 1 to 3; null where unset.</param>
    /// <param name="Languages">The content languages, primary first.</param>
    /// <param name="Calendars">The calendar codes, primary first.</param>
    /// <param name="TimeZone">The tenant's zone, an IANA id.</param>
    /// <param name="Region">The tenant's default region; null for each language's default.</param>
    /// <param name="Digits">The tenant's default digits; null for the region's default.</param>
    /// <param name="Entries">
    ///     Every client-visible setting by key, stored or default: the client never learns which.
    /// </param>
    public sealed record TenantSettingsForClient(
        int TenantId,
        IReadOnlyList<string?> Names,
        IReadOnlyList<LanguageInfo> Languages,
        IReadOnlyList<string> Calendars,
        string TimeZone,
        string? Region,
        DigitStyle? Digits,
        IReadOnlyDictionary<string, JsonElement> Entries)
    {
        /// <summary>
        ///     The format version of this shape, part of its wire tag. Raise it whenever the shape
        ///     changes, so every client refetches.
        /// </summary>
        public const int FormatVersion = 1;
    }
}
