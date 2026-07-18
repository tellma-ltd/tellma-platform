// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>Creates host factories wired to a fresh, migrated test database.</summary>
    public static class DatabaseBackedFactory
    {
        /// <summary>
        ///     Creates a <see cref="StandaloneFactory" /> whose store is a fresh database on the
        ///     shared SQL Server, with migrations applied at startup.
        /// </summary>
        /// <param name="fixture">The shared SQL Server.</param>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <param name="overrides">Extra configuration on top of the database wiring.</param>
        /// <returns>The configured (not yet started) factory.</returns>
        public static async Task<StandaloneFactory> CreateStandaloneAsync(
            SqlServerFixture fixture, string prefix, IReadOnlyDictionary<string, string?>? overrides = null)
        {
            string connectionString = await fixture.CreateDatabaseAsync(prefix);

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
        ///     shared SQL Server, with migrations applied at startup.
        /// </summary>
        /// <param name="fixture">The shared SQL Server.</param>
        /// <param name="prefix">The database name prefix identifying the test.</param>
        /// <param name="overrides">Extra configuration on top of the database wiring.</param>
        /// <returns>The configured (not yet started) factory.</returns>
        public static async Task<InProcFactory> CreateInProcAsync(
            SqlServerFixture fixture, string prefix, IReadOnlyDictionary<string, string?>? overrides = null)
        {
            string connectionString = await fixture.CreateDatabaseAsync(prefix);

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
    }
}
