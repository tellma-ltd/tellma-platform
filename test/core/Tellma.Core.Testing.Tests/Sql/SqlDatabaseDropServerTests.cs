// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The end-of-suite drops against a real server: a database that sessions are still in, an
    ///     idle pooled one and one inside an open transaction, is dropped; and a database that was
    ///     reserved but never created is passed over.
    /// </summary>
    /// <param name="sql">The class's server: its own container unless an existing server is named.</param>
    [Trait("Category", "Integration")]
    public sealed class SqlDatabaseDropServerTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     A database is dropped though sessions are still in it: one idle in the pool, and one
        ///     holding a write in an open transaction, which the drop rolls back.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_database_that_sessions_are_still_in_is_dropped()
        {
            string connectionString = await sql.CreateDatabaseAsync("dropped");
            string name = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
            await SqlServerFixture.ExecuteAsync(connectionString, "CREATE TABLE dbo.Held (Id int NOT NULL);");

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

                await SqlDatabaseDrop.DropAllAsync(sql.MasterConnectionString, [name], Token);

                Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{name}');"));

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
