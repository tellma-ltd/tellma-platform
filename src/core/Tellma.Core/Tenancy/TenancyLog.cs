// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>Source-generated log messages of the tenancy runtime.</summary>
    /// <remarks>
    ///     Messages name tenants by id and servers by name, never a connection string, a profile's
    ///     value, a credential or an email address.
    /// </remarks>
    internal static partial class TenancyLog
    {
        /// <summary>The registry reloaded its snapshot.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="changed">How many descriptors changed, new tenants included.</param>
        /// <param name="tenants">How many tenants the snapshot serves.</param>
        [LoggerMessage(
            EventName = "TenantSnapshotReloaded",
            Level = LogLevel.Information,
            Message = "The tenant snapshot was reloaded: {Changed} descriptor(s) changed, {Tenants} tenant(s) served.")]
        public static partial void SnapshotReloaded(ILogger logger, int changed, int tenants);

        /// <summary>A refresh failed while the snapshot is still within its staleness bound.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "TenantSnapshotRefreshFailed",
            Level = LogLevel.Warning,
            Message = "The tenant snapshot could not be refreshed; the current snapshot is still within its staleness bound.")]
        public static partial void RefreshFailed(ILogger logger, Exception exception);

        /// <summary>A refresh failed and the snapshot is beyond its staleness bound, or was never loaded.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "TenantSnapshotRefreshFailedStale",
            Level = LogLevel.Error,
            Message = "The tenant snapshot could not be refreshed and is beyond its staleness bound or was never loaded; every tenant request answers catalog-unavailable.")]
        public static partial void RefreshFailedStale(ILogger logger, Exception exception);

        /// <summary>A row names a server outside the allowed servers, or a server the location grammar refuses.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="server">The server the row names.</param>
        [LoggerMessage(
            EventName = "TenantServerRefused",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId} names the server '{Server}', which is malformed or outside Tellma:Sql:AllowedServers; the tenant is left out of the snapshot.")]
        public static partial void TenantServerRefused(ILogger logger, int tenantId, string server);

        /// <summary>A row's database name breaks the location grammar.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "TenantDatabaseRefused",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId} names a database the location grammar refuses; the tenant is left out of the snapshot.")]
        public static partial void TenantDatabaseRefused(ILogger logger, int tenantId);

        /// <summary>A row names a profile that is not configured, or one that is not a valid fragment.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="profile">The profile's name.</param>
        [LoggerMessage(
            EventName = "TenantProfileRefused",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId} uses the credential profile '{Profile}', which is not configured under Tellma:Sql:Profiles or breaks the connection rules core.sql-profiles reports; the tenant is left out of the snapshot.")]
        public static partial void TenantProfileRefused(ILogger logger, int tenantId, string profile);

        /// <summary>A row holds a category or state this runtime does not know.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        [LoggerMessage(
            EventName = "TenantRowUnknown",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId} holds a category or state this platform version does not know; the tenant is left out of the snapshot.")]
        public static partial void TenantRowUnknown(ILogger logger, int tenantId);

        /// <summary>More tenants use warm profiles than the configured bound.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="warmTenants">How many tenants use a profile with a non-zero minimum pool size.</param>
        /// <param name="maxWarmTenants">The configured bound.</param>
        [LoggerMessage(
            EventName = "WarmTenantsExceeded",
            Level = LogLevel.Warning,
            Message = "{WarmTenants} tenants use credential profiles with a non-zero Min Pool Size, above Tellma:Sql:MaxWarmTenants ({MaxWarmTenants}); their idle connections count against the server's session limit.")]
        public static partial void WarmTenantsExceeded(ILogger logger, int warmTenants, int maxWarmTenants);

        /// <summary>The first load at startup failed; the host starts not ready and keeps retrying.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "TenantSnapshotFirstLoadFailed",
            Level = LogLevel.Warning,
            Message = "The tenant snapshot could not be loaded at startup; the host starts not ready and retries every refresh interval.")]
        public static partial void FirstLoadFailed(ILogger logger, Exception exception);

        /// <summary>The catalog database could not be opened at startup, outside Development.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="command">The migrator command that creates and migrates the catalog.</param>
        [LoggerMessage(
            EventName = "CatalogDatabaseUnavailable",
            Level = LogLevel.Error,
            Message = "The catalog database could not be opened: it is missing, or this host's principal has no access to it. The host starts not ready. {Command}")]
        public static partial void CatalogDatabaseUnavailable(ILogger logger, string command);

        /// <summary>A refresh after startup found the catalog behind the platform's catalog migrations.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="command">The migrator command that migrates the catalog.</param>
        [LoggerMessage(
            EventName = "CatalogBehind",
            Level = LogLevel.Error,
            Message = "The catalog lacks the platform's latest catalog migration; no tenant is served until it is migrated. {Command}")]
        public static partial void CatalogBehind(ILogger logger, string command);

        /// <summary>A tenant-state listener threw.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="listener">The listener's type.</param>
        /// <param name="tenantId">The tenant it was told about.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "TenantStateListenerFailed",
            Level = LogLevel.Error,
            Message = "The tenant-state listener {Listener} failed for tenant {TenantId}.")]
        public static partial void ListenerFailed(ILogger logger, string listener, int tenantId, Exception exception);

        /// <summary>A tenant-state listener outlasted its bound; the queue moved on.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="listener">The listener's type.</param>
        /// <param name="tenantId">The tenant it was told about.</param>
        /// <param name="timeout">The bound.</param>
        [LoggerMessage(
            EventName = "TenantStateListenerTimedOut",
            Level = LogLevel.Error,
            Message = "The tenant-state listener {Listener} did not finish within {Timeout} for tenant {TenantId}; the next notification proceeds.")]
        public static partial void ListenerTimedOut(ILogger logger, string listener, int tenantId, TimeSpan timeout);

        /// <summary>A tenant was registered.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="category">Its category.</param>
        /// <param name="actor">Who registered it.</param>
        [LoggerMessage(
            EventName = "TenantRegistered",
            Level = LogLevel.Information,
            Message = "Tenant {TenantId} was registered as a {Category} tenant by {Actor}.")]
        public static partial void TenantRegistered(ILogger logger, int tenantId, TenantCategory category, string actor);

        /// <summary>A tenant changed state.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="from">The state it left.</param>
        /// <param name="to">The state it entered.</param>
        /// <param name="actor">Who moved it.</param>
        [LoggerMessage(
            EventName = "TenantStateChanged",
            Level = LogLevel.Information,
            Message = "Tenant {TenantId} moved from {From} to {To}, by {Actor}.")]
        public static partial void StateChanged(ILogger logger, int tenantId, TenantState from, TenantState to, string actor);

        /// <summary>A tenant was relocated.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="server">Its new server.</param>
        /// <param name="database">Its new database.</param>
        /// <param name="profile">Its new profile's name.</param>
        /// <param name="actor">Who moved it.</param>
        [LoggerMessage(
            EventName = "TenantRelocated",
            Level = LogLevel.Information,
            Message = "Tenant {TenantId} was relocated to database '{Database}' on '{Server}' under profile '{Profile}', by {Actor}.")]
        public static partial void Relocated(ILogger logger, int tenantId, string server, string database, string profile, string actor);

        /// <summary>The refresh after a committed catalog write failed; the loop converges.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant written.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "TenantWriteRefreshFailed",
            Level = LogLevel.Warning,
            Message = "The write to tenant {TenantId} committed, but the registry could not be refreshed after it; the refresh loop picks it up.")]
        public static partial void WriteRefreshFailed(ILogger logger, int tenantId, Exception exception);

        /// <summary>A membership-hint write failed.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="records">How many hints the write carried.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MembershipHintWriteFailed",
            Level = LogLevel.Warning,
            Message = "{Records} membership hint(s) could not be recorded; the next reconciliation repairs them.")]
        public static partial void MembershipWriteFailed(ILogger logger, int records, Exception exception);
    }
}
