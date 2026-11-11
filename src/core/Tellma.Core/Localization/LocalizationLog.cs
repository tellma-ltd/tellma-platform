// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Localization
{
    /// <summary>Source-generated log messages of localization.</summary>
    internal static partial class LocalizationLog
    {
        /// <summary>
        ///     A zone a user can pick is one this host cannot resolve: the sign of a container image
        ///     with stale time-zone data. A reader who asks for it gets their tenant's zone instead.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="id">The zone's id.</param>
        [LoggerMessage(
            EventName = "TimeZoneUnresolvable",
            Level = LogLevel.Warning,
            Message = "This host's time-zone data cannot resolve the zone {Id}; update the image's tzdata.")]
        public static partial void TimeZoneUnresolvable(ILogger logger, string id);
    }
}
