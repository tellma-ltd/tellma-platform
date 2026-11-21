// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Testing.Support.Sql;

namespace Tellma.Core.Testing.Tests.Sql
{
    /// <summary>
    ///     The fixtures' database-creation retry: which errors it retries, its jittered doubling
    ///     waits and their ceiling, its bound on runs, and that every other failure, and a
    ///     cancellation, surfaces at once.
    /// </summary>
    /// <remarks>
    ///     A retry that caught more than the <c>model</c> lock would turn a real failure into a slow
    ///     green, and one that never stopped would hang a suite; each test here would fail on either.
    /// </remarks>
    public sealed class CreateDatabaseRetryTests
    {
        /// <summary>A policy of four runs over 100 ms waits doubling to 400 ms.</summary>
        private static readonly CreateDatabaseRetry Policy = new(4, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(400));

        /// <summary>Only the model lock, alone or with the creation failure that follows it, is transient.</summary>
        /// <param name="numbers">The error numbers one failed statement reported.</param>
        /// <param name="expected">Whether a retry may succeed.</param>
        [Theory]
        [InlineData(new[] { 1807, 1802 }, true)]
        [InlineData(new[] { 1807 }, true)]
        [InlineData(new[] { 1802, 1807 }, true)]

        // --- A creation failure without the lock, an existing database, a deadlock, a timeout ---
        [InlineData(new[] { 1802 }, false)]
        [InlineData(new[] { 1801 }, false)]
        [InlineData(new[] { 1205 }, false)]
        [InlineData(new[] { -2 }, false)]

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

            // --- The fixtures' policy: about 1, 2, 4, 8, then 10 seconds ---
            Assert.Equal(
                [1, 2, 4, 8, 10],
                Enumerable.Range(1, 5).Select(retry => CreateDatabaseRetry.Default.Delay(retry, 0.5).TotalSeconds));
            Assert.Equal(6, CreateDatabaseRetry.Default.MaxAttempts);
        }

        /// <summary>The policy refuses bounds and arguments outside their domains.</summary>
        [Fact]
        public void Bounds_and_arguments_outside_their_domains_are_refused()
        {
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(0, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(1, TimeSpan.Zero, TimeSpan.FromSeconds(1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => new CreateDatabaseRetry(1, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(1)));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(0, 0.5));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, 1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, -0.1));
            _ = Assert.Throws<ArgumentOutOfRangeException>(() => Policy.Delay(1, double.NaN));
        }

        /// <summary>A transient failure is run again after its wait until a run succeeds, and each retry is logged.</summary>
        [Fact]
        public async Task A_transient_failure_is_run_again_after_its_wait_until_a_run_succeeds()
        {
            Harness harness = new(failures: 2, new TransientFailure());

            await harness.RunAsync(CancellationToken.None);

            Assert.Equal(3, harness.Runs);
            Assert.Equal([TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(200)], harness.Waits);
            Assert.Equal(2, harness.Log.Count);
            Assert.All(harness.Log, line => Assert.Contains("CREATE DATABASE [probe] failed with a transient error", line, StringComparison.Ordinal));
            Assert.StartsWith("CREATE DATABASE [probe] failed with a transient error on attempt 1 of 4; retrying in 0.1 s: TransientFailure:", harness.Log[0], StringComparison.Ordinal);
        }

        /// <summary>A transient failure on every run stops at the bound and surfaces the last run's own exception.</summary>
        [Fact]
        public async Task A_transient_failure_on_every_run_stops_at_the_bound_and_surfaces_the_last_exception()
        {
            TransientFailure last = new();
            Harness harness = new(failures: int.MaxValue, last);

            TransientFailure surfaced = await Assert.ThrowsAsync<TransientFailure>(() => harness.RunAsync(CancellationToken.None));

            Assert.Same(last, surfaced);
            Assert.Equal(4, harness.Runs);
            Assert.Equal(3, harness.Waits.Count);
            Assert.Equal(4, harness.Log.Count);
            Assert.Contains("on attempt 4 of 4; giving up", harness.Log[^1], StringComparison.Ordinal);
        }

        /// <summary>A failure that is not transient surfaces from the first run, with no wait and no log.</summary>
        [Fact]
        public async Task A_failure_that_is_not_transient_surfaces_at_once()
        {
            Harness harness = new(failures: int.MaxValue, new InvalidOperationException("Database 'probe' already exists."));

            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => harness.RunAsync(CancellationToken.None));

            Assert.Equal(1, harness.Runs);
            Assert.Empty(harness.Waits);
            Assert.Empty(harness.Log);
        }

        /// <summary>A transient failure after a cancellation surfaces at once rather than waiting for another run.</summary>
        [Fact]
        public async Task A_transient_failure_after_a_cancellation_surfaces_at_once()
        {
            using CancellationTokenSource cancellation = new();
            Harness harness = new(failures: int.MaxValue, new TransientFailure(), onRun: cancellation.Cancel);

            _ = await Assert.ThrowsAsync<TransientFailure>(() => harness.RunAsync(cancellation.Token));

            Assert.Equal(1, harness.Runs);
            Assert.Empty(harness.Waits);
        }

        /// <summary>A cancellation during a wait ends the loop with the wait's cancellation, and no further run.</summary>
        [Fact]
        public async Task A_cancellation_during_a_wait_ends_the_loop()
        {
            using CancellationTokenSource cancellation = new();
            Harness harness = new(failures: int.MaxValue, new TransientFailure(), onWait: cancellation.Cancel);

            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => harness.RunAsync(cancellation.Token));

            Assert.Equal(1, harness.Runs);
            _ = Assert.Single(harness.Waits);
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
        ///     Drives the policy's loop over an operation that fails a set number of times, a wait that
        ///     records its delay and returns at once, and a jitter fixed at the centre.
        /// </summary>
        /// <param name="failures">How many runs fail before one succeeds.</param>
        /// <param name="failure">What each failing run throws.</param>
        /// <param name="onRun">Called at the start of each run.</param>
        /// <param name="onWait">Called at the start of each wait.</param>
        private sealed class Harness(int failures, Exception failure, Action? onRun = null, Action? onWait = null)
        {
            /// <summary>How many runs started.</summary>
            public int Runs { get; private set; }

            /// <summary>Each wait's delay, in order.</summary>
            public List<TimeSpan> Waits { get; } = [];

            /// <summary>Each logged line, in order.</summary>
            public List<string> Log { get; } = [];

            /// <summary>Runs the loop.</summary>
            /// <param name="cancellationToken">Cancels the loop.</param>
            /// <returns>A task that completes when the loop ends.</returns>
            public Task RunAsync(CancellationToken cancellationToken)
            {
                return Policy.RunAsync(
                    _ =>
                    {
                        Runs++;
                        onRun?.Invoke();
                        return Runs <= failures ? Task.FromException(failure) : Task.CompletedTask;
                    },
                    "CREATE DATABASE [probe]",
                    Log.Add,
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
