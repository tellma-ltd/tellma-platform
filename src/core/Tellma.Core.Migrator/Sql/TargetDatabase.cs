// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>
    ///     One database the migrator prepares: created in the platform collation when it is missing,
    ///     checked when it exists, and told apart as fresh when its model's history table is absent.
    /// </summary>
    /// <param name="connectionString">The database's connection string, as the migrator connects to it.</param>
    internal sealed class TargetDatabase(string connectionString)
    {
        /// <summary>The error SQL Server raises for a database that already exists.</summary>
        private const int DatabaseExistsErrorNumber = 1801;

        /// <summary>The value <c>sys.databases.catalog_collation_type_desc</c> must hold on Azure SQL Database.</summary>
        private const string DatabaseDefaultCatalogCollation = "DATABASE_DEFAULT";

        /// <summary>The query-store statement of a fresh tenant database.</summary>
        private const string QueryStoreStatement =
            "ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, MAX_STORAGE_SIZE_MB = 1024, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30));";

        /// <summary>The database's connection string.</summary>
        public string ConnectionString { get; } = connectionString ?? throw new ArgumentNullException(nameof(connectionString));

        /// <summary>The database's name.</summary>
        public string Name { get; } = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

        /// <summary>The server's name.</summary>
        public string Server { get; } = new SqlConnectionStringBuilder(connectionString).DataSource;

        /// <summary>The engine edition, once <see cref="EnsureExistsAsync" /> read it.</summary>
        public int EngineEdition { get; private set; }

        /// <summary>
        ///     Reads the engine edition in the server's <c>master</c> and, when the database is
        ///     missing and <paramref name="create" /> allows it, creates it there in a batch of its own.
        ///     A database a concurrent run created meanwhile counts as existing.
        /// </summary>
        /// <param name="create">Whether a missing database is created; otherwise it fails the command.</param>
        /// <param name="createDatabaseOptions">The options appended on Azure SQL Database.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>True when this call created the database.</returns>
        /// <exception cref="MigratorFailureException">The database is missing and may not be created.</exception>
        public async Task<bool> EnsureExistsAsync(bool create, string? createDatabaseOptions, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(Name))
            {
                throw new MigratorFailureException($"The connection string to server '{Server}' names no database.");
            }

            // --- master: the edition and the database's existence ---
            SqlConnectionStringBuilder master = new(ConnectionString) { InitialCatalog = "master" };
            SqlConnection connection = new(master.ConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                EngineEdition = Convert.ToInt32(await ScalarAsync(connection, DatabaseCreation.EngineEditionQuery, cancellationToken).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);

                await using SqlCommand exists = connection.CreateCommand();
                exists.CommandText = "SELECT DB_ID(@name);";
                _ = exists.Parameters.Add(new SqlParameter("@name", SqlDbType.NVarChar, 128) { Value = Name });
                if (await exists.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is not (null or DBNull))
                {
                    return false;
                }

                if (!create)
                {
                    throw new MigratorFailureException($"The database '{Name}' does not exist on server '{Server}'; only provisioning creates a tenant database.");
                }

                // --- The creation, its own batch; a concurrent creation is an existing database ---
                try
                {
                    await using SqlCommand command = connection.CreateCommand();
                    command.CommandText = DatabaseCreation.CreateStatement(Name, EngineEdition, createDatabaseOptions);

                    // Azure SQL Database allocates the database before it answers, which can outlast
                    // a statement's default bound.
                    command.CommandTimeout = 0;
                    _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (SqlException exception) when (exception.Number == DatabaseExistsErrorNumber)
                {
                    return false;
                }
            }
        }

        /// <summary>
        ///     Checks that an existing database is in the platform collation and, on Azure SQL
        ///     Database, that its catalog collation follows the database's.
        /// </summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="cancellationToken">Cancels the check.</param>
        /// <returns>A task that completes when the database passed.</returns>
        /// <exception cref="MigratorFailureException">A value differs; the message names the database, the value expected and the value found.</exception>
        public async Task CheckCollationAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            string? collation = await ScalarAsync(connection, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));", cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(collation, DatabaseCollations.Platform, StringComparison.Ordinal))
            {
                throw new MigratorFailureException(
                    $"The database '{Name}' on server '{Server}' has the collation '{collation}', but the platform collation '{DatabaseCollations.Platform}' is required; a database's collation is never altered, so recreate it.");
            }

            // The column exists on Azure SQL Database, the only engine where it can differ.
            if (EngineEdition == DatabaseCreation.AzureSqlDatabaseEngineEdition)
            {
                string? catalogCollation = await ScalarAsync(connection, "SELECT [catalog_collation_type_desc] FROM sys.databases WHERE [database_id] = DB_ID();", cancellationToken).ConfigureAwait(false) as string;
                if (!string.Equals(catalogCollation, DatabaseDefaultCatalogCollation, StringComparison.Ordinal))
                {
                    throw new MigratorFailureException(
                        $"The database '{Name}' on server '{Server}' has the catalog collation '{catalogCollation}', but '{DatabaseDefaultCatalogCollation}' is required; recreate it.");
                }
            }
        }

        /// <summary>Whether the database is fresh: its own model's history table is absent, whoever created the database.</summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="historySchema">The schema of the model's history table: <c>catalog</c> for the catalog, <c>dbo</c> for a tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>True when the history table is absent.</returns>
        public static async Task<bool> IsFreshAsync(SqlConnection connection, string historySchema, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(historySchema);

            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT OBJECT_ID(@history, N'U');";
            _ = command.Parameters.Add(new SqlParameter("@history", SqlDbType.NVarChar, 300)
            {
                Value = $"{ApplicationGrants.Quote(historySchema)}.{ApplicationGrants.Quote(ApplicationGrants.HistoryTable)}",
            });

            return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is null or DBNull;
        }

        /// <summary>
        ///     Turns on read-committed snapshot isolation in a fresh database, unless it is on already
        ///     (it is, on Azure SQL Database). Any other connection open in the database is ended.
        /// </summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the option is on.</returns>
        public static async Task EnableReadCommittedSnapshotAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            // The option waits for every other connection in the database to leave. A web host
            // started before its catalog exists polls it, and its pooled connections stay, so the
            // statement would wait out its timeout; in a fresh database they hold nothing, and are
            // ended instead.
            await ExecuteAsync(
                connection,
                "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE [database_id] = DB_ID() AND [is_read_committed_snapshot_on] = 1) ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Turns on the query store with the platform's settings.</summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the store is on.</returns>
        public static Task EnableQueryStoreAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return ExecuteAsync(connection, QueryStoreStatement, cancellationToken);
        }

        /// <summary>Opens a connection to the database.</summary>
        /// <param name="cancellationToken">Cancels the open.</param>
        /// <returns>The open connection; the caller disposes it.</returns>
        public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
        {
            SqlConnection connection = new(ConnectionString);
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
                return connection;
            }
            catch
            {
                await connection.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>Runs statements, each a batch of its own, in order.</summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="statements">The statements.</param>
        /// <param name="cancellationToken">Cancels between and during statements.</param>
        /// <returns>A task that completes when every statement ran.</returns>
        public static async Task ExecuteAsync(SqlConnection connection, IEnumerable<string> statements, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(statements);

            foreach (string statement in statements)
            {
                await ExecuteAsync(connection, statement, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Runs one statement as a batch of its own.</summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="statement">The statement.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the statement ran.</returns>
        public static async Task ExecuteAsync(SqlConnection connection, string statement, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs a scalar query.</summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="query">The query.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <returns>The value, or null.</returns>
        private static async Task<object?> ScalarAsync(SqlConnection connection, string query, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is DBNull ? null : value;
        }
    }
}
