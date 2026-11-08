// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Identity.Data;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Seeding
{
    /// <summary>
    ///     The check the engine runs on its store at every start, before seeding writes anything:
    ///     an existing database must be in the platform collation, and its migration history must
    ///     hold the engine's last migration. With <see cref="TellmaIdentitySeedOptions.ApplyMigrations" />
    ///     on — development and test hosts only — the engine first creates a missing database and
    ///     applies its pending migrations; otherwise it never creates or migrates anything.
    /// </summary>
    /// <remarks>
    ///     A failure throws <see cref="IdentityStoreCheckException" />, which stops the host as any
    ///     failed seeding does. Later history rows than the engine's last migration pass, so the
    ///     previous engine release runs on a store an upgrade has already migrated.
    /// </remarks>
    /// <param name="context">The identity store.</param>
    /// <param name="options">The engine options (the seed section).</param>
    /// <param name="logger">Progress logging.</param>
    public sealed class IdentityStoreCheck(
        TellmaIdentityDbContext context,
        IOptions<TellmaIdentityOptions> options,
        ILogger<IdentityStoreCheck> logger)
    {
        /// <summary>The <c>SERVERPROPERTY('EngineEdition')</c> value of Azure SQL Database.</summary>
        internal const int AzureSqlDatabaseEngineEdition = 5;

        /// <summary>The catalog collation an Azure SQL Database platform database is created with.</summary>
        internal const string AzureCatalogCollation = "DATABASE_DEFAULT";

        /// <summary>Runs the check, migrating first when the seed options allow it.</summary>
        /// <param name="cancellationToken">Aborts the check.</param>
        /// <returns>A task that completes when the store has passed.</returns>
        /// <exception cref="IdentityStoreCheckException">The store failed the check.</exception>
        public async Task CheckAsync(CancellationToken cancellationToken)
        {
            TellmaIdentitySeedOptions seed = options.Value.Seed;
            DatabaseFacade database = context.Database;
            string databaseName = database.GetDbConnection().Database;

            // Step 1 (spec 0010 §A.11): an existing database's collation. It runs ahead of any
            // migration, so a database in another collation is refused whether or not this host
            // may migrate, and nothing is ever written to it.
            bool exists = await ReadAsync(
                databaseName,
                () => database.GetService<IRelationalDatabaseCreator>().ExistsAsync(cancellationToken));
            if (exists)
            {
                await CheckCollationAsync(databaseName, cancellationToken);
            }

            // Step 2: development and test hosts create and migrate their own store. The context
            // must name the engine's migrations assembly: without it EF would create an empty
            // database with no identity schema, so the check refuses before anything is created.
            if (seed.ApplyMigrations)
            {
                IMigrationsAssembly migrationsAssembly = database.GetService<IMigrationsAssembly>();
                if (!migrationsAssembly.Migrations.ContainsKey(TellmaIdentityConstants.LatestMigration))
                {
                    throw new IdentityStoreCheckException(
                        $"The identity store check failed: TellmaIdentity:Seed:ApplyMigrations is on, but the identity "
                            + $"store's migrations assembly '{migrationsAssembly.Assembly.GetName().Name}' does not contain "
                            + $"the engine's migration '{TellmaIdentityConstants.LatestMigration}'. Reference "
                            + $"{TellmaIdentityConstants.MigrationsAssemblyName} and configure the identity store with "
                            + $"MigrationsAssembly(\"{TellmaIdentityConstants.MigrationsAssemblyName}\"), or turn "
                            + "ApplyMigrations off and migrate the store outside the host.");
                }

                IdentityStoreCheckLog.ApplyingMigrations(logger, databaseName);
                await database.MigrateAsync(cancellationToken);

                // A database that was missing a moment ago now exists: EF created it from the
                // model, or a concurrent start did. Either way it gets the same collation check
                // as one that existed all along.
                if (!exists)
                {
                    await CheckCollationAsync(databaseName, cancellationToken);
                }
            }
            else if (!exists)
            {
                throw new IdentityStoreCheckException(
                    $"The identity store check failed: database '{databaseName}' does not exist, so it holds no "
                        + $"identity schema '{TellmaIdentityConstants.Schema}' migrated to "
                        + $"'{TellmaIdentityConstants.LatestMigration}'. {Remedy(seed)}");
            }

            // Step 3: the migration history must hold the engine's last migration. The constant is
            // read, never the migrations assembly, so a host that never migrates carries none.
            await CheckHistoryAsync(databaseName, seed, cancellationToken);

            IdentityStoreCheckLog.StorePassed(logger, databaseName);
        }

        /// <summary>
        ///     Describes why a database's collations fail the platform rule, or returns null when they
        ///     pass.
        /// </summary>
        /// <param name="databaseName">The database, as the message names it.</param>
        /// <param name="collation">The database's collation (<c>DATABASEPROPERTYEX(DB_NAME(), 'Collation')</c>).</param>
        /// <param name="engineEdition">The server's <c>SERVERPROPERTY('EngineEdition')</c>.</param>
        /// <param name="catalogCollation">
        ///     The database's <c>catalog_collation_type_desc</c>; read on Azure SQL Database only, and
        ///     ignored elsewhere.
        /// </param>
        /// <returns>The failure message, or null when the database is in the platform collation.</returns>
        internal static string? DescribeCollationFailure(
            string databaseName, string? collation, int engineEdition, string? catalogCollation)
        {
            if (!string.Equals(collation, DatabaseCollations.Platform, StringComparison.Ordinal))
            {
                return $"The identity store check failed: database '{databaseName}' uses the collation "
                    + $"'{collation ?? "(none)"}', but every platform database must use '{DatabaseCollations.Platform}'. "
                    + "A database's collation cannot be changed in place: re-create it in the platform collation "
                    + "and migrate it again.";
            }

            // On Azure SQL Database the system catalog has a collation of its own, and a platform
            // database is created so that it follows the database's (spec 0011 §6.7).
            return engineEdition == AzureSqlDatabaseEngineEdition
                && !string.Equals(catalogCollation, AzureCatalogCollation, StringComparison.Ordinal)
                    ? $"The identity store check failed: database '{databaseName}' uses the catalog collation "
                        + $"'{catalogCollation ?? "(none)"}', but a platform database on Azure SQL Database must use "
                        + $"'{AzureCatalogCollation}'. It must be re-created WITH CATALOG_COLLATION = {AzureCatalogCollation}."
                    : null;
        }

        /// <summary>The sentence a failed history check ends with: the configured command, or the general remedy.</summary>
        /// <param name="seed">The seed options.</param>
        /// <returns>The remedy sentence.</returns>
        internal static string Remedy(TellmaIdentitySeedOptions seed)
        {
            return string.IsNullOrWhiteSpace(seed.MigrateCommand)
                ? $"Apply the engine's migrations ({TellmaIdentityConstants.MigrationsAssemblyName}) to it before starting the engine."
                : $"Apply the engine's migrations before starting the engine: {seed.MigrateCommand}";
        }

        /// <summary>Reads the database's collations and refuses any but the platform's.</summary>
        private async Task CheckCollationAsync(string databaseName, CancellationToken cancellationToken)
        {
            DatabaseFacade database = context.Database;
            await ReadAsync(databaseName, async () =>
            {
                await database.OpenConnectionAsync(cancellationToken);
                return true;
            });

            try
            {
                (string? collation, int engineEdition) = await ReadAsync(databaseName, async () =>
                {
                    await using DbCommand command = CreateCommand(
                        "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation')), "
                            + "CONVERT(int, SERVERPROPERTY('EngineEdition'));");
                    await using DbDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
                    await reader.ReadAsync(cancellationToken);
                    return (reader.IsDBNull(0) ? null : reader.GetString(0), reader.GetInt32(1));
                });

                // The catalog collation column exists on Azure SQL Database and recent SQL Server
                // releases only, so it is queried only where the rule applies.
                string? catalogCollation = null;
                if (engineEdition == AzureSqlDatabaseEngineEdition)
                {
                    catalogCollation = await ReadAsync(databaseName, async () =>
                    {
                        await using DbCommand command = CreateCommand(
                            "SELECT [catalog_collation_type_desc] FROM [sys].[databases] WHERE [database_id] = DB_ID();");
                        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
                    });
                }

                string? failure = DescribeCollationFailure(databaseName, collation, engineEdition, catalogCollation);
                if (failure is not null)
                {
                    throw new IdentityStoreCheckException(failure);
                }
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
        }

        /// <summary>Reads the migration history and refuses one without the engine's last migration.</summary>
        private async Task CheckHistoryAsync(
            string databaseName, TellmaIdentitySeedOptions seed, CancellationToken cancellationToken)
        {
            // The history repository reads the table the store's context is configured with, which
            // is where its migrations were recorded: dbo.__EFMigrationsHistory by default.
            IHistoryRepository history = context.Database.GetService<IHistoryRepository>();
            bool hasHistory = await ReadAsync(databaseName, () => history.ExistsAsync(cancellationToken));
            if (!hasHistory)
            {
                throw new IdentityStoreCheckException(
                    $"The identity store check failed: database '{databaseName}' has no migration history, so its "
                        + $"identity schema '{TellmaIdentityConstants.Schema}' was never migrated to "
                        + $"'{TellmaIdentityConstants.LatestMigration}'. {Remedy(seed)}");
            }

            IReadOnlyList<HistoryRow> rows = await ReadAsync(
                databaseName, () => history.GetAppliedMigrationsAsync(cancellationToken));
            if (!rows.Any(static row => string.Equals(row.MigrationId, TellmaIdentityConstants.LatestMigration, StringComparison.Ordinal)))
            {
                throw new IdentityStoreCheckException(
                    $"The identity store check failed: the identity schema '{TellmaIdentityConstants.Schema}' in "
                        + $"database '{databaseName}' lacks the migration '{TellmaIdentityConstants.LatestMigration}' "
                        + $"this engine requires. {Remedy(seed)}");
            }
        }

        /// <summary>Creates a command on the store's open connection.</summary>
        private DbCommand CreateCommand(string sql)
        {
            DbCommand command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = sql;
            return command;
        }

        /// <summary>
        ///     Runs one read of the check, turning a database failure (an unreachable server, a
        ///     missing permission) into a check failure that names the database.
        /// </summary>
        private static async Task<T> ReadAsync<T>(string databaseName, Func<Task<T>> read)
        {
            try
            {
                return await read();
            }
            catch (DbException exception)
            {
                throw new IdentityStoreCheckException(
                    $"The identity store check could not read database '{databaseName}': {exception.Message}",
                    exception);
            }
        }
    }

    /// <summary>Source-generated log messages for <see cref="IdentityStoreCheck" />.</summary>
    internal static partial class IdentityStoreCheckLog
    {
        /// <summary>Migrations are about to be applied.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The store's database.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Applying pending identity store migrations to database {Database}.")]
        public static partial void ApplyingMigrations(ILogger logger, string database);

        /// <summary>The store passed the check.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="database">The store's database.</param>
        [LoggerMessage(Level = LogLevel.Information, Message = "Identity store in database {Database} passed the store check.")]
        public static partial void StorePassed(ILogger logger, string database);
    }
}
