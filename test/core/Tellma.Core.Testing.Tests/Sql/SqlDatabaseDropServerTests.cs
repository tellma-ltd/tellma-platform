// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Globalization;
using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The end-of-suite drops against a real server: databases that sessions are still in, an
    ///     idle pooled one and one inside an open transaction, are dropped, the pools cleared once
    ///     before the first drop and every drop under the five-minute bound; and a database that was
    ///     reserved but never created is passed over.
    /// </summary>
    /// <param name="sql">The collection's server: the assembly's own container unless an existing server is named.</param>
    [Trait("Category", "Integration")]
    [Collection(SqlServerCollectionDefinition.Name)]
    public sealed class SqlDatabaseDropServerTests(SqlServerFixture sql)
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     Databases are dropped though sessions are still in them: one idle in the pool, and one
        ///     holding a write in an open transaction, which the drop rolls back. The pools are cleared
        ///     once, before the first drop, and each drop runs under the five-minute bound.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Databases_that_sessions_are_still_in_are_dropped()
        {
            string connectionString = await sql.CreateDatabaseAsync("dropped");
            string name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            string other = new SqlConnectionStringBuilder(await sql.CreateDatabaseAsync("dropped_too")).InitialCatalog;
            await SqlServerFixture.ExecuteAsync(connectionString, "CREATE TABLE dbo.Held (Id int NOT NULL);");
            List<string> events = [];
            List<string> log = [];

            // --- A session left idle in the pool ---
            Assert.Equal(1, await SqlServerFixture.ScalarAsync<int>(connectionString, "SELECT 1;"));

            // --- A session of its own, holding a write in an open transaction ---
            SqlConnection holder = new(new SqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString);
            await using (holder)
            {
                await holder.OpenAsync(Token);
                SqlCommand write = holder.CreateCommand();
                await using (write)
                {
                    write.CommandText = "BEGIN TRANSACTION; INSERT INTO dbo.Held (Id) VALUES (1);";
                    _ = await write.ExecuteNonQueryAsync(Token);
                }

                // --- The real path, its command runner and pool clearer observed on the way through ---
                await SqlDatabaseDrop.DropAllAsync(
                    sql.MasterConnectionString,
                    [name, other],
                    CreateDatabaseRetry.Default,
                    log.Add,
                    (command, token) =>
                    {
                        events.Add(string.Create(CultureInfo.InvariantCulture, $"run {command.CommandTimeout} s"));
                        return command.ExecuteNonQueryAsync(token);
                    },
                    () =>
                    {
                        events.Add("clear");
                        SqlConnection.ClearAllPools();
                    },
                    Token);

                Assert.Equal(["clear", "run 300 s", "run 300 s"], events);
                Assert.Empty(log);
                Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{name}');"));
                Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{other}');"));

                // --- The holder's session was rolled back and ended under it ---
                SqlCommand after = holder.CreateCommand();
                await using (after)
                {
                    after.CommandText = "SELECT COUNT(*) FROM dbo.Held;";
                    _ = await Assert.ThrowsAsync<SqlException>(() => after.ExecuteScalarAsync(Token));
                }
            }
        }

        /// <summary>A database reserved but never created is passed over without an error.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_database_never_created_is_passed_over()
        {
            string name = sql.NewDatabaseName("never_created");

            await SqlDatabaseDrop.DropAllAsync(sql.MasterConnectionString, [name], Token);

            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{name}');"));
        }
    }
}
