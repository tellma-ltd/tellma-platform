// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Hosting;
using Testcontainers.MsSql;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     One SQL Server for every database-backed test in the assembly, shared through the
    ///     <see cref="SqlServerCollectionDefinition" /> collection fixture (lazily created, so protocol
    ///     tests that never touch a database run without Docker). By default a
    ///     Testcontainers-managed container (identical locally and in CI); set
    ///     <c>TELLMA_TEST_SQL</c> to a connection string to target LocalDB or a local SQL Server
    ///     for faster inner-loop runs. Each test gets its own database: usually a name reserved by
    ///     <see cref="ReserveDatabase" />, which the engine's own startup creates through its model
    ///     (in the platform collation), or one created up front by <see cref="CreateDatabaseAsync" />
    ///     when a test needs an existing database in a given collation.
    /// </summary>
    public sealed class SqlServerFixture : IAsyncLifetime
    {
        private MsSqlContainer? _container;
        private string? _masterConnectionString;
        private readonly List<string> _createdDatabases = [];

        /// <summary>A connection string to the server's <c>master</c> database.</summary>
        public string MasterConnectionString =>
            _masterConnectionString ?? throw new InvalidOperationException("The fixture is not initialized.");

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            string? overrideConnectionString = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL");
            if (!string.IsNullOrWhiteSpace(overrideConnectionString))
            {
                _masterConnectionString = overrideConnectionString;
                return;
            }

            // The image is overridable because some Docker Desktop/WSL2 kernels crash specific
            // SQL Server images (e.g. 2022 exiting 255 at startup); CI uses the default.
            string image = Environment.GetEnvironmentVariable("TELLMA_TEST_SQL_IMAGE")
                ?? "mcr.microsoft.com/mssql/server:2022-latest";
            try
            {
                // Both Build() (Docker endpoint detection) and StartAsync() can fail when Docker
                // is absent or not running.
                _container = new MsSqlBuilder(image).Build();
                await _container.StartAsync(TestContext.Current.CancellationToken);
            }
            catch (Exception exception)
            {
                // Fail fast and actionably instead of leaking a cryptic Testcontainers stack.
                throw new InvalidOperationException(
                    "Integration tests require Docker (for Testcontainers) and could not start the SQL Server "
                        + "container. Install and start Docker, or set the TELLMA_TEST_SQL environment variable to a "
                        + "SQL Server connection string (e.g. LocalDB or a local SQL Server instance) to run against "
                        + $"an existing server. Underlying error: {exception.Message}",
                    exception);
            }

            _masterConnectionString = _container.GetConnectionString();
        }

        /// <summary>
        ///     Reserves a fresh, uniquely named database without creating it, and returns a
        ///     connection string to it. A host started with migrations on creates it through the
        ///     engine's model — in the platform collation, with read-committed snapshot on — exactly
        ///     as a developer's first start does. The name is dropped with the fixture either way.
        /// </summary>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <returns>The connection string.</returns>
        public string ReserveDatabase(string prefix)
        {
            string name = $"{prefix}_{Guid.NewGuid():N}";
            lock (_createdDatabases)
            {
                _createdDatabases.Add(name);
            }

            SqlConnectionStringBuilder builder = new(MasterConnectionString) { InitialCatalog = name };
            return builder.ConnectionString;
        }

        /// <summary>
        ///     Creates a fresh, uniquely named, empty database in an explicit collation and returns a
        ///     connection string to it. The server's default collation is never relied on: test
        ///     servers keep theirs, which is not the platform's.
        /// </summary>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <param name="collation">The database collation; the platform collation unless a test needs another.</param>
        /// <returns>The connection string.</returns>
        public async Task<string> CreateDatabaseAsync(string prefix, string collation = DatabaseCollations.Platform)
        {
            string connectionString = ReserveDatabase(prefix);
            string name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

            await using SqlConnection connection = new(MasterConnectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = $"CREATE DATABASE [{name}] COLLATE {collation}";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);

            return connectionString;
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            if (_container is not null)
            {
                // The container is discarded wholesale; no per-database cleanup needed.
                await _container.DisposeAsync();
                return;
            }

            // Running against a user-provided server (TELLMA_TEST_SQL): drop what we created.
            if (_masterConnectionString is not null)
            {
                await using SqlConnection connection = new(_masterConnectionString);
                await connection.OpenAsync();
                foreach (string name in _createdDatabases)
                {
                    await using SqlCommand command = connection.CreateCommand();
                    command.CommandText =
                        $"IF DB_ID('{name}') IS NOT NULL BEGIN ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}]; END";
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }

    /// <summary>
    ///     The collection every database-backed test class joins to share one SQL Server.
    /// </summary>
    [CollectionDefinition(Name)]
    public sealed class SqlServerCollectionDefinition : ICollectionFixture<SqlServerFixture>
    {
        /// <summary>The collection name used by <c>[Collection]</c> attributes.</summary>
        public const string Name = "SqlServer";
    }
}
