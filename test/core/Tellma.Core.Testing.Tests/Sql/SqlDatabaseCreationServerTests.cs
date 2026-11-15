// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The shared database creation against a real server: its preparation runs every batch on
    ///     one session; and, under real contention for the <c>model</c> lock on a container of the
    ///     class's own, SQL Server's own 1807 is classified as transient, a bound of one run surfaces
    ///     it unchanged, and concurrent creations it blocked are retried and all succeed once
    ///     <c>model</c> is free.
    /// </summary>
    /// <remarks>
    ///     The contention is made deterministic by a session whose database is <c>model</c>: it holds
    ///     a shared lock there, so every <c>CREATE DATABASE</c> waits out the server's own lock wait
    ///     (about 25 seconds) and fails with 1807, as a creation queued behind a suite's parallel
    ///     creations does. That test runs only on a container the fixture started, and is skipped on
    ///     a server named by <c>TELLMA_TEST_SQL</c>: the held lock would stall every other suite's
    ///     creations on a shared server.
    /// </remarks>
    /// <param name="sql">The class's server: its own container unless an existing server is named.</param>
    [Trait("Category", "Integration")]
    public sealed class SqlDatabaseCreationServerTests(SqlServerFixture sql) : IClassFixture<SqlServerFixture>
    {
        /// <summary>How many creations contend at once.</summary>
        private const int Contenders = 6;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     A database's preparation runs its batches in order on one session, so a later batch
        ///     sees what an earlier one left in the session.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Preparation_runs_every_batch_on_one_session()
        {
            string name = sql.NewDatabaseName("prepared");
            await SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, name, cancellationToken: Token);

            // A temporary table lives only as long as its session: the second batch finds it only on the same one.
            await SqlDatabaseCreation.PrepareAsync(
                sql.ConnectionString(name),
                Token,
                "CREATE TABLE #session (Id int NOT NULL);",
                "SELECT Id INTO dbo.Survivor FROM #session;");

            Assert.NotNull(await SqlServerFixture.ScalarAsync<int?>(sql.ConnectionString(name), "SELECT OBJECT_ID(N'dbo.Survivor');"));
        }

        /// <summary>
        ///     While a session holds <c>model</c>, a one-run creation surfaces SQL Server's 1807 as
        ///     transient, and concurrent creations each log a retry; once the session leaves, every one
        ///     of them exists in the stated collation.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Creations_blocked_on_the_model_lock_are_retried_and_all_exist_once_it_is_free()
        {
            // Never hold model on a server other suites may share.
            Assert.SkipUnless(
                sql.OwnsContainer,
                $"Holds the model database's lock, which would stall other suites' creations on the shared server {SqlServerFixture.ServerVariable} names; runs on a container of its own (unset {SqlServerFixture.ServerVariable}, Docker required).");

            // Short waits keep the test inside the server's own lock wait plus a few seconds.
            CreateDatabaseRetry patient = new(4, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1));
            CreateDatabaseRetry single = new(1, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
            string[] names = [.. Enumerable.Range(0, Contenders).Select(_ => sql.NewDatabaseName("contended"))];
            string lone = sql.NewDatabaseName("contended_once");

            // --- Hold model: an unpooled session in it, so closing it ends the session ---
            SqlConnectionStringBuilder model = new(sql.MasterConnectionString) { InitialCatalog = "model", Pooling = false };
            SqlConnection holder = new(model.ConnectionString);
            Task[] creations;
            ConcurrentDictionary<string, ConcurrentQueue<string>> logs = new(StringComparer.Ordinal);
            List<string> loneLog = [];
            await using (holder)
            {
                await holder.OpenAsync(Token);

                // --- The contenders: each logs into its own queue, and the last first retry frees the gate ---
                TaskCompletionSource everyRetried = new(TaskCreationOptions.RunContinuationsAsynchronously);
                int retried = 0;
                creations = [.. names.Select(name => SqlDatabaseCreation.CreateAsync(
                    sql.MasterConnectionString,
                    name,
                    DatabaseCollations.Platform,
                    patient,
                    line =>
                    {
                        ConcurrentQueue<string> log = logs.GetOrAdd(name, static _ => new ConcurrentQueue<string>());
                        log.Enqueue(line);
                        if (log.Count == 1 && Interlocked.Increment(ref retried) == Contenders)
                        {
                            everyRetried.SetResult();
                        }
                    },
                    Token))];

                // --- One run only: the server's own error surfaces unchanged, classified as transient ---
                SqlException surfaced = await Assert.ThrowsAsync<SqlException>(
                    () => SqlDatabaseCreation.CreateAsync(sql.MasterConnectionString, lone, DatabaseCollations.Platform, single, loneLog.Add, Token));
                Assert.Equal(
                    [CreateDatabaseRetry.ModelLockedErrorNumber, CreateDatabaseRetry.CreateDatabaseFailedErrorNumber],
                    surfaced.Errors.Cast<SqlError>().Select(static error => error.Number));
                Assert.True(CreateDatabaseRetry.IsTransient(surfaced));
                Assert.Contains("on attempt 1 of 1; giving up: SQL error 1807, 1802", Assert.Single(loneLog), StringComparison.Ordinal);

                // --- Every contender was refused by the lock at least once before model is let go ---
                await everyRetried.Task.WaitAsync(TimeSpan.FromMinutes(2), Token);
            }

            // --- Model is free: every contender's next run creates its database ---
            await Task.WhenAll(creations).WaitAsync(TimeSpan.FromMinutes(2), Token);
            foreach (string name in names)
            {
                Assert.Equal(
                    DatabaseCollations.Platform,
                    await SqlServerFixture.ScalarAsync<string>(sql.MasterConnectionString, $"SELECT CONVERT(nvarchar(128), DATABASEPROPERTYEX(N'{name}', 'Collation'));"));
                Assert.Contains("retrying in", logs[name].First(), StringComparison.Ordinal);
                Assert.Contains("SQL error 1807, 1802", logs[name].First(), StringComparison.Ordinal);
            }

            Assert.Null(await SqlServerFixture.ScalarAsync<int?>(sql.MasterConnectionString, $"SELECT DB_ID(N'{lone}');"));
        }
    }
}
