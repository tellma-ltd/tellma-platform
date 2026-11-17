// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Migrator
{
    /// <summary>Source-generated log messages of the migrator.</summary>
    /// <remarks>Messages name tenants by id and databases by name, never a connection string or an email address.</remarks>
    internal static partial class MigratorLog
    {
        /// <summary>The category the migrator's own messages are logged under.</summary>
        public const string Category = "Tellma.Core.Migrator";

        /// <summary>A database's migrations were applied.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The database.</param>
        /// <param name="count">How many migrations were applied.</param>
        [LoggerMessage(
            EventName = "MigratorMigrationsApplied",
            Level = LogLevel.Information,
            Message = "{Count} migration(s) applied to database {Database}.")]
        public static partial void MigrationsApplied(ILogger logger, string database, int count);

        /// <summary>A database was created in the platform collation.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The database.</param>
        /// <param name="server">The server.</param>
        [LoggerMessage(
            EventName = "MigratorDatabaseCreated",
            Level = LogLevel.Information,
            Message = "Database {Database} was created on server {Server}.")]
        public static partial void DatabaseCreated(ILogger logger, string database, string server);

        /// <summary>
        ///     A database's creation failed only because SQL Server could not lock the <c>model</c>
        ///     database, and runs again after a wait.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The database.</param>
        /// <param name="server">The server.</param>
        /// <param name="attempt">The attempt that failed, from 1.</param>
        /// <param name="maxAttempts">How many attempts the creation has.</param>
        /// <param name="retryDelay">The wait before the next attempt.</param>
        [LoggerMessage(
            EventName = "MigratorDatabaseCreationRetrying",
            Level = LogLevel.Warning,
            Message = "Database {Database} could not be created on server {Server} because the model database was locked (attempt {Attempt} of {MaxAttempts}); retrying in {RetryDelay}.")]
        public static partial void DatabaseCreationRetrying(ILogger logger, string database, string server, int attempt, int maxAttempts, TimeSpan retryDelay);

        /// <summary>
        ///     A database's creation found the <c>model</c> database locked on its last attempt; the
        ///     creation's failure follows as its database's failure.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The database.</param>
        /// <param name="server">The server.</param>
        /// <param name="attempts">How many attempts were made.</param>
        [LoggerMessage(
            EventName = "MigratorDatabaseCreationGaveUp",
            Level = LogLevel.Warning,
            Message = "Database {Database} could not be created on server {Server} in {Attempts} attempt(s) because the model database stayed locked; a session left open in model, or other database creations on the server, hold it. Run the migrator again once it is free.")]
        public static partial void DatabaseCreationGaveUp(ILogger logger, string database, string server, int attempts);

        /// <summary>A tenant database is ahead of this migrator: it got no fingerprint row and nothing ran after the migration.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="migrationId">The migrator's own last migration.</param>
        [LoggerMessage(
            EventName = "MigratorTenantAhead",
            Level = LogLevel.Warning,
            Message = "Tenant {TenantId}'s database is ahead of this migrator, whose last migration is {MigrationId}; it was not stamped and its steps did not run.")]
        public static partial void TenantAhead(ILogger logger, int tenantId, string migrationId);

        /// <summary>A tenant's command failed; the run went on with the other tenants.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MigratorTenantFailed",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId} failed.")]
        public static partial void TenantFailed(ILogger logger, int tenantId, Exception exception);

        /// <summary>
        ///     A tenant's run failed after it changed the database, and moving the version tags on the
        ///     way out failed too: the tenant's caches keep their tags until a write moves them.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="exception">The bump's failure.</param>
        [LoggerMessage(
            EventName = "MigratorVersionTagBumpFailed",
            Level = LogLevel.Error,
            Message = "Tenant {TenantId}'s run failed after it changed the database, and its version tags could not be moved either; run migrate again.")]
        public static partial void VersionTagBumpFailed(ILogger logger, int tenantId, Exception exception);

        /// <summary>The catalog's part of a command failed, which ends the command.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MigratorCatalogFailed",
            Level = LogLevel.Error,
            Message = "The catalog could not be brought up to date.")]
        public static partial void CatalogFailed(ILogger logger, Exception exception);

        /// <summary>A command failed outside any tenant's guarded work.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        [LoggerMessage(
            EventName = "MigratorCommandFailed",
            Level = LogLevel.Error,
            Message = "The migrator command failed.")]
        public static partial void CommandFailed(ILogger logger, Exception exception);
    }
}
