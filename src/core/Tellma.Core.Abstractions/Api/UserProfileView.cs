// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>The caller's own profile, as the <c>me</c> envelope carries it.</summary>
    /// <param name="Id">The caller's user id.</param>
    /// <param name="Kind">Whether the caller is an individual or a service account.</param>
    /// <param name="State">How far the caller's invitation got.</param>
    /// <param name="Name">The name in the tenant's first content language.</param>
    /// <param name="Name2">The name in the second content language; null for none.</param>
    /// <param name="Name3">The name in the third content language; null for none.</param>
    /// <param name="Email">The sign-in email; null for a service account.</param>
    /// <param name="ContactEmail">The address notifications go to instead of the sign-in email; null for the sign-in email.</param>
    /// <param name="ContactMobile">The mobile number, in international format; null for none.</param>
    /// <param name="ImageId">The profile picture's blob; null for none.</param>
    /// <param name="SignatureId">The signature's blob; null for none.</param>
    /// <param name="PreferredLanguage">The preferred language; null for the tenant's default.</param>
    /// <param name="PreferredCalendar">The preferred calendar; null for the tenant's default.</param>
    /// <param name="PreferredTimeZone">The preferred time zone; null for the tenant's default.</param>
    /// <param name="PreferredRegion">The preferred region; null for the language's default.</param>
    /// <param name="PreferredDigits">The preferred digits; null for the language's default.</param>
    /// <param name="Gender">The gender; null when not stated.</param>
    /// <param name="ModifiedAt">The row's stamp, which the caller's next profile edit echoes.</param>
    public sealed record UserProfileView(
        int Id,
        UserKind Kind,
        UserState State,
        string Name,
        string? Name2,
        string? Name3,
        string? Email,
        string? ContactEmail,
        string? ContactMobile,
        int? ImageId,
        int? SignatureId,
        string? PreferredLanguage,
        string? PreferredCalendar,
        string? PreferredTimeZone,
        string? PreferredRegion,
        DigitStyle? PreferredDigits,
        Gender? Gender,
        DateTimeOffset ModifiedAt);
}
