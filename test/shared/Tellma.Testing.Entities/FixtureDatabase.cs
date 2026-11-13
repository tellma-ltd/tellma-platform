// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Data;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Testing.Entities
{
    /// <summary>
    ///     Creates fixture databases as provisioning creates a tenant database: in the platform
    ///     collation, with read-committed snapshot on and the application role in place, migrated with
    ///     the fixture migrations, with change tracking on every table the data layer writes, and with
    ///     the running model's fingerprint recorded as the migrator records it.
    /// </summary>
    public static class FixtureDatabase
    {
        /// <summary>The platform version the fixture records beside a fingerprint.</summary>
        public const string PlatformVersion = "fixture";

        /// <summary>
        ///     How long each of the fixture's own commands may run. Creating and migrating a database is
        ///     slow on a server busy with a whole suite's databases, so it is far above a request's.
        /// </summary>
        private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

        /// <summary>Creates and migrates a fixture database.</summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="databaseName">The new database's name; the caller drops it.</param>
        /// <param name="fingerprint">The storage fingerprint to record, or <see langword="null" /> to record none.</param>
        /// <param name="cancellationToken">Cancels the creation.</param>
        /// <returns>The new database's connection string.</returns>
        public static Task<string> CreateAsync(
            string masterConnectionString, string databaseName, string? fingerprint, CancellationToken cancellationToken = default)
        {
            return CreateAsync(masterConnectionString, databaseName, fingerprint, readCommittedSnapshot: true, cancellationToken);
        }

        /// <summary>
        ///     Creates and migrates a fixture database with read-committed snapshot on or off, for a
        ///     test that holds the migrations to running under either setting.
        /// </summary>
        /// <param name="masterConnectionString">A connection string to the server's <c>master</c> database.</param>
        /// <param name="databaseName">The new database's name; the caller drops it.</param>
        /// <param name="fingerprint">The storage fingerprint to record, or <see langword="null" /> to record none.</param>
        /// <param name="readCommittedSnapshot">Whether the database reads committed data from row versions, as provisioning sets it.</param>
        /// <param name="cancellationToken">Cancels the creation.</param>
        /// <returns>The new database's connection string.</returns>
        public static async Task<string> CreateAsync(
            string masterConnectionString, string databaseName, string? fingerprint, bool readCommittedSnapshot, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(masterConnectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(databaseName);

            // --- The empty database, as provisioning creates it ---
            string quoted = Quote(databaseName);
            await ExecuteAsync(
                masterConnectionString,
                cancellationToken,
                $"CREATE DATABASE {quoted} COLLATE {DatabaseCollations.Platform};",
                $"ALTER DATABASE {quoted} SET READ_COMMITTED_SNAPSHOT {(readCommittedSnapshot ? "ON" : "OFF")} WITH ROLLBACK IMMEDIATE;",
                $"ALTER DATABASE {quoted} SET CHANGE_TRACKING = ON (CHANGE_RETENTION = 1 DAYS, AUTO_CLEANUP = ON);");

            string connectionString = new SqlConnectionStringBuilder(masterConnectionString) { InitialCatalog = databaseName }.ConnectionString;

            // The migrations grant the table types to the application role, which provisioning creates first.
            await ExecuteAsync(connectionString, cancellationToken, $"CREATE ROLE {Quote(DatabaseRoles.Application)};");

            // --- The fixture schema ---
            await using (var context = TellmaFixtureDbContext.Create(connectionString))
            {
                context.Database.SetCommandTimeout(CommandTimeout);
                await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            // --- Change tracking on every table a batch writes: the platform's and the fixture's, never a history table ---
            await ExecuteAsync(connectionString, cancellationToken, """
                DECLARE @sql nvarchar(max) = N'';
                SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.[name]) + N'.' + QUOTENAME(t.[name]) + N' ENABLE CHANGE_TRACKING;' + NCHAR(10)
                FROM sys.tables AS t
                JOIN sys.schemas AS s ON s.[schema_id] = t.[schema_id]
                WHERE s.[name] <> N'dbo' AND t.[temporal_type] <> 1 AND t.[is_ms_shipped] = 0;
                EXEC sys.sp_executesql @sql;
                """);

            // --- The fingerprint row the schema guard reads ---
            if (fingerprint is not null)
            {
                await RecordFingerprintAsync(connectionString, fingerprint, cancellationToken).ConfigureAwait(false);
            }

            return connectionString;
        }

        /// <summary>Records a storage fingerprint, as the migrator does after a migration.</summary>
        /// <param name="connectionString">The fixture database's connection string.</param>
        /// <param name="fingerprint">The fingerprint.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the row is written.</returns>
        public static async Task RecordFingerprintAsync(string connectionString, string fingerprint, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
            ArgumentException.ThrowIfNullOrWhiteSpace(fingerprint);

            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO [dbo].[__TellmaSchema] ([Fingerprint], [MigrationId], [PlatformVersion], [AppliedAt])
                SELECT @fingerprint, ISNULL((SELECT MAX([MigrationId]) FROM [dbo].[__EFMigrationsHistory]), N''), @version, SYSDATETIMEOFFSET();
                """;
            command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
            _ = command.Parameters.Add(new SqlParameter("@fingerprint", SqlDbType.Char, 64) { Value = fingerprint });
            _ = command.Parameters.Add(new SqlParameter("@version", SqlDbType.NVarChar, 64) { Value = PlatformVersion });
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs batches over one connection.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="cancellationToken">Cancels the batches.</param>
        /// <param name="batches">The batches.</param>
        /// <returns>A task that completes when every batch ran.</returns>
        private static async Task ExecuteAsync(string connectionString, CancellationToken cancellationToken, params string[] batches)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            foreach (string batch in batches)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = batch;
                command.CommandTimeout = (int)CommandTimeout.TotalSeconds;
                _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>A name in brackets, its closing brackets doubled.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The quoted name.</returns>
        private static string Quote(string name)
        {
            return "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
        }
    }
}
