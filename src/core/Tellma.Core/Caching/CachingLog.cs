// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Caching
{
    /// <summary>Source-generated log messages of the version tags. No message carries a tag's value.</summary>
    internal static partial class CachingLog
    {
        /// <summary>
        ///     A prelude found no row for a registered tag: the kind behind it is uncacheable on this
        ///     instance, and a write that bumps it fails, until the migrator seeds the row. Logged once
        ///     per name per process.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="name">The tag's name.</param>
        /// <param name="tenantId">The tenant whose database lacks it.</param>
        [LoggerMessage(
            EventName = "VersionTagRowMissing",
            Level = LogLevel.Warning,
            Message = "Tenant {TenantId} has no row for the version tag {Name}, so what it versions is not cached on this instance and writes that bump it fail until the migrator seeds it.")]
        public static partial void VersionTagRowMissing(ILogger logger, string name, int tenantId);

        /// <summary>A read or validate batch was discarded because a cached input it re-runs on moved under it.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="names">The moved tags' names, comma-separated.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "StaleVersionTagRetry",
            Level = LogLevel.Information,
            Message = "A batch of tenant {TenantId} was discarded because the version tags {Names} it depended on moved; the request may run it again.")]
        public static partial void StaleVersionTagRetry(ILogger logger, string names, int tenantId);

        /// <summary>The migrator's seed inserted the row of every registered tag the tenant database lacked.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="registered">How many tags are registered.</param>
        [LoggerMessage(
            EventName = "VersionTagsSeeded",
            Level = LogLevel.Information,
            Message = "The database of tenant {TenantId} has a row for each of the {Registered} registered version tags.")]
        public static partial void VersionTagsSeeded(ILogger logger, int tenantId, int registered);

        /// <summary>The migrator moved every version tag of a tenant database after a run that changed it.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "VersionTagsBumped",
            Level = LogLevel.Information,
            Message = "Moved every version tag in the database of tenant {TenantId}, since this run changed it.")]
        public static partial void VersionTagsBumped(ILogger logger, int tenantId);
    }
}
