// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The context values a query binds: <c>today()</c>, <c>now()</c>, <c>me</c> and the tenant's
    ///     time zone.
    /// </summary>
    /// <param name="Today">Today's date in the tenant's time zone.</param>
    /// <param name="Now">The unit of work's instant, offset to the tenant's time zone.</param>
    /// <param name="UserId">
    ///     The calling user's id, or null before the connect step has bound one and for a principal
    ///     that is no user.
    /// </param>
    /// <param name="TimeZoneName">
    ///     The tenant's time zone as SQL Server's <c>AT TIME ZONE</c> accepts it: a Windows zone id.
    ///     The display zone formats values and is never bound.
    /// </param>
    public sealed record QueryexContextValues(DateOnly Today, DateTimeOffset Now, int? UserId, string TimeZoneName);
}
