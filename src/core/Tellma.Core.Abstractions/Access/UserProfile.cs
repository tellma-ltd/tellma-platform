// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;

namespace Tellma.Core.Abstractions.Access
{
    // The profile is spec 0014 §7.1; the connect prologue's result set 3 reads it (§7.3).
    /// <summary>
    ///     The connected user's own row as the platform keeps it in memory: the names, the contact
    ///     details, the images and the stored locale preferences. Cached per user and valid while the
    ///     user's preferences tag is the one it was read under.
    /// </summary>
    /// <param name="Name">The user's name in the tenant's primary language.</param>
    /// <param name="Name2">The name in the secondary language.</param>
    /// <param name="Name3">The name in the tertiary language.</param>
    /// <param name="Email">The sign-in email address; null for a service account and the system user.</param>
    /// <param name="ContactEmail">The address the user is contacted at, when it differs from the sign-in address.</param>
    /// <param name="ContactMobile">The mobile number the user is contacted at.</param>
    /// <param name="ImageId">The blob of the user's picture.</param>
    /// <param name="SignatureId">The blob of the user's signature.</param>
    /// <param name="PreferredLanguage">The language the user prefers, a language tag.</param>
    /// <param name="PreferredCalendar">The calendar the user prefers, a calendar code.</param>
    /// <param name="PreferredTimeZone">The display zone the user prefers, an IANA name.</param>
    /// <param name="PreferredRegion">The region whose formats the user prefers, two letters.</param>
    /// <param name="PreferredDigits">The digits the user prefers.</param>
    /// <param name="Gender">The user's grammatical gender, for messages that agree with it.</param>
    /// <param name="ModifiedAt">When the row was last saved: the stamp a self-service save compares.</param>
    public sealed record UserProfile(
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
        DateTimeOffset ModifiedAt)
    {
        /// <inheritdoc cref="UserProfile(string, string?, string?, string?, string?, string?, int?, int?, string?, string?, string?, string?, DigitStyle?, Gender?, DateTimeOffset)" />
        public string Name { get; init => field = value ?? throw new ArgumentNullException(nameof(Name)); } =
            Name ?? throw new ArgumentNullException(nameof(Name));
    }
}
