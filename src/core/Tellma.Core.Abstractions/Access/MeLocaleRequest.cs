// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Access
{
    /// <summary>
    ///     A change of the caller's own locale: each member set is stored, each member named in
    ///     <paramref name="Reset" /> returns to the tenant's default, and a null member is left as
    ///     stored. A request sets or resets at least one member, and never both for one member.
    /// </summary>
    /// <param name="Language">The preferred language, a bare language code the distribution offers; null to leave it.</param>
    /// <param name="Calendar">The preferred calendar; null to leave it.</param>
    /// <param name="TimeZone">The preferred time zone, an IANA zone the platform knows; null to leave it.</param>
    /// <param name="Region">The preferred region, which the regional formats follow; null to leave it.</param>
    /// <param name="Digits">The preferred digits; null to leave them.</param>
    /// <param name="Reset">The members to return to the tenant's default; null or empty for none.</param>
    public sealed record MeLocaleRequest(
        string? Language,
        string? Calendar,
        string? TimeZone,
        string? Region,
        DigitStyle? Digits,
        IReadOnlyList<MeLocaleMember>? Reset = null);
}
