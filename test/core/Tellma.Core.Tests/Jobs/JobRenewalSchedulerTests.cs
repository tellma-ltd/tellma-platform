// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Jobs;
using Tellma.Core.Tests.Data.Batches;

namespace Tellma.Core.Tests.Jobs
{
    /// <summary>
    ///     The renewal schedule under a fake clock with no database: one renewal per tenant once its
    ///     earliest carried lease is due, none while one is in flight, a failed renewal logged and
    ///     backed off by the interval, a handler that ignores a requested cancel abandoned a lease
    ///     length later, one that stopped one item of several still renewed, and a check that throws
    ///     leaving the schedule running.
    /// </summary>
    public sealed class JobRenewalSchedulerTests : IAsyncDisposable
    {
        /// <summary>The worker tests' handlers.</summary>
        private readonly BatchHarness _harness = new(compose: WorkerTestModel.Compose);

        /// <summary>The clock the leases and the schedule run on.</summary>
        private readonly SignallingTimeProvider _time = new();

        /// <summary>Collects the schedule's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The factory the loggers come from.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The leases this instance holds.</summary>
        private readonly JobLeases _leases;

        /// <summary>The renewal the schedule runs.</summary>
        private readonly FakeRenewal _renewal = new();

        /// <summary>The schedule under test, never started: each test checks it by hand.</summary>
        private readonly JobRenewalScheduler _scheduler;

        /// <summary>Creates the schedule over fakes.</summary>
        public JobRenewalSchedulerTests()
        {
            _loggers = new LoggerFactory([_logs]);
            _leases = new JobLeases(_time);
            _scheduler = new JobRenewalScheduler(_renewal, _leases, _time, _loggers);

            // A renewal stamps the leases it carried, as the real one does.
            _renewal.Renew = (tenantId, _) =>
            {
                foreach (JobLease lease in _leases.InFlight(tenantId))
                {
                    lease.Renewed(_time.GetTimestamp());
                }

                return Task.CompletedTask;
            };
        }

        /// <summary>The audit handler's registration: the default lease of 300 s, so a renewal every 60 s.</summary>
        private JobHandlerRegistration Audit => _harness.Services.GetRequiredService<JobHandlerRegistry>().TryFind(WorkerTestModel.AuditKey, out JobHandlerRegistration? handler)
            ? handler
            : throw new InvalidOperationException("The worker test model registers the handler.");

        [Fact]
        public async Task A_tenant_is_renewed_once_for_all_its_leases_a_fifth_of_the_lease_after_the_last_renewal()
        {
            using JobLease first = Track(tenantId: 1, jobId: 1);
            _time.Advance(TimeSpan.FromSeconds(20));
            using JobLease second = Track(tenantId: 1, jobId: 2);
            using JobLease other = Track(tenantId: 2, jobId: 3);

            // --- Not due before the earliest lease's fifth ---
            _time.Advance(TimeSpan.FromSeconds(39));
            await _scheduler.CheckAsync();
            Assert.Empty(_renewal.Renewed);

            // --- Due: one renewal for tenant 1's two leases; tenant 2's lease is not yet due ---
            _time.Advance(TimeSpan.FromSeconds(1));
            await _scheduler.CheckAsync();
            Assert.Equal([1], _renewal.Renewed);

            // --- Renewed, the schedule restarts from the renewal ---
            _time.Advance(TimeSpan.FromSeconds(30));
            await _scheduler.CheckAsync();
            Assert.Equal([1, 2], _renewal.Renewed);
            _time.Advance(TimeSpan.FromSeconds(30));
            await _scheduler.CheckAsync();
            Assert.Equal([1, 2, 1], _renewal.Renewed);
        }

        [Fact]
        public async Task A_renewal_in_flight_is_not_started_again()
        {
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _renewal.Renew = (_, _) => release.Task;
            using JobLease lease = Track(tenantId: 1, jobId: 1);
            _time.Advance(TimeSpan.FromSeconds(60));

            Task running = _scheduler.CheckAsync();
            await WaitUntilAsync(() => _renewal.Renewed.Count == 1);
            await _scheduler.CheckAsync();
            Assert.Single(_renewal.Renewed);

            release.SetResult();
            await running;
        }

        [Fact]
        public async Task A_failed_renewal_is_logged_and_the_tenant_waits_a_renewal_interval_before_the_next()
        {
            _renewal.Renew = static (_, _) => throw new InvalidOperationException("The tenant's database is unreachable.");
            using JobLease lease = Track(tenantId: 1, jobId: 1);
            _time.Advance(TimeSpan.FromSeconds(60));

            await _scheduler.CheckAsync();
            FakeLogRecord failed = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobRenewalFailed");
            Assert.Equal(LogLevel.Warning, failed.Level);

            // --- The lease is still due, but the tenant waits the interval ---
            _time.Advance(TimeSpan.FromSeconds(59));
            await _scheduler.CheckAsync();
            Assert.Single(_renewal.Renewed);
            _time.Advance(TimeSpan.FromSeconds(1));
            await _scheduler.CheckAsync();
            Assert.Equal(2, _renewal.Renewed.Count);
        }

        [Fact]
        public async Task A_handler_that_ignores_a_requested_cancel_is_abandoned_a_lease_length_later_and_no_longer_renewed()
        {
            using JobLease lease = Track(tenantId: 1, jobId: 1);
            lease.RequestCancel(1);

            // --- One second short of a lease length: still carried ---
            _time.Advance(TimeSpan.FromSeconds(299));
            await _scheduler.CheckAsync();
            Assert.False(lease.IsAbandoned);

            // --- A lease length after the cancel: abandoned with a warning, and left to lapse ---
            _time.Advance(TimeSpan.FromSeconds(1));
            int renewed = _renewal.Renewed.Count;
            await _scheduler.CheckAsync();
            Assert.True(lease.IsAbandoned);
            FakeLogRecord abandoned = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobHandlerAbandoned");
            Assert.Equal(LogLevel.Warning, abandoned.Level);
            Assert.Equal(renewed, _renewal.Renewed.Count);
        }

        [Fact]
        public async Task A_cancel_of_one_item_keeps_the_lease_renewed_until_every_item_was_cancelled()
        {
            using JobLease lease = Track(tenantId: 1, jobs: [1, 2]);
            lease.RequestCancel(1);

            // --- A lease length later the handler runs on with item 2: the lease is still renewed ---
            _time.Advance(TimeSpan.FromSeconds(300));
            await _scheduler.CheckAsync();
            Assert.False(lease.IsAbandoned);
            Assert.Equal([1], _renewal.Renewed);

            // --- Item 2's cancel starts the clock: abandoned a lease length after it, not before ---
            lease.RequestCancel(2);
            _time.Advance(TimeSpan.FromSeconds(299));
            await _scheduler.CheckAsync();
            Assert.False(lease.IsAbandoned);
            _time.Advance(TimeSpan.FromSeconds(1));
            await _scheduler.CheckAsync();
            Assert.True(lease.IsAbandoned);
        }

        [Fact]
        public async Task A_check_that_throws_is_logged_and_the_next_check_renews_all_the_same()
        {
            // --- A logger that fails the abandonment's warning, under a scheduler of its own ---
            using FakeLoggerProvider logs = new();
            using LoggerFactory loggers = new([logs, new FailingLoggerProvider("JobHandlerAbandoned")]);
            using JobRenewalScheduler scheduler = new(_renewal, _leases, _time, loggers);
            using JobLease ignoring = Track(tenantId: 1, jobs: [1]);
            using JobLease running = Track(tenantId: 2, jobs: [2]);
            ignoring.RequestCancel(1);
            _time.Advance(TimeSpan.FromSeconds(300));

            // --- The first check throws at the abandonment, before any renewal; it is logged ---
            scheduler.Start();
            await _time.TimerCreatedAsync();
            _time.Advance(JobRenewalScheduler.CheckEvery);
            await WaitUntilAsync(() => logs.Collector.GetSnapshot().Any(static record => record.Id.Name == "JobRenewalFailed"));
            Assert.Empty(_renewal.Renewed);

            // --- The next check renews tenant 2's lease ---
            _time.Advance(JobRenewalScheduler.CheckEvery);
            await WaitUntilAsync(() => _renewal.Renewed.Contains(2));
            await scheduler.StopAsync();
        }

        [Fact]
        public async Task A_started_schedule_checks_every_second_and_stops_with_its_renewals()
        {
            TaskCompletionSource<CancellationToken> renewing = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _renewal.Renew = async (_, cancellationToken) =>
            {
                renewing.TrySetResult(cancellationToken);
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            };
            using JobLease lease = Track(tenantId: 1, jobId: 1);
            _time.Advance(TimeSpan.FromSeconds(59));

            _scheduler.Start();
            await _time.TimerCreatedAsync();
            _time.Advance(JobRenewalScheduler.CheckEvery);

            // --- The check started the renewal; the stop cancels it and waits for it ---
            CancellationToken token = await renewing.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            await _scheduler.StopAsync().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.True(token.IsCancellationRequested);
            Assert.DoesNotContain(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobRenewalFailed");
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await _scheduler.StopAsync();
            _scheduler.Dispose();
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

        /// <summary>Tracks a claim of one audit row.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="jobId">The job.</param>
        /// <returns>The lease.</returns>
        private JobLease Track(int tenantId, int jobId)
        {
            return Track(tenantId, [jobId]);
        }

        /// <summary>Tracks a claim of audit rows.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="jobs">The jobs, in claim order.</param>
        /// <returns>The lease.</returns>
        private JobLease Track(int tenantId, int[] jobs)
        {
            ClaimedJob[] rows = [.. jobs.Select(static jobId => new ClaimedJob(new Job { Id = jobId, HandlerKey = WorkerTestModel.AuditKey, RunAsUserId = 7, Attempts = 1 }, null, null, Guid.Empty))];
            return _leases.Track(tenantId, Audit, Guid.NewGuid(), rows, TestContext.Current.CancellationToken);
        }

        /// <summary>A logger provider whose loggers throw on one event, as a faulty logging provider would.</summary>
        /// <param name="eventName">The event that throws.</param>
        private sealed class FailingLoggerProvider(string eventName) : ILoggerProvider
        {
            /// <inheritdoc />
            public ILogger CreateLogger(string categoryName)
            {
                return new FailingLogger(eventName);
            }

            /// <inheritdoc />
            public void Dispose()
            {
            }
        }

        /// <summary>A logger that throws on one event.</summary>
        /// <param name="eventName">The event that throws.</param>
        private sealed class FailingLogger(string eventName) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            /// <inheritdoc />
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (eventId.Name == eventName)
                {
                    throw new InvalidOperationException("The logging provider failed.");
                }
            }
        }
    }
}
