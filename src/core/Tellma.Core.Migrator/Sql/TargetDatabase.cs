// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Sql
{
    /// <summary>
    ///     One database the migrator prepares: created in the platform collation when it is missing,
    ///     checked when it exists, and told apart as fresh when its model's history table is absent.
    ///     Every statement runs under the bound of its class in <see cref="MigratorOptions" />.
    /// </summary>
    /// <param name="connectionString">The database's connection string, as the migrator connects to it.</param>
    /// <param name="bounds">The migrator's statement bounds.</param>
    internal sealed class TargetDatabase(string connectionString, MigratorOptions bounds)
    {
        /// <summary>The error SQL Server raises for a database that already exists.</summary>
        private const int DatabaseExistsErrorNumber = 1801;

        /// <summary>The value <c>sys.databases.catalog_collation_type_desc</c> must hold on Azure SQL Database.</summary>
        private const string DatabaseDefaultCatalogCollation = "DATABASE_DEFAULT";

        /// <summary>The query-store statement of a fresh tenant database.</summary>
        private const string QueryStoreStatement =
            "ALTER DATABASE CURRENT SET QUERY_STORE = ON (OPERATION_MODE = READ_WRITE, QUERY_CAPTURE_MODE = AUTO, MAX_STORAGE_SIZE_MB = 1024, CLEANUP_POLICY = (STALE_QUERY_THRESHOLD_DAYS = 30));";

        /// <summary>The migrator's statement bounds.</summary>
        private readonly MigratorOptions _bounds = bounds ?? throw new ArgumentNullException(nameof(bounds));

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
        ///     missing and <paramref name="create" /> allows it, creates it there in a batch of its own,
        ///     running the creation again while the server reports the <c>model</c> database locked.
        ///     A database a concurrent run created meanwhile counts as existing.
        /// </summary>
        /// <param name="create">Whether a missing database is created; otherwise it fails the command.</param>
        /// <param name="createDatabaseOptions">The options appended on Azure SQL Database.</param>
        /// <param name="retry">When a creation that found <c>model</c> locked runs again.</param>
        /// <param name="logger">The migrator's logger, which records each retry.</param>
        /// <param name="cancellationToken">Cancels the work and the waits between attempts.</param>
        /// <returns>True when this call created the database.</returns>
        /// <exception cref="MigratorFailureException">The database is missing and may not be created.</exception>
        public async Task<bool> EnsureExistsAsync(
            bool create, string? createDatabaseOptions, CreateDatabaseRetry retry, ILogger logger, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(retry);
            ArgumentNullException.ThrowIfNull(logger);
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
                EngineEdition = Convert.ToInt32(
                    await ScalarAsync(connection, DatabaseCreation.EngineEditionQuery, parameter: null, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false),
                    System.Globalization.CultureInfo.InvariantCulture);

                SqlParameter name = new("@name", SqlDbType.NVarChar, 128) { Value = Name };
                if (await ScalarAsync(connection, "SELECT DB_ID(@name);", name, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false) is not null)
                {
                    return false;
                }

                if (!create)
                {
                    throw new MigratorFailureException($"The database '{Name}' does not exist on server '{Server}'; only provisioning creates a tenant database.");
                }

                // --- The creation, its own batch, run again while model is locked; a concurrent creation is an existing database ---
                string statement = DatabaseCreation.CreateStatement(Name, EngineEdition, createDatabaseOptions);
                try
                {
                    // A creation that found model locked created nothing (the server's own wait for the
                    // lock is part of the statement), so the same statement runs again on the same
                    // connection. Azure SQL Database allocates the database before it answers, and
                    // SQL Server waits for the lock first, so the creation has the database bound.
                    await retry.RunAsync(
                        token => RunStatementAsync(connection, statement, _bounds.DatabaseCommandTimeout, token),
                        logger,
                        Name,
                        Server,
                        cancellationToken).ConfigureAwait(false);
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

            string? collation = await ScalarAsync(connection, "SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(DB_NAME(), 'Collation'));", parameter: null, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false) as string;
            if (!string.Equals(collation, DatabaseCollations.Platform, StringComparison.Ordinal))
            {
                throw new MigratorFailureException(
                    $"The database '{Name}' on server '{Server}' has the collation '{collation}', but the platform collation '{DatabaseCollations.Platform}' is required; a database's collation is never altered, so recreate it.");
            }

            // The column exists on Azure SQL Database, the only engine where it can differ.
            if (EngineEdition == DatabaseCreation.AzureSqlDatabaseEngineEdition)
            {
                string? catalogCollation = await ScalarAsync(connection, "SELECT [catalog_collation_type_desc] FROM sys.databases WHERE [database_id] = DB_ID();", parameter: null, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false) as string;
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
        public async Task<bool> IsFreshAsync(SqlConnection connection, string historySchema, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(historySchema);

            SqlParameter history = new("@history", SqlDbType.NVarChar, 300)
            {
                Value = $"{ApplicationGrants.Quote(historySchema)}.{ApplicationGrants.Quote(ApplicationGrants.HistoryTable)}",
            };

            return await ScalarAsync(connection, "SELECT OBJECT_ID(@history, N'U');", history, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false) is null;
        }

        /// <summary>
        ///     Turns on read-committed snapshot isolation in a fresh database, unless it is on already
        ///     (it is, on Azure SQL Database). Any other connection open in the database is ended.
        /// </summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the option is on.</returns>
        public Task EnableReadCommittedSnapshotAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);

            // The option waits for every other connection in the database to leave. A web host
            // started before its catalog exists polls it, and its pooled connections stay, so the
            // statement would wait out its bound; in a fresh database they hold nothing, and are
            // ended instead. Still an option change: a loaded server takes its time over it.
            return RunStatementAsync(
                connection,
                "IF NOT EXISTS (SELECT 1 FROM sys.databases WHERE [database_id] = DB_ID() AND [is_read_committed_snapshot_on] = 1) ALTER DATABASE CURRENT SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;",
                _bounds.DatabaseCommandTimeout,
                cancellationToken);
        }

        /// <summary>Turns on the query store with the platform's settings.</summary>
        /// <param name="connection">An open connection to the database.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the store is on.</returns>
        public Task EnableQueryStoreAsync(SqlConnection connection, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            return RunStatementAsync(connection, QueryStoreStatement, _bounds.DatabaseCommandTimeout, cancellationToken);
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

        /// <summary>
        ///     Runs statements that are neither a database's creation or option change nor a
        ///     migration's, such as the role and the grants: each a batch of its own, in order, under
        ///     <see cref="MigratorOptions.CommandTimeout" />.
        /// </summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="statements">The statements.</param>
        /// <param name="cancellationToken">Cancels between and during statements.</param>
        /// <returns>A task that completes when every statement ran.</returns>
        public async Task ExecuteAsync(SqlConnection connection, IEnumerable<string> statements, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(statements);

            foreach (string statement in statements)
            {
                await RunStatementAsync(connection, statement, _bounds.CommandTimeout, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Runs one statement that is neither a database's creation or option change nor a
        ///     migration's, such as the role, as a batch of its own under
        ///     <see cref="MigratorOptions.CommandTimeout" />.
        /// </summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="statement">The statement.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the statement ran.</returns>
        public Task ExecuteAsync(SqlConnection connection, string statement, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentException.ThrowIfNullOrWhiteSpace(statement);
            return RunStatementAsync(connection, statement, _bounds.CommandTimeout, cancellationToken);
        }

        /// <summary>Runs one statement as a batch of its own under a bound.</summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="statement">The statement.</param>
        /// <param name="timeout">The statement's bound.</param>
        /// <param name="cancellationToken">Cancels the statement.</param>
        /// <returns>A task that completes when the statement ran.</returns>
        private static async Task RunStatementAsync(SqlConnection connection, string statement, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = statement;
            command.CommandTimeout = MigratorOptions.Seconds(timeout);
            _ = await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs a scalar query under a bound.</summary>
        /// <param name="connection">An open connection.</param>
        /// <param name="query">The query.</param>
        /// <param name="parameter">The query's one parameter, or null.</param>
        /// <param name="timeout">The query's bound.</param>
        /// <param name="cancellationToken">Cancels the query.</param>
        /// <returns>The value, or null for a SQL null.</returns>
        private static async Task<object?> ScalarAsync(SqlConnection connection, string query, SqlParameter? parameter, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = query;
            command.CommandTimeout = MigratorOptions.Seconds(timeout);
            if (parameter is not null)
            {
                _ = command.Parameters.Add(parameter);
            }

            object? value = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return value is DBNull ? null : value;
        }
    }
}
