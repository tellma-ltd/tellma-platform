// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Globalization;
using Tellma.Core.Migrator.Sql;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     The migrator's database-creation retry: which errors it retries, its jittered doubling
    ///     waits and their ceiling, its bound on attempts, the structured events it logs, and that
    ///     every other failure, and a cancellation, surfaces at once.
    /// </summary>
    /// <remarks>
    ///     A retry that caught more than the <c>model</c> lock would turn a real failure, or a timeout
    ///     the server may still finish, into a second creation; one that never stopped would hold a
    ///     tenant's lock and parallel slot for good. Each test here would fail on either.
    /// </remarks>
    public sealed class CreateDatabaseRetryTests
    {
        /// <summary>A policy of four attempts over 100 ms waits doubling to 400 ms.</summary>
        private static readonly CreateDatabaseRetry Policy = new(4, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400));

        /// <summary>Only the model lock, alone or with the creation failure that follows it, is transient.</summary>
        /// <param name="numbers">The error numbers one failed statement reported.</param>
        /// <param name="expected">Whether running the statement again may succeed.</param>
        [Theory]
        [InlineData(new[] { 1807, 1802 }, true)]
        [InlineData(new[] { 1807 }, true)]
        [InlineData(new[] { 1802, 1807 }, true)]

        // --- A creation failure without the lock, an existing database, a deadlock, a timeout, a lost connection ---
        [InlineData(new[] { 1802 }, false)]
        [InlineData(new[] { 1801 }, false)]
        [InlineData(new[] { 1205 }, false)]
        [InlineData(new[] { -2 }, false)]
        [InlineData(new[] { 10054 }, false)]

        // --- The lock beside another real error: the other error decides ---
        [InlineData(new[] { 1807, 5170 }, false)]
        [InlineData(new[] { 1807, 1802, 1801 }, false)]
        [InlineData(new int[0], false)]
        public void Only_the_model_lock_is_transient(int[] numbers, bool expected)
        {
            Assert.Equal(expected, CreateDatabaseRetry.IsTransient(numbers));
        }

        /// <summary>An exception that is not SQL Server's is never transient, whatever its message says.</summary>
        [Fact]
        public void An_exception_that_is_not_sql_servers_is_not_transient()
        {
            Assert.False(CreateDatabaseRetry.IsTransient(new InvalidOperationException("Could not obtain exclusive lock on database 'model'.")));
            Assert.False(CreateDatabaseRetry.IsTransient(new TimeoutException()));
        }

        /// <summary>The waits double from the base, stop at the ceiling, and vary by a quarter either way.</summary>
        [Fact]
        public void The_waits_double_from_the_base_stop_at_the_ceiling_and_vary_by_a_quarter_either_way()
        {
            // --- The centre of the jitter: 100, 200, 400, then held at 400 ---
            Assert.Equal(
                [100, 200, 400, 400, 400],
                Enumerable.Range(1, 5).Select(retry => Policy.Delay(retry, 0.5).TotalMilliseconds));

            // --- The edges: three quarters at the low end, just under five quarters at the high ---
            Assert.Equal(TimeSpan.FromMilliseconds(75), Policy.Delay(1, 0));
            Assert.InRange(Policy.Delay(3, 0.999999), TimeSpan.FromMilliseconds(499.9), TimeSpan.FromMilliseconds(500));

            // --- A retry count far past any bound neither overflows nor passes the ceiling ---
            Assert.Equal(TimeSpan.FromMilliseconds(400), Policy.Delay(int.MaxValue, 0.5));
        }

        /// <summary>The migrator's default: five attempts, with waits of about 2, 4, 8 and 15 seconds between them.</summary>
        [Fact]
        public void The_default_policy_makes_five_attempts_about_two_four_eight_and_fifteen_seconds_apart()
        {
            CreateDatabaseRetry policy = new(CreateDatabaseRetry.DefaultMaxAttempts);

            Assert.Equal(5, policy.MaxAttempts);
            Assert.Equal(
                [2, 4, 8, 15],
                Enumerable.Range(1, policy.MaxAttempts - 1).Select(retry => policy.Delay(retry, 0.5).TotalSeconds));
        }

        /// <summary>The policy refuses bounds and arguments outside their domains.</summary>
        [Fact]
        public void Bounds_and_arguments_outside_their_domains_are_refused()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(0));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(0, 0.5));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, -0.1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, double.NaN));
        }

        /// <summary>
        ///     A transient failure runs again after its wait until an attempt succeeds, and each retry is
        ///     logged as a structured warning naming the database, the server, the attempt and the wait.
        /// </summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_transient_failure_runs_again_after_its_wait_until_an_attempt_succeeds_and_each_retry_is_logged()
        {
            Harness harness = new(failures: 2, new TransientFailure());

            await harness.RunAsync(CancellationToken.None);

            Assert.Equal(3, harness.Runs);
            Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)], harness.Waits);

            // --- One warning per retry, its properties structured ---
            IReadOnlyList<FakeLogRecord> records = harness.Logger.Collector.GetSnapshot();
            Assert.Equal(2, records.Count);
            Assert.All(records, static record =>
            {
                Assert.Equal(LogLevel.Warning, record.Level);
                Assert.Equal("MigratorDatabaseCreationRetrying", record.Id.Name);
                Assert.Equal("probe", record.GetStructuredStateValue("Database"));
                Assert.Equal("server-1", record.GetStructuredStateValue("Server"));
                Assert.Equal("4", record.GetStructuredStateValue("MaxAttempts"));
            });
            Assert.Equal(["1", "2"], records.Select(static record => record.GetStructuredStateValue("Attempt")));
            Assert.Equal(
                [TimeSpan.FromMilliseconds(100).ToString("c", CultureInfo.InvariantCulture), TimeSpan.FromMilliseconds(200).ToString("c", CultureInfo.InvariantCulture)],
                records.Select(static record => record.GetStructuredStateValue("RetryDelay")));
            Assert.Contains("because the model database was locked (attempt 1 of 4)", records[0].Message, StringComparison.Ordinal);
        }

        /// <summary>A transient failure on every attempt stops at the bound, logs giving up, and surfaces the last attempt's own exception.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_transient_failure_on_every_attempt_stops_at_the_bound_and_surfaces_the_last_exception()
        {
            TransientFailure last = new();
            Harness harness = new(failures: int.MaxValue, last);

            TransientFailure surfaced = await Assert.ThrowsAsync<TransientFailure>(() => harness.RunAsync(CancellationToken.None));

            Assert.Same(last, surfaced);
            Assert.Equal(4, harness.Runs);
            Assert.Equal(3, harness.Waits.Count);

            // --- Three retries, then one giving-up warning naming every attempt ---
            IReadOnlyList<FakeLogRecord> records = harness.Logger.Collector.GetSnapshot();
            Assert.Equal(
                ["MigratorDatabaseCreationRetrying", "MigratorDatabaseCreationRetrying", "MigratorDatabaseCreationRetrying", "MigratorDatabaseCreationGaveUp"],
                records.Select(static record => record.Id.Name));
            FakeLogRecord gaveUp = records[^1];
            Assert.Equal(LogLevel.Warning, gaveUp.Level);
            Assert.Equal("4", gaveUp.GetStructuredStateValue("Attempts"));
            Assert.Equal("probe", gaveUp.GetStructuredStateValue("Database"));
        }

        /// <summary>A policy of one attempt never runs a creation again: the first transient failure surfaces, logged as giving up.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_policy_of_one_attempt_surfaces_the_first_transient_failure()
        {
            Harness harness = new(failures: int.MaxValue, new TransientFailure(), policy: new CreateDatabaseRetry(1));

            _ = await Assert.ThrowsAsync<TransientFailure>(() => harness.RunAsync(CancellationToken.None));

            Assert.Equal(1, harness.Runs);
            Assert.Empty(harness.Waits);
            Assert.Equal("MigratorDatabaseCreationGaveUp", Assert.Single(harness.Logger.Collector.GetSnapshot()).Id.Name);
        }

        /// <summary>A failure that is not transient, a timeout among them, surfaces from the first attempt with no wait and no log.</summary>
        /// <param name="kind">Which failure the attempt throws.</param>
        /// <returns>A task that completes when the test is done.</returns>
        [Theory]
        [InlineData("exists")]
        [InlineData("timeout")]
        public async Task A_failure_that_is_not_transient_surfaces_at_once(string kind)
        {
            Exception failure = kind == "timeout"
                ? new TimeoutException("Execution Timeout Expired.")
                : new InvalidOperationException("Database 'probe' already exists.");
            Harness harness = new(failures: int.MaxValue, failure);

            Exception surfaced = await Assert.ThrowsAnyAsync<Exception>(() => harness.RunAsync(CancellationToken.None));

            Assert.Same(failure, surfaced);
            Assert.Equal(1, harness.Runs);
            Assert.Empty(harness.Waits);
            Assert.Empty(harness.Logger.Collector.GetSnapshot());
        }

        /// <summary>A transient failure after a cancellation surfaces at once rather than waiting for another attempt.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_transient_failure_after_a_cancellation_surfaces_at_once()
        {
            using CancellationTokenSource cancellation = new();
            Harness harness = new(failures: int.MaxValue, new TransientFailure(), onRun: cancellation.Cancel);

            _ = await Assert.ThrowsAsync<TransientFailure>(() => harness.RunAsync(cancellation.Token));

            Assert.Equal(1, harness.Runs);
            Assert.Empty(harness.Waits);
            Assert.Empty(harness.Logger.Collector.GetSnapshot());
        }

        /// <summary>A cancellation during a wait ends the loop with the wait's cancellation, and no further attempt.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task A_cancellation_during_a_wait_ends_the_loop()
        {
            using CancellationTokenSource cancellation = new();
            Harness harness = new(failures: int.MaxValue, new TransientFailure(), onWait: cancellation.Cancel);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(cancellation.Token));

            Assert.Equal(1, harness.Runs);
            _ = Assert.Single(harness.Waits);
        }

        /// <summary>The production wait honours the token: a cancelled run does not sit out the delay.</summary>
        /// <returns>A task that completes when the test is done.</returns>
        [Fact]
        public async Task The_real_wait_is_cut_short_by_a_cancellation()
        {
            // A wait far longer than the test's own budget; the cancellation must end it.
            CreateDatabaseRetry patient = new(2, TimeSpan.FromHours(1), TimeSpan.FromHours(1));
            using CancellationTokenSource cancellation = new();
            int runs = 0;

            Task run = patient.RunAsync(
                _ =>
                {
                    runs++;
                    return Task.FromException(new TransientFailure());
                },
                new FakeLogger(),
                "probe",
                "server-1",
                static exception => exception is TransientFailure,
                Task.Delay,
                static () => 0.5,
                cancellation.Token);
            await cancellation.CancelAsync();

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run.WaitAsync(TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
            Assert.Equal(1, runs);
        }

        /// <summary>The failure the harness's classifier treats as transient, standing in for SQL Server's 1807.</summary>
        private sealed class TransientFailure : Exception
        {
            /// <summary>Creates the failure.</summary>
            public TransientFailure()
                : base("Could not obtain exclusive lock on database 'model'.")
            {
            }
        }

        /// <summary>
        ///     Drives the policy's loop over a creation that fails a set number of times, a wait that
        ///     records its delay and returns at once, a jitter fixed at the centre, and a fake logger.
        /// </summary>
        /// <param name="failures">How many attempts fail before one succeeds.</param>
        /// <param name="failure">What each failing attempt throws.</param>
        /// <param name="onRun">Called at the start of each attempt.</param>
        /// <param name="onWait">Called at the start of each wait.</param>
        /// <param name="policy">The policy; <see cref="Policy" /> when null.</param>
        private sealed class Harness(int failures, Exception failure, Action? onRun = null, Action? onWait = null, CreateDatabaseRetry? policy = null)
        {
            /// <summary>How many attempts started.</summary>
            public int Runs { get; private set; }

            /// <summary>Each wait's delay, in order.</summary>
            public List<TimeSpan> Waits { get; } = [];

            /// <summary>The logger the loop writes to.</summary>
            public FakeLogger Logger { get; } = new();

            /// <summary>Runs the loop.</summary>
            /// <param name="cancellationToken">Cancels the loop.</param>
            /// <returns>A task that completes when the loop ends.</returns>
            public Task RunAsync(CancellationToken cancellationToken)
            {
                return (policy ?? Policy).RunAsync(
                    _ =>
                    {
                        Runs++;
                        onRun?.Invoke();
                        return Runs <= failures ? Task.FromException(failure) : Task.CompletedTask;
                    },
                    Logger,
                    "probe",
                    "server-1",
                    static exception => exception is TransientFailure,
                    (delay, token) =>
                    {
                        Waits.Add(delay);
                        onWait?.Invoke();
                        return token.IsCancellationRequested ? Task.FromCanceled(token) : Task.CompletedTask;
                    },
                    static () => 0.5,
                    cancellationToken);
            }
        }
    }
}
