// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>Creates host factories wired to a fresh, migrated test database.</summary>
    public static class DatabaseBackedFactory
    {
        /// <summary>
        ///     Creates a <see cref="StandaloneFactory" /> whose store is a fresh database on the
        ///     shared SQL Server, created through the shared helper as the engine would create it
        ///     (<see cref="CreateStore" />), which the host migrates at startup.
        /// </summary>
        /// <param name="fixture">The shared SQL Server.</param>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <param name="overrides">Extra configuration on top of the database wiring.</param>
        /// <returns>The configured (not yet started) factory.</returns>
        public static StandaloneFactory CreateStandalone(
            SqlServerFixture fixture, string prefix, IReadOnlyDictionary<string, string?>? overrides = null)
        {
            string connectionString = CreateStore(fixture, prefix);

            StandaloneFactory factory = new();
            factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"] = connectionString;
            factory.ConfigurationOverrides["TellmaIdentity:Seed:ApplyMigrations"] = "true";

            if (overrides is not null)
            {
                foreach ((string key, string? value) in overrides)
                {
                    factory.ConfigurationOverrides[key] = value;
                }
            }

            return factory;
        }

        /// <summary>
        ///     Creates an <see cref="InProcFactory" /> whose store is a fresh database on the
        ///     shared SQL Server, created through the shared helper as the engine would create it
        ///     (<see cref="CreateStore" />), which the host migrates at startup.
        /// </summary>
        /// <param name="fixture">The shared SQL Server.</param>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <param name="overrides">Extra configuration on top of the database wiring.</param>
        /// <returns>The configured (not yet started) factory.</returns>
        public static InProcFactory CreateInProc(
            SqlServerFixture fixture, string prefix, IReadOnlyDictionary<string, string?>? overrides = null)
        {
            string connectionString = CreateStore(fixture, prefix);

            InProcFactory factory = new();
            factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"] = connectionString;
            factory.ConfigurationOverrides["TellmaIdentity:Seed:ApplyMigrations"] = "true";

            if (overrides is not null)
            {
                foreach ((string key, string? value) in overrides)
                {
                    factory.ConfigurationOverrides[key] = value;
                }
            }

            return factory;
        }

        /// <summary>
        ///     Creates an empty store as the engine's own creation leaves it, in the platform
        ///     collation with read-committed snapshot on, through the shared database creation, which
        ///     retries the <c>model</c> lock the suite's parallel creations queue on. The engine's
        ///     creation through EF would not retry it. A test that proves the engine's own creation
        ///     reserves its database instead.
        /// </summary>
        /// <param name="fixture">The shared SQL Server.</param>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <returns>The store's connection string.</returns>
        public static string CreateStore(SqlServerFixture fixture, string prefix)
        {
            // The factories are built synchronously by every caller, and the host they start blocks
            // on its own startup the same way; the helper never resumes on a captured context.
            string connectionString = fixture.CreateDatabaseAsync(prefix).GetAwaiter().GetResult();
            string name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;

            // What EF adds to the creation it runs on SQL Server (not Azure SQL Database, where it is always on).
            SqlDatabaseCreation.PrepareAsync(
                fixture.MasterConnectionString,
                TestContext.Current.CancellationToken,
                $"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;").GetAwaiter().GetResult();
            return connectionString;
        }
    }
}
