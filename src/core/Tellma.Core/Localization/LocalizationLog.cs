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

        /// <summary>
        ///     A resource key no culture of the lookup's chain holds, the neutral English resources
        ///     included: the reader sees the key itself. Logged once per key and culture per process.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="baseName">The base name of the resources looked in.</param>
        /// <param name="key">The key.</param>
        /// <param name="culture">The culture the lookup started from.</param>
        [LoggerMessage(
            EventName = "MissingResourceString",
            Level = LogLevel.Warning,
            Message = "The resource {Key} is missing from {BaseName} in every culture from {Culture} to the neutral resources; the key is shown instead.")]
        public static partial void MissingResourceString(ILogger logger, string baseName, string key, string culture);

        /// <summary>
        ///     A resource value that is not a valid ICU message, or that cannot be formatted with the
        ///     arguments it was given: the reader sees the raw value. Logged once per key and culture
        ///     per process.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The formatting failure.</param>
        /// <param name="baseName">The base name of the resources.</param>
        /// <param name="key">The key.</param>
        /// <param name="culture">The culture the value was formatted in.</param>
        [LoggerMessage(
            EventName = "MessageFormatMalformed",
            Level = LogLevel.Warning,
            Message = "The resource {Key} of {BaseName} could not be formatted as an ICU message in {Culture}; its raw value is shown instead.")]
        public static partial void MessageFormatMalformed(ILogger logger, Exception exception, string baseName, string key, string culture);
    }
}
