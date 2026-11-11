// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Hosting;

// One SQL Server for the whole assembly: the shared fixture, which every test reaches through its constructor.
[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace Tellma.Core.IntegrationTests.Infrastructure
{
    /// <summary>The suite's way of creating catalogs on the shared server.</summary>
    internal static class CatalogDatabases
    {
        /// <summary>
        ///     Creates an empty database as the migrator creates a fresh catalog: in the platform
        ///     collation, with read-committed snapshot on and the application role in place, ready for
        ///     the catalog migrations. The fixture drops it at the end.
        /// </summary>
        /// <param name="sql">The shared server.</param>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <param name="createRole">Whether to create the application role, which the membership table type's migration grants.</param>
        /// <returns>The database's connection string.</returns>
        public static async Task<string> CreateEmptyCatalogAsync(this SqlServerFixture sql, string prefix, bool createRole = true)
        {
            string name = sql.NewDatabaseName(prefix);
            await SqlServerFixture.ExecuteAsync(
                sql.MasterConnectionString,
                $"CREATE DATABASE [{name}] COLLATE {DatabaseCollations.Platform};",
                $"ALTER DATABASE [{name}] SET READ_COMMITTED_SNAPSHOT ON WITH ROLLBACK IMMEDIATE;");

            string connectionString = sql.ConnectionString(name);
            if (createRole)
            {
                await SqlServerFixture.ExecuteAsync(connectionString, $"CREATE ROLE [{DatabaseRoles.Application}];");
            }

            return connectionString;
        }
    }
}
