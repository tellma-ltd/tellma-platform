// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Settings;

namespace Tellma.Core.Abstractions.Localization
{
    /// <summary>What locale negotiation weighs for one reader: request headers, their stored preferences, a surface's fixed choices, and the tenant's settings.</summary>
    /// <param name="AcceptLanguage">The <c>Accept-Language</c> header's value, if any.</param>
    /// <param name="CalendarHeader">The requested calendar code, if any.</param>
    /// <param name="TimeZoneHeader">The requested display zone, if any.</param>
    /// <param name="PreferredLanguage">The reader's stored language, if any.</param>
    /// <param name="PreferredCalendar">The reader's stored calendar, if any.</param>
    /// <param name="PreferredTimeZone">The reader's stored zone, if any.</param>
    /// <param name="PreferredRegion">The reader's stored region, if any.</param>
    /// <param name="MessageLanguage">A surface's fixed message language (the agent surface passes one); null on the web.</param>
    /// <param name="MessageCalendar">A surface's fixed message calendar; null on the web.</param>
    /// <param name="Settings">The tenant's settings.</param>
    public sealed record NegotiationInput(
        string? AcceptLanguage,
        string? CalendarHeader,
        string? TimeZoneHeader,
        string? PreferredLanguage,
        string? PreferredCalendar,
        string? PreferredTimeZone,
        string? PreferredRegion,
        string? MessageLanguage,
        string? MessageCalendar,
        TenantSettings Settings);
}
