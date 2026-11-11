// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Calendars;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Where a request context starts: the platform's defaults (English, the Gregorian calendar,
    ///     UTC) stamped with the host's clock. The tenantless context is exactly this; a tenant
    ///     binding copies it and sets the tenant and the principal. A singleton.
    /// </summary>
    /// <param name="calendars">The composed calendar systems, the Gregorian one among them.</param>
    /// <param name="time">The host's clock.</param>
    internal sealed class RequestContextDefaults(ICalendarRegistry calendars, TimeProvider time)
    {
        /// <summary>The tenantless context of a unit of work that starts now.</summary>
        /// <returns>The context.</returns>
        public RequestContext Tenantless()
        {
            return RequestContext.Tenantless(calendars[CalendarCodes.Gregorian], time.GetUtcNow());
        }
    }
}
