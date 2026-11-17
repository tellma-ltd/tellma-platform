// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The end-of-suite drops without a server: the statement each drop runs, which errors rerun
    ///     a drop, and the loop over the databases, which tries every one past a failure, stops once
    ///     the connection is lost, and reports every database left behind.
    /// </summary>
    /// <remarks>
    ///     A loop that stopped at the first failure would leave the rest of a suite's databases on
    ///     the server, and one that swallowed a failure would turn a cleanup that left databases
    ///     behind into a green run; each test here would fail on either.
    /// </remarks>
    public sealed class SqlDatabaseDropTests
    {
        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     The statement drops only a database that exists, after setting it to single-user with
        ///     its sessions rolled back; the name is bracketed, and quoted as a literal for the check.
        /// </summary>
        [Fact]
        public void The_statement_rolls_back_the_sessions_of_an_existing_database_then_drops_it()
        {
            Assert.Equal(
                "IF DB_ID(N'fx_a]b''c') IS NOT NULL BEGIN ALTER DATABASE [fx_a]]b'c] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [fx_a]]b'c]; END",
                SqlDatabaseDrop.DropStatement("fx_a]b'c"));
        }

        /// <summary>A blank name is refused.</summary>
        /// <param name="database">The name.</param>
        [Theory]
        [InlineData("")]
        [InlineData("  ")]
        public void A_blank_name_is_refused(string database)
        {
            _ = Assert.Throws<ArgumentException>(() => SqlDatabaseDrop.DropStatement(database));
        }

        /// <summary>
        ///     Only a database held for a moment is transient: in use, locked, taken in single-user
        ///     mode, in transition, or a deadlock, alone or with the failure that follows an
        ///     <c>ALTER DATABASE</c>.
        /// </summary>
        /// <param name="numbers">The error numbers one failed statement reported.</param>
        /// <param name="expected">Whether a rerun may succeed.</param>
        [Theory]
        [InlineData(new[] { 3702 }, true)]
        [InlineData(new[] { 5061, 5069 }, true)]
        [InlineData(new[] { 5064, 5069 }, true)]
        [InlineData(new[] { 924 }, true)]
        [InlineData(new[] { 952 }, true)]
        [InlineData(new[] { 1205 }, true)]

        // --- The follow-up alone, a missing database, the model lock, a timeout, nothing at all ---
        [InlineData(new[] { 5069 }, false)]
        [InlineData(new[] { 3701 }, false)]
        [InlineData(new[] { 1807, 1802 }, false)]
        [InlineData(new[] { -2 }, false)]
        [InlineData(new int[0], false)]

        // --- A hold beside another real error: the other error decides ---
        [InlineData(new[] { 3702, 5011 }, false)]
        [InlineData(new[] { 5061, 5069, 5058 }, false)]
        public void Only_a_database_held_for_a_moment_is_transient(int[] numbers, bool expected)
        {
            Assert.Equal(expected, SqlDatabaseDrop.IsTransient(numbers));
        }

        /// <summary>An exception that is not SQL Server's is never transient, whatever its message says.</summary>
        [Fact]
        public void An_exception_that_is_not_sql_servers_is_not_transient()
        {
            Assert.False(SqlDatabaseDrop.IsTransient(new InvalidOperationException("Cannot drop database because it is currently in use.")));
            Assert.False(SqlDatabaseDrop.IsTransient(new TimeoutException()));
        }

        /// <summary>Every database is dropped in order when none fails, and nothing is thrown.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Every_database_is_dropped_in_order()
        {
            List<string> dropped = [];

            await SqlDatabaseDrop.DropEachAsync(["fx_a", "fx_b", "fx_c"], (database, _) => Record(dropped, database), static () => true, Token);

            Assert.Equal(["fx_a", "fx_b", "fx_c"], dropped);
        }

        /// <summary>
        ///     A failed drop does not stop the ones after it, and every failure is reported together at
        ///     the end: the database named, its failure the inner exception unchanged.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_failed_drop_does_not_stop_the_rest_and_every_failure_is_reported()
        {
            List<string> dropped = [];
            InvalidOperationException first = new("first");
            InvalidOperationException second = new("second");

            AggregateException reported = await Assert.ThrowsAsync<AggregateException>(() => SqlDatabaseDrop.DropEachAsync(
                ["fx_a", "fx_b", "fx_c", "fx_d"],
                (database, _) => database switch
                {
                    "fx_b" => Task.FromException(first),
                    "fx_d" => Task.FromException(second),
                    _ => Record(dropped, database),
                },
                static () => true,
                Token));

            Assert.Equal(["fx_a", "fx_c"], dropped);
            Assert.Equal<Exception>([first, second], reported.InnerExceptions);
            Assert.StartsWith("2 of the 4 test databases were not dropped", reported.Message, StringComparison.Ordinal);
            Assert.Contains("Failed, each with its failure as an inner exception: [fx_b], [fx_d].", reported.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("Not tried", reported.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     Once the connection is lost, the remaining databases are not tried, and they are named
        ///     beside the failure that came before.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_lost_connection_leaves_the_rest_untried_and_named()
        {
            List<string> dropped = [];
            bool connected = true;
            InvalidOperationException lost = new("lost");

            AggregateException reported = await Assert.ThrowsAsync<AggregateException>(() => SqlDatabaseDrop.DropEachAsync(
                ["fx_a", "fx_b", "fx_c", "fx_d"],
                (database, _) =>
                {
                    if (database == "fx_b")
                    {
                        connected = false;
                        return Task.FromException(lost);
                    }

                    return Record(dropped, database);
                },
                () => connected,
                Token));

            Assert.Equal(["fx_a"], dropped);
            Assert.Same(lost, Assert.Single(reported.InnerExceptions));
            Assert.StartsWith("3 of the 4 test databases were not dropped", reported.Message, StringComparison.Ordinal);
            Assert.Contains("Failed, each with its failure as an inner exception: [fx_b].", reported.Message, StringComparison.Ordinal);
            Assert.Contains("Not tried, as the connection to the server was lost: [fx_c], [fx_d].", reported.Message, StringComparison.Ordinal);
        }

        /// <summary>A cancellation surfaces at once: no later database is tried and nothing is wrapped.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_cancellation_surfaces_at_once()
        {
            List<string> dropped = [];
            using CancellationTokenSource cancellation = new();

            _ = await Assert.ThrowsAsync<OperationCanceledException>(() => SqlDatabaseDrop.DropEachAsync(
                ["fx_a", "fx_b", "fx_c"],
                async (database, token) =>
                {
                    if (database == "fx_b")
                    {
                        await cancellation.CancelAsync();
                        token.ThrowIfCancellationRequested();
                    }

                    await Record(dropped, database);
                },
                static () => true,
                cancellation.Token));

            Assert.Equal(["fx_a"], dropped);
        }

        /// <summary>Records a drop that succeeds.</summary>
        /// <param name="dropped">The drops so far.</param>
        /// <param name="database">The database.</param>
        /// <returns>A completed task.</returns>
        private static Task Record(List<string> dropped, string database)
        {
            dropped.Add(database);
            return Task.CompletedTask;
        }
    }
}
