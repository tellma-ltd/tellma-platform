// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Core.Abstractions.Hosting;
using Testcontainers.MsSql;

[assembly: AssemblyFixture(typeof(Tellma.Core.IntegrationTests.Infrastructure.SqlServerFixture))]

namespace Tellma.Core.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     One SQL Server for the whole integration assembly. By default a Testcontainers-managed
    ///     container (identical locally and in CI); set <c>TELLMA_TEST_SQL</c> to a connection string
    ///     to target LocalDB or a local SQL Server instead. Each test creates its own uniquely named
    ///     databases, which a run against an existing server drops at the end.
    /// </summary>
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        private readonly List<string> _databases = [];
        private MsSqlContainer? _container;
        private string? _masterConnectionString;

        /// <summary>A connection string to the server's <c>master</c> database.</summary>
        public string MasterConnectionString =>
            _masterConnectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

        /// <summary>The server the connection strings name, as a catalog row would hold it.</summary>
        public string Server => new SqlConnectionStringBuilder(MasterConnectionString).DataSource;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            string? overrideConnectionString = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL");
            if (!string.IsNullOrWhiteSpace(overrideConnectionString))
            {
                _masterConnectionString = overrideConnectionString;
                return;
            }

            // The image is overridable because some Docker Desktop/WSL2 kernels crash specific SQL
            // Server images (2022 exiting 255 at startup); CI uses the default.
            string image = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL_IMAGE") ?? "mcr.microsoft.com/mssql/server:2022-latest";
            try
            {
                _container = new MsSqlBuilder(image).Build();
                await _container.StartAsync(TestContext.Current.CancellationToken);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    "Integration tests require Docker (for Testcontainers) and could not start the SQL Server container. Install and "
                        + "start Docker, or set the TELLMA_TEST_SQL environment variable to a SQL Server connection string (LocalDB or a "
                        + $"local instance) to run against an existing server. Underlying error: {exception.Message}",
                    exception);
            }

            _masterConnectionString = _container.GetConnectionString();
        }

        /// <summary>A unique database name for a test, which the fixture drops at the end.</summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <returns>The name; the database is not created.</returns>
        public string NewDatabaseName(string prefix)
        {
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
        ///     Creates an empty database as the migrator creates a fresh catalog: in the platform
        ///     collation, with read-committed snapshot on and the application role in place, ready for
        ///     the catalog migrations.
        /// </summary>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <param name="createRole">Whether to create the application role, which the membership table type's migration grants.</param>
        /// <returns>The database's connection string.</returns>
        public async Task<string> CreateEmptyCatalogAsync(string prefix, bool createRole = true)
        {
            string name = NewDatabaseName(prefix);
            await ExecuteAsync(
                MasterConnectionString,
                $"CREATE DATABASE [{name}] COLLATE {DatabaseCollations.Platform};",
                $"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;");

            string connectionString = ConnectionString(name);
            if (createRole)
            {
                await ExecuteAsync(connectionString, $"CREATE ROLE [{DatabaseRoles.Application}];");
            }

            return connectionString;
        }

        /// <summary>Runs batches over one connection.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="batches">The batches.</param>
        /// <returns>A task that completes when every batch ran.</returns>
        public static async Task ExecuteAsync(string connectionString, params string[] batches)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            foreach (string batch in batches)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = batch;
                _ = await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }

        /// <summary>Runs a scalar query.</summary>
        /// <typeparam name="T">The result's type.</typeparam>
        /// <param name="connectionString">The connection string.</param>
        /// <param name="sql">The query.</param>
        /// <returns>The result, or the default for a null.</returns>
        public static async Task<T?> ScalarAsync<T>(string connectionString, string sql)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = sql;
            object? result = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
            return result is null or DBNull ? default : (T)result;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                // The container is discarded whole.
                await _container.DisposeAsync();
                return;
            }

            if (_masterConnectionString is null)
            {
                return;
            }

            // An existing server: drop what the suite created.
            await using SqlConnection connection = new(_masterConnectionString);
            await connection.OpenAsync();
            foreach (string name in _databases)
            {
                await using SqlCommand command = connection.CreateCommand();
                command.CommandText = $"IF DB_ID(N'{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
                _ = await command.ExecuteNonQueryAsync();
            }
        }
    }
}
