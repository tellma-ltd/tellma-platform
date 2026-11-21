// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The batches of this instance's claims and their drain, under a fake clock with no database:
    ///     a batch counted in flight and its slot released once it ended; the drain cancelling the host
    ///     token and waiting for the batches, the final renewal binding the host's shutdown timeout for
    ///     every tenant with a handler still running, the renewals stopped before it, and the
    ///     abandonment warning.
    /// </summary>
    public sealed class JobDispatcherTests : IDisposable
    {
        /// <summary>The worker tests' handlers.</summary>
        private readonly BatchHarness _harness = new(compose: WorkerTestModel.Compose);

        /// <summary>The clock the drain waits on.</summary>
        private readonly SignallingTimeProvider _time = new();

        /// <summary>Collects the dispatcher's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The factory the loggers come from.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The leases the fake runner tracks.</summary>
        private readonly JobLeases _leases;

        /// <summary>The runner the dispatcher starts.</summary>
        private readonly FakeClaimRunner _runner;

        /// <summary>The renewal the drain issues its final renewal through.</summary>
        private readonly FakeRenewal _renewal = new();

        /// <summary>The instance's batch slots.</summary>
        private readonly JobConcurrency _concurrency;

        /// <summary>The dispatcher under test.</summary>
        private readonly JobDispatcher _dispatcher;

        /// <summary>Creates the dispatcher over fakes.</summary>
        public JobDispatcherTests()
        {
            _loggers = new LoggerFactory([_logs]);
            _leases = new JobLeases(_time);
            _runner = new FakeClaimRunner(_leases);
            _concurrency = new JobConcurrency(maxParallelBatches: 4, new TenantPollerWake(_loggers));
            _dispatcher = new JobDispatcher(_runner, _renewal, _leases, _harness.Services.GetRequiredService<JobsMetrics>(), _time, _loggers);
        }

        /// <summary>The host's shutdown timeout, the hosting default.</summary>
        private static TimeSpan ShutdownTimeout => TimeSpan.FromSeconds(30);

        /// <summary>The drain timeout, the option's default.</summary>
        private static TimeSpan DrainTimeout => new JobsOptions().DrainTimeout;

        /// <summary>The audit handler's registration: two rows a batch.</summary>
        private JobHandlerRegistration Audit => _harness.Services.GetRequiredService<JobHandlerRegistry>().TryFind(WorkerTestModel.AuditKey, out JobHandlerRegistration? handler)
            ? handler
            : throw new InvalidOperationException("The worker test model registers the handler.");

        [Fact]
        public async Task A_batch_counts_its_rows_in_flight_while_it_runs_and_releases_its_slot_once_it_ended()
        {
            using MetricCollector<long> inFlight = new(_harness.Services.GetRequiredService<IMeterFactory>(), JobsTelemetryNames.MeterName, JobsTelemetryNames.InFlight);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _runner.Run = (_, _) => release.Task;

            JobPolledClaim polled = Claim(tenantId: 1, rows: 2);
            _dispatcher.Start(polled);
            Assert.Equal(1, _dispatcher.Running);
            Assert.Equal(1, _concurrency.Running(1, WorkerTestModel.AuditKey));

            // --- Ended: the rows leave in_flight and the slot is free ---
            release.SetResult();
            await WaitUntilAsync(() => _dispatcher.Running == 0 && _concurrency.Running(1, WorkerTestModel.AuditKey) == 0);
            Assert.Equal([2L, -2L], inFlight.GetMeasurementSnapshot().Select(static measurement => measurement.Value));
            Assert.All(inFlight.GetMeasurementSnapshot(), static measurement => Assert.Equal(WorkerTestModel.AuditKey, measurement.Tags[JobsTelemetryNames.HandlerTag]));
        }

        [Fact]
        public async Task A_claim_the_instance_had_no_slot_for_is_released_through_the_runner_outside_in_flight_and_frees_its_key()
        {
            using MetricCollector<long> inFlight = new(_harness.Services.GetRequiredService<IMeterFactory>(), JobsTelemetryNames.MeterName, JobsTelemetryNames.InFlight);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _runner.Release = _ => release.Task;

            // --- The release runs among the batches the drain waits for, its key's slot still reserved ---
            JobPolledClaim polled = Claim(tenantId: 1, rows: 2);
            _dispatcher.Release(polled);
            Assert.Equal(1, _dispatcher.Running);
            Assert.Equal(1, _concurrency.Running(1, WorkerTestModel.AuditKey));

            // --- Released: no handler ran, nothing was in flight, and the key's slot is free ---
            release.SetResult();
            await WaitUntilAsync(() => _dispatcher.Running == 0 && _concurrency.Running(1, WorkerTestModel.AuditKey) == 0);
            Assert.Same(polled.Claim, Assert.Single(_runner.Released));
            Assert.Empty(_runner.HostTokens);
            Assert.Empty(inFlight.GetMeasurementSnapshot());
            Assert.Equal(_concurrency.MaxParallelBatches, _concurrency.Free);
        }

        [Fact]
        public async Task A_batch_that_fails_unexpectedly_is_logged_and_still_releases_its_slot()
        {
            _runner.Run = static (_, _) => throw new InvalidOperationException("The runner broke.");

            _dispatcher.Start(Claim(tenantId: 1, rows: 1));

            await WaitUntilAsync(() => _dispatcher.Running == 0 && _concurrency.Free == _concurrency.MaxParallelBatches);
            FakeLogRecord failed = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobBatchFailed");
            Assert.Equal(LogLevel.Error, failed.Level);
        }

        [Fact]
        public async Task A_drain_whose_batches_return_in_time_stops_the_renewals_and_issues_no_final_renewal()
        {
            _runner.Run = static (_, host) => Task.Delay(Timeout.InfiniteTimeSpan, host).ContinueWith(static _ => { }, TaskScheduler.Default);
            _dispatcher.Start(Claim(tenantId: 1, rows: 1));
            int stopped = 0;

            await _dispatcher.DrainAsync(
                DrainTimeout,
                ShutdownTimeout,
                Task.CompletedTask,
                () =>
                {
                    stopped++;
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);

            // --- The host token stopped the handler, which returned: nothing is left leased ---
            Assert.True(_dispatcher.HostToken.IsCancellationRequested);
            Assert.True(Assert.Single(_runner.HostTokens).IsCancellationRequested);
            Assert.Equal(1, stopped);
            Assert.Empty(_renewal.Final);
            Assert.DoesNotContain(_logs.Collector.GetSnapshot(), static record => record.Level >= LogLevel.Warning);
        }

        [Fact]
        public async Task A_handler_still_running_after_the_drain_timeout_is_renewed_once_by_the_shutdown_timeout_and_abandoned()
        {
            // --- Two tenants' handlers ignore the host's stop; a third tenant's returns ---
            TaskCompletionSource never = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _runner.Run = (lease, host) => lease.TenantId == 3 ? Task.Delay(Timeout.InfiniteTimeSpan, host).ContinueWith(static _ => { }, TaskScheduler.Default) : never.Task;
            _dispatcher.Start(Claim(tenantId: 1, rows: 2));
            _dispatcher.Start(Claim(tenantId: 2, rows: 1));
            _dispatcher.Start(Claim(tenantId: 3, rows: 1));
            await WaitUntilAsync(() => _leases.All().Count == 3);
            bool renewalsStopped = false;

            // --- The drain waits its timeout on the fake clock ---
            Task drain = _dispatcher.DrainAsync(
                DrainTimeout,
                TimeSpan.FromSeconds(29.5),
                Task.CompletedTask,
                () =>
                {
                    renewalsStopped = true;
                    Assert.Empty(_renewal.Final);
                    return Task.CompletedTask;
                },
                TestContext.Current.CancellationToken);
            await _time.TimerCreatedAsync();
            await WaitUntilAsync(() => _dispatcher.Running == 2);
            Assert.False(drain.IsCompleted);
            _time.Advance(DrainTimeout);
            await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

            // --- The renewals stopped first; then one final renewal per tenant still holding a lease, by the shutdown timeout rounded up ---
            Assert.True(renewalsStopped);
            Assert.Equal([(1, 30), (2, 30)], _renewal.Final.OrderBy(static final => final.TenantId));

            // --- Each running handler is abandoned with a warning, and the drain's timeout is logged ---
            Assert.All(_leases.All(), static lease => Assert.True(lease.IsAbandoned));
            FakeLogRecord[] abandoned = [.. _logs.Collector.GetSnapshot().Where(static record => record.Id.Name == "JobHandlerAbandoned")];
            Assert.Equal(2, abandoned.Length);
            Assert.All(abandoned, static record => Assert.Equal(LogLevel.Warning, record.Level));
            FakeLogRecord timedOut = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "WorkerDrainTimedOut");
            Assert.Equal(LogLevel.Warning, timedOut.Level);
            Assert.Contains("2 claim(s)", timedOut.Message, StringComparison.Ordinal);
            never.SetResult();
        }

        [Fact]
        public async Task The_drain_waits_for_the_polls_in_flight_and_for_the_batches_they_start()
        {
            TaskCompletionSource poll = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _runner.Run = (_, _) => release.Task;

            Task drain = _dispatcher.DrainAsync(DrainTimeout, ShutdownTimeout, poll.Task, static () => Task.CompletedTask, TestContext.Current.CancellationToken);
            await _time.TimerCreatedAsync();

            // --- The poll ends after the drain began and starts a batch, which the drain waits for too ---
            _dispatcher.Start(Claim(tenantId: 1, rows: 1));
            poll.SetResult();
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.False(drain.IsCompleted);

            release.SetResult();
            await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Empty(_renewal.Final);
        }

        [Fact]
        public async Task A_drain_ends_at_the_hosts_own_deadline_even_when_its_clock_never_reaches_the_drain_timeout()
        {
            // --- A poll that never ends, and a clock that never moves ---
            TaskCompletionSource poll = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            bool renewalsStopped = false;

            Task drain = _dispatcher.DrainAsync(
                DrainTimeout,
                ShutdownTimeout,
                poll.Task,
                () =>
                {
                    renewalsStopped = true;
                    return Task.CompletedTask;
                },
                deadline.Token);
            await _time.TimerCreatedAsync();
            Assert.False(drain.IsCompleted);

            // --- The host's deadline ends the wait; the renewals still stop, and nothing is renewed ---
            await deadline.CancelAsync();
            await drain.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(renewalsStopped);
            Assert.Empty(_renewal.Final);
            poll.SetResult();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _dispatcher.Dispose();
            _time.Dispose();
            _loggers.Dispose();
            _logs.Dispose();
            _harness.Dispose();
        }

        /// <summary>Waits until a condition holds.</summary>
        /// <param name="condition">The condition.</param>
        /// <returns>A task that completes once it held.</returns>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!condition())
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }
        }

        /// <summary>A claim of the audit handler that leased rows, in a slot reserved for it.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="rows">The rows leased.</param>
        /// <returns>The claim.</returns>
        private JobPolledClaim Claim(int tenantId, int rows)
        {
            JobPollPlan plan = new(tenantId, ReadOnly: false, Stopping: false, [new JobPollClaimRequest(Audit, _concurrency.TryReserve(tenantId, Audit))], SampleBacklog: false, SampleOrphans: false, "host-a/41");
            return Assert.Single(FakeJobPoll.Result(plan, _ => rows).Claims);
        }
    }
}
