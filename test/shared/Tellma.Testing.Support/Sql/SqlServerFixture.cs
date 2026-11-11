// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Core.Abstractions.Hosting;
using Testcontainers.MsSql;
using Xunit;

namespace Tellma.Testing.Support.Sql
{
    /// <summary>
    ///     One SQL Server for an integration suite, shared as an assembly or a collection fixture. By
    ///     default it is a Testcontainers-managed container, identical locally and in CI. Set
    ///     <c>TELLMA_TEST_SQL</c> to a connection string to target LocalDB or a local SQL Server
    ///     instead, and <c>TELLMA_TEST_SQL_IMAGE</c> to run another image. Tests create uniquely named
    ///     databases through it: a container is discarded whole, and a run against an existing server
    ///     drops what it created.
    /// </summary>
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        /// <summary>The variable naming an existing server to use instead of starting a container.</summary>
        public const string ServerVariable = "TELLMA_TEST_SQL";

        /// <summary>The variable naming the image a container run starts.</summary>
        public const string ImageVariable = "TELLMA_TEST_SQL_IMAGE";

        /// <summary>The image a container run starts unless <see cref="ImageVariable" /> names another.</summary>
        public const string DefaultImage = "mcr.microsoft.com/mssql/server:2022-latest";

        /// <summary>The databases reserved or created through the fixture, dropped at the end on an existing server.</summary>
        private readonly List<string> _databases = [];

        /// <summary>The container, when one was started.</summary>
        private MsSqlContainer? _container;

        /// <summary>The server's <c>master</c> connection string, once initialized.</summary>
        private string? _masterConnectionString;

        /// <summary>A connection string to the server's <c>master</c> database.</summary>
        public string MasterConnectionString =>
            _masterConnectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

        /// <summary>The server the connection strings name, as a catalog row would hold it.</summary>
        public string Server => new SqlConnectionStringBuilder(MasterConnectionString).DataSource;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            // --- An existing server the developer named ---
            string? named = Environment.GetEnvironmentVariable(ServerVariable);
            if (!string.IsNullOrWhiteSpace(named))
            {
                _masterConnectionString = named;
                return;
            }

            // --- Otherwise a container. The image is overridable because some Docker Desktop/WSL2
            // kernels crash specific SQL Server images (2022 exiting 255 at startup); CI uses the default ---
            string image = Environment.GetEnvironmentVariable(ImageVariable) ?? DefaultImage;
            try
            {
                // Both Build (Docker endpoint detection) and StartAsync fail when Docker is absent or stopped.
                _container = new MsSqlBuilder(image).Build();
                await _container.StartAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Integration tests require Docker (for Testcontainers) and could not start the SQL Server container. Install and "
                        + $"start Docker, or set the {ServerVariable} environment variable to a SQL Server connection string (LocalDB or a "
                        + $"local instance) to run against an existing server. Underlying error: {exception.Message}",
                    exception);
            }

            _masterConnectionString = _container.GetConnectionString();
        }

        /// <summary>A unique database name for a test, which the fixture drops at the end; the database is not created.</summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <returns>The name.</returns>
        public string NewDatabaseName(string prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
            string name = $"{prefix}_{Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)[..12]}";
            lock (_databases)
            {
                _databases.Add(name);
            }

            return name;
        }

        /// <summary>The connection string to a database of the server.</summary>
        /// <param name="database">The database.</param>
        /// <returns>The connection string.</returns>
        public string ConnectionString(string database)
        {
            return new SqlConnectionStringBuilder(MasterConnectionString) { InitialCatalog = database }.ConnectionString;
        }

        /// <summary>
        ///     Reserves a unique database without creating it, for a host that creates its own
        ///     database through its model, and returns a connection string to it.
        /// </summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <returns>The connection string.</returns>
        public string ReserveDatabase(string prefix)
        {
            return ConnectionString(NewDatabaseName(prefix));
        }

        /// <summary>
        ///     Creates a unique, empty database in an explicit collation and returns a connection
        ///     string to it. The collation is stated because a test server's default is not the
        ///     platform's.
        /// </summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <param name="collation">The collation; the platform collation unless a test needs another.</param>
        /// <returns>The connection string.</returns>
        public async Task<string> CreateDatabaseAsync(string prefix, string collation = DatabaseCollations.Platform)
        {
            string name = NewDatabaseName(prefix);
            await ExecuteAsync(MasterConnectionString, $"CREATE DATABASE [{name}] COLLATE {collation};").ConfigureAwait(false);
            return ConnectionString(name);
        }

        /// <summary>Runs batches over one connection.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="batches">The batches, in order.</param>
        /// <returns>A task that completes when every batch ran.</returns>
        public static async Task ExecuteAsync(string connectionString, params string[] batches)
        {
            ArgumentNullException.ThrowIfNull(batches);
            SqlConnection connection = new(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                foreach (string batch in batches)
                {
                    SqlCommand command = connection.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = batch;
                        _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        /// <summary>Runs a scalar query.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="sql">The query.</param>
        /// <returns>The result, or the default for a null.</returns>
        public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
        {
            SqlConnection connection = new(connectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                SqlCommand command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText = sql;
                    object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
                    return result is null or DBNull ? default : (T)result;
                }
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            // --- A container is discarded whole ---
            if (_container is not null)
            {
                await _container.DisposeAsync().ConfigureAwait(false);
                return;
            }

            if (_masterConnectionString is null)
            {
                return;
            }

            // --- An existing server: drop what the suite reserved or created ---
            SqlConnection connection = new(_masterConnectionString);
            await using (connection.ConfigureAwait(false))
            {
                await connection.OpenAsync().ConfigureAwait(false);
                foreach (string name in _databases)
                {
                    SqlCommand command = connection.CreateCommand();
                    await using (command.ConfigureAwait(false))
                    {
                        command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
                        _ = await command.ExecuteNonQueryAsync().ConfigureAwait(false);
                    }
                }
            }
        }
    }
}
