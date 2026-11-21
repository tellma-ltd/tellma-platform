// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using System.Reflection;
using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The end-of-suite drops without a server: the statement each drop runs, which errors rerun
    ///     a drop, the drop path itself (a held refusal run again after the pools are cleared, any
    ///     other refusal surfacing at once, the five-minute bound on every run), a connection that
    ///     cannot be opened, and the loop over the databases, which tries every one past a failure,
    ///     stops once the connection is lost, and reports every database left behind.
    /// </summary>
    /// <remarks>
    ///     A loop that stopped at the first failure would leave the rest of a suite's databases on
    ///     the server, and one that swallowed a failure would turn a cleanup that left databases
    ///     behind into a green run; each test here would fail on either.
    /// </remarks>
    public sealed class SqlDatabaseDropTests
    {
        /// <summary>A connection string to nowhere: the drop path builds its commands on it, and an open of it fails fast.</summary>
        private const string Nowhere = "Data Source=tellma-no-such-server.invalid;Initial Catalog=master;Connect Timeout=1";

        /// <summary>A policy of three runs over 10 ms waits, so a rerun costs the test nothing.</summary>
        private static readonly CreateDatabaseRetry Quick = new(3, TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10));

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

        /// <summary>
        ///     A drop refused because the database was held runs again, the process's pooled
        ///     connections closed before the rerun and not before the first run, and the retry logged.
        /// </summary>
        /// <param name="numbers">The error numbers of the held refusal.</param>
        /// <returns>A task that completes when the test is done.</returns>
        [Theory]
        [InlineData(new[] { 3702 })]
        [InlineData(new[] { 5064, 5069 })]
        public async Task A_held_drop_runs_again_after_the_pools_are_cleared(int[] numbers)
        {
            List<string> events = [];
            List<string> log = [];
            using SqlConnection master = new(Nowhere);

            await SqlDatabaseDrop.DropAsync(
                master,
                "fx_held",
                Quick,
                log.Add,
                (_, _) =>
                {
                    events.Add("run");
                    return events.Count == 1 ? Task.FromException(Refusal(numbers)) : Task.CompletedTask;
                },
                () => events.Add("clear"),
                Token);

            Assert.Equal(["run", "clear", "run"], events);
            string retried = Assert.Single(log);
            Assert.Contains("DROP DATABASE [fx_held] failed with a transient error on attempt 1 of 3; retrying in", retried, StringComparison.Ordinal);
            Assert.Contains($"SQL error {string.Join(", ", numbers)}", retried, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A drop refused for any other reason, a missing database or a timeout, surfaces at once:
        ///     no rerun, no pool cleared, nothing logged.
        /// </summary>
        /// <param name="number">The error number of the refusal.</param>
        /// <returns>A task that completes when the test is done.</returns>
        [Theory]
        [InlineData(3701)]
        [InlineData(-2)]
        public async Task A_drop_refused_for_another_reason_is_not_run_again(int number)
        {
            List<string> events = [];
            List<string> log = [];
            SqlException refusal = Refusal(number);
            using SqlConnection master = new(Nowhere);

            SqlException surfaced = await Assert.ThrowsAsync<SqlException>(() => SqlDatabaseDrop.DropAsync(
                master,
                "fx_refused",
                Quick,
                log.Add,
                (_, _) =>
                {
                    events.Add("run");
                    return Task.FromException(refusal);
                },
                () => events.Add("clear"),
                Token));

            Assert.Same(refusal, surfaced);
            Assert.Equal(["run"], events);
            Assert.Empty(log);
        }

        /// <summary>Every run of a drop carries its statement and the five-minute bound, the rerun as much as the first.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task Every_run_carries_the_statement_and_the_five_minute_bound()
        {
            List<(string Text, int Timeout)> commands = [];
            using SqlConnection master = new(Nowhere);

            await SqlDatabaseDrop.DropAsync(
                master,
                "fx_bound",
                Quick,
                static _ => { },
                (command, _) =>
                {
                    commands.Add((command.CommandText, command.CommandTimeout));
                    return commands.Count == 1 ? Task.FromException(Refusal(3702)) : Task.CompletedTask;
                },
                static () => { },
                Token);

            (string Text, int Timeout) expected = (SqlDatabaseDrop.DropStatement("fx_bound"), (int)SqlDatabaseCreation.CommandTimeout.TotalSeconds);
            Assert.Equal([expected, expected], commands);
            Assert.Equal(300, expected.Timeout);
        }

        /// <summary>
        ///     A server that cannot be reached leaves every database untried, each one named, with the
        ///     failed open as the inner exception; no drop runs and no pool is cleared.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_connection_that_cannot_be_opened_leaves_every_database_untried_and_named()
        {
            List<string> events = [];

            AggregateException reported = await Assert.ThrowsAsync<AggregateException>(() => SqlDatabaseDrop.DropAllAsync(
                Nowhere,
                ["fx_a", "fx_b"],
                Quick,
                static _ => { },
                (_, _) =>
                {
                    events.Add("run");
                    return Task.CompletedTask;
                },
                () => events.Add("clear"),
                Token));

            Assert.Empty(events);
            _ = Assert.IsType<SqlException>(Assert.Single(reported.InnerExceptions));
            Assert.StartsWith("2 of the 2 test databases were not dropped", reported.Message, StringComparison.Ordinal);
            Assert.Contains(
                "Not tried, as the connection to the server could not be opened, its failure the inner exception: [fx_a], [fx_b].",
                reported.Message,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Failed,", reported.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A SQL Server refusal carrying the given error numbers, built through SqlClient's own
        ///     factories, which it keeps internal.
        /// </summary>
        /// <param name="numbers">The error numbers, in order.</param>
        /// <returns>The exception.</returns>
        private static SqlException Refusal(params int[] numbers)
        {
            const BindingFlags Internal = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
            var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
            ConstructorInfo error = typeof(SqlError).GetConstructor(
                Internal,
                [typeof(int), typeof(byte), typeof(byte), typeof(string), typeof(string), typeof(string), typeof(int), typeof(Exception)])!;
            MethodInfo add = typeof(SqlErrorCollection).GetMethod("Add", Internal, [typeof(SqlError)])!;
            foreach (int number in numbers)
            {
                _ = add.Invoke(errors, [error.Invoke([number, (byte)1, (byte)16, "server", $"Error {number}.", string.Empty, 1, null])]);
            }

            MethodInfo create = typeof(SqlException).GetMethod("CreateException", Internal, [typeof(SqlErrorCollection), typeof(string)])!;
            return (SqlException)create.Invoke(null, [errors, "16.0.0"])!;
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
