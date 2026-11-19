// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Settings
{
    /// <summary>Source-generated log messages of the tenant settings. No message carries a setting entry's value.</summary>
    internal static partial class SettingsLog
    {
        /// <summary>A stored setting names no declared key, as after a pack was removed; the row is ignored.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The stored key.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsEntryUndeclared",
            Level = LogLevel.Warning,
            Message = "Tenant {TenantId} stores the setting {Key}, which no composed feature declares; the stored value is ignored.")]
        public static partial void SettingsEntryUndeclared(ILogger logger, string key, int tenantId);

        /// <summary>A stored setting does not read as its key's type, or its key refuses it; the key's default is used.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The key.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsEntryInvalid",
            Level = LogLevel.Warning,
            Message = "Tenant {TenantId} stores a value of the setting {Key} that does not read as the key's type or that the key refuses; the key's default is used.")]
        public static partial void SettingsEntryInvalid(ILogger logger, string key, int tenantId);

        /// <summary>
        ///     A stored secret could not be unprotected, as after a restore under another tenant id, key
        ///     ring or application name, or in a host without data protection; its default is used.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="key">The key.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsSecretUnreadable",
            Level = LogLevel.Critical,
            Message = "Tenant {TenantId} stores a value of the secret setting {Key} that this host cannot unprotect; the key's default is used until an administrator saves it again.")]
        public static partial void SettingsSecretUnreadable(ILogger logger, string key, int tenantId);

        /// <summary>
        ///     A typed setting names something this composition does not know, as after a pack was
        ///     removed; the platform default stands in for it.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="column">The column of <c>core.Settings</c>.</param>
        /// <param name="value">The stored value: a language, calendar, zone or digits code, never a setting entry.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsValueUnresolvable",
            Level = LogLevel.Critical,
            Message = "Tenant {TenantId} stores {Value} in the setting column {Column}, which this composition cannot resolve; the platform default is used.")]
        public static partial void SettingsValueUnresolvable(ILogger logger, string column, string value, int tenantId);

        /// <summary>
        ///     The routed tenant's database belongs to another tenant, as after a catalog typo, a backup
        ///     restored under the wrong name or a sandbox cloned from live; the load fails closed.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="expected">The routed tenant.</param>
        /// <param name="actual">The tenant the database's settings name; zero for an unprovisioned database.</param>
        [LoggerMessage(
            EventName = "TenantDatabaseMismatch",
            Level = LogLevel.Critical,
            Message = "The database routed for tenant {Expected} holds the settings of tenant {Actual}; nothing is served from it.")]
        public static partial void TenantDatabaseMismatch(ILogger logger, int expected, int actual);

        /// <summary>A tenant's settings a batch read were not stored because the store was already disposed, as while the host stops.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsNotStored",
            Level = LogLevel.Information,
            Message = "The settings of tenant {TenantId} were read but not stored: the cache is already disposed, as it is while the host stops.")]
        public static partial void SettingsNotStored(ILogger logger, int tenantId);

        /// <summary>
        ///     The tenant catalog could not mirror the names a committed settings save changed; the
        ///     save stands, and the next save that changes the names or the languages mirrors them again.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "SettingsNamesMirrorFailed",
            Level = LogLevel.Error,
            Message = "The tenant catalog did not mirror the display names of tenant {TenantId} after a committed settings save; the catalog shows the former names until a later save that changes them succeeds in mirroring them.")]
        public static partial void SettingsNamesMirrorFailed(ILogger logger, Exception exception, int tenantId);
    }
}
