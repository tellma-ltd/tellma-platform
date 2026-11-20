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
    ///     One tenant's poller under a fake clock, with no database: the adaptive interval doubling
    ///     and resetting, the immediate re-poll after a full claim, nudges coalescing into one wake,
    ///     the keys a read-only tenant claims, the slots freed by claims that leased nothing and by a
    ///     poll that failed, the sample cadences, and a stop that lets the poll in flight finish.
    /// </summary>
    public sealed class TenantPollerTests : IDisposable
    {
        /// <summary>The tenant polled.</summary>
        private const int TenantId = 4;

        /// <summary>The worker tests' handlers.</summary>
        private readonly BatchHarness _harness = new(compose: WorkerTestModel.Compose);

        /// <summary>The clock the poller waits on.</summary>
        private readonly SignallingTimeProvider _time = new();

        /// <summary>The poll the poller runs.</summary>
        private readonly FakeJobPoll _poll = new();

        /// <summary>Where the claims that leased rows go.</summary>
        private readonly FakeDispatch _dispatch = new();

        /// <summary>Collects the poller's logs.</summary>
        private readonly FakeLoggerProvider _logs = new();

        /// <summary>The factory the loggers come from.</summary>
        private readonly LoggerFactory _loggers;

        /// <summary>The wake seam an enqueue nudges the poller through.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>The instance's batch slots: two.</summary>
        private readonly JobConcurrency _concurrency;

        /// <summary>The pollers the test started, stopped when it ends.</summary>
        private readonly List<TenantPoller> _pollers = [];

        /// <summary>Creates the poller's services.</summary>
        public TenantPollerTests()
        {
            _loggers = new LoggerFactory([_logs]);
            _wake = new TenantPollerWake(_loggers);
            _concurrency = new JobConcurrency(maxParallelBatches: 2, _wake);
        }

        /// <summary>The job options: the default intervals, one and thirty seconds.</summary>
        private static JobsOptions Options { get; } = new();

        [Theory]
        [InlineData(1, "Nothing", 2)]
        [InlineData(8, "Nothing", 16)]
        [InlineData(16, "Nothing", 30)]
        [InlineData(30, "Nothing", 30)]
        [InlineData(0, "Nothing", 1)]
        [InlineData(16, "Some", 1)]
        [InlineData(16, "Full", 0)]
        public void The_wait_doubles_after_nothing_resets_after_something_and_vanishes_after_a_full_poll(int current, string finding, int next)
        {
            Assert.Equal(TimeSpan.FromSeconds(next), TenantPoller.Next(TimeSpan.FromSeconds(current), Enum.Parse<JobPollFinding>(finding), Options.MinPollInterval, Options.MaxPollInterval));
        }

        [Fact]
        public void A_failed_poll_waits_the_longest_interval()
        {
            Assert.Equal(Options.MaxPollInterval, TenantPoller.Next(TimeSpan.FromSeconds(1), finding: null, Options.MinPollInterval, Options.MaxPollInterval));
        }

        [Fact]
        public async Task An_idle_tenant_is_polled_at_once_then_after_two_and_four_seconds()
        {
            Start();
            await _poll.PolledAsync(1);

            // --- Two seconds, then four: nothing earlier ---
            await AdvanceAsync(TimeSpan.FromSeconds(1.9));
            Assert.Single(_poll.Plans);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(2);

            await AdvanceAsync(TimeSpan.FromSeconds(3.9));
            Assert.Equal(2, _poll.Plans.Count);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(3);
        }

        [Fact]
        public async Task A_full_claim_polls_again_at_once_and_a_partial_one_waits_the_shortest_interval()
        {
            // --- Poll 1 fills the recompute key's batch of one; poll 2 leases one audit row of two ---
            _poll.Answer = (plan, number) => Task.FromResult(FakeJobPoll.Result(plan, handler => (number, handler.Key) switch
            {
                (1, WorkerTestModel.RecomputeKey) => 1,
                (2, WorkerTestModel.AuditKey) => 1,
                _ => 0,
            }));
            Start();

            await _poll.PolledAsync(2);
            await AdvanceAsync(TimeSpan.FromSeconds(0.9));
            Assert.Equal(2, _poll.Plans.Count);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(3);

            // --- The claims that leased rows were dispatched, in their slots ---
            Assert.Equal([WorkerTestModel.RecomputeKey, WorkerTestModel.AuditKey], _dispatch.Started.Select(static polled => polled.Claim.Handler.Key));
        }

        [Fact]
        public async Task Ten_nudges_while_a_poll_runs_wake_the_poller_once()
        {
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _poll.Answer = async (plan, number) =>
            {
                if (number == 1)
                {
                    await release.Task.ConfigureAwait(false);
                }

                return FakeJobPoll.Result(plan, static _ => 0);
            };
            Start();
            await _poll.PolledAsync(1);

            // --- Ten nudges, through the seam an enqueue uses, while the first poll runs ---
            for (int nudge = 0; nudge < 10; nudge++)
            {
                _wake.Wake(TenantId);
            }

            release.SetResult();

            // --- One poll at once for all ten, then the poller waits on its timer ---
            await _poll.PolledAsync(2);
            await _time.TimerCreatedAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.Equal(2, _poll.Plans.Count);

            // --- A nudge while it waits wakes it before the timer ---
            _wake.Wake(TenantId);
            await _poll.PolledAsync(3);
        }

        [Fact]
        public async Task A_read_only_tenant_claims_only_the_keys_that_run_while_read_only()
        {
            TenantPoller poller = Start(readOnly: true);
            await _poll.PolledAsync(1);
            Assert.Equal([WorkerTestModel.AuditKey], Plan(1).Claims.Select(static claim => claim.Handler.Key));
            Assert.True(Plan(1).ReadOnly);

            // --- Back to active, every key is claimed again ---
            poller.ReadOnly = false;
            await AdvanceAsync(TimeSpan.FromSeconds(2));
            await _poll.PolledAsync(2);
            Assert.Equal([WorkerTestModel.AuditKey, WorkerTestModel.RecomputeKey], Plan(2).Claims.Select(static claim => claim.Handler.Key));
            Assert.False(Plan(2).ReadOnly);
        }

        [Fact]
        public async Task A_claim_that_leased_nothing_frees_its_slot_and_a_key_without_a_free_slot_is_left_out()
        {
            // --- Poll 1 fills the recompute key's batch of one, whose slot stays taken while its batch runs ---
            _poll.Answer = (plan, number) => Task.FromResult(FakeJobPoll.Result(plan, handler => number == 1 && handler.Key == WorkerTestModel.RecomputeKey ? 1 : 0));
            Start();

            // --- Poll 2 follows at once and leaves the busy key out: its one batch at a time is running ---
            await _poll.PolledAsync(2);
            Assert.Equal([WorkerTestModel.AuditKey], Plan(2).Claims.Select(static claim => claim.Handler.Key));
            await _time.TimerCreatedAsync();
            Assert.Equal(1, _concurrency.Running(TenantId, WorkerTestModel.RecomputeKey));
            Assert.Equal(0, _concurrency.Running(TenantId, WorkerTestModel.AuditKey));
            Assert.Equal(1, _concurrency.Free);

            // --- The batch ends: its slot is free, and the tenant refused one is woken at once ---
            Assert.True(_dispatch.Started.TryPeek(out JobPolledClaim? running));
            running.Reservation!.Dispose();
            await _poll.PolledAsync(3);
            Assert.Equal([WorkerTestModel.AuditKey, WorkerTestModel.RecomputeKey], Plan(3).Claims.Select(static claim => claim.Handler.Key));
        }

        [Fact]
        public async Task A_failed_poll_is_logged_frees_every_slot_and_waits_the_longest_interval()
        {
            _poll.Answer = (plan, number) => number == 1 ? throw new InvalidOperationException("The tenant's database is unreachable.") : Task.FromResult(FakeJobPoll.Result(plan, static _ => 0));
            Start();
            await _poll.PolledAsync(1);
            await _time.TimerCreatedAsync();

            // --- Logged with the tenant in scope; the slots it reserved are free ---
            FakeLogRecord failed = Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobPollFailed");
            Assert.Equal(LogLevel.Error, failed.Level);
            Assert.Contains(failed.Scopes.OfType<IEnumerable<KeyValuePair<string, object?>>>().SelectMany(static scope => scope), static pair => pair.Key == "TenantId" && Equals(pair.Value, TenantId));
            Assert.Equal(2, _concurrency.Free);

            // --- The next poll comes after thirty seconds ---
            await AdvanceAsync(TimeSpan.FromSeconds(29.9), awaitTimer: false);
            Assert.Single(_poll.Plans);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(2);
        }

        [Fact]
        public async Task The_backlog_rides_one_poll_a_minute_and_the_orphan_sample_one_an_hour_from_the_first_poll_on()
        {
            // Every poll finds nothing, so the poller waits two, four, eight, then up to thirty seconds.
            Start();
            await _poll.PolledAsync(1);
            Assert.True(Plan(1).SampleBacklog);
            Assert.True(Plan(1).SampleOrphans);

            // --- Within the minute, neither sample ---
            await AdvanceAsync(TimeSpan.FromSeconds(2));
            await _poll.PolledAsync(2);
            Assert.False(Plan(2).SampleBacklog);
            Assert.False(Plan(2).SampleOrphans);

            // --- Polls a minute after the first carry the backlog sample again, not the orphan sample ---
            int polls = 2;
            while (!_poll.Plans.Last().SampleBacklog)
            {
                await AdvanceAsync(Options.MaxPollInterval);
                await _poll.PolledAsync(++polls);
            }

            Assert.True(_time.GetUtcNow() - _time.Start >= TenantPoller.BacklogEvery);
            Assert.False(_poll.Plans.Last().SampleOrphans);
        }

        [Fact]
        public async Task A_stop_lets_the_poll_in_flight_finish_and_polls_no_more()
        {
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _poll.Answer = async (plan, _) =>
            {
                await release.Task.ConfigureAwait(false);
                return FakeJobPoll.Result(plan, static _ => 0);
            };
            TenantPoller poller = Start();
            await _poll.PolledAsync(1);

            poller.Stop();
            Assert.False(poller.Completion.IsCompleted);
            Assert.True(_poll.Tokens.TryPeek(out CancellationToken token));
            Assert.False(token.IsCancellationRequested);

            release.SetResult();
            await poller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.Single(_poll.Plans);
        }

        [Fact]
        public async Task An_abort_cancels_the_poll_in_flight_without_logging_a_failure()
        {
            _poll.Answer = async (plan, _) =>
            {
                Assert.True(_poll.Tokens.TryPeek(out CancellationToken token));
                await Task.Delay(Timeout.InfiniteTimeSpan, token).ConfigureAwait(false);
                return FakeJobPoll.Result(plan, static _ => 0);
            };
            TenantPoller poller = Start();
            await _poll.PolledAsync(1);

            poller.Abort();
            await poller.Completion.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            Assert.DoesNotContain(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobPollFailed");
            Assert.Equal(2, _concurrency.Free);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach (TenantPoller poller in _pollers)
            {
                poller.Abort();
                poller.Completion.Wait(TimeSpan.FromSeconds(10));
                poller.Dispose();
            }

            _concurrency.Dispose();
            _poll.Dispose();
            _time.Dispose();
            _loggers.Dispose();
            _logs.Dispose();
            _harness.Dispose();
        }

        /// <summary>Starts a poller of the tenant.</summary>
        /// <param name="readOnly">Whether the tenant is read-only.</param>
        /// <returns>The poller.</returns>
        private TenantPoller Start(bool readOnly = false)
        {
            TenantPollerServices services = new(
                _poll,
                _concurrency,
                _harness.Services.GetRequiredService<JobHandlerRegistry>(),
                _dispatch,
                _wake,
                Options,
                _time,
                _loggers.CreateLogger(JobsLog.Category),
                "host-a/41");
            TenantPoller poller = new(TenantId, readOnly, services);
            _pollers.Add(poller);
            poller.Start();
            return poller;
        }

        /// <summary>A poll's plan, by its number from 1.</summary>
        /// <param name="number">The number.</param>
        /// <returns>The plan.</returns>
        private JobPollPlan Plan(int number)
        {
            return _poll.Plans.ElementAt(number - 1);
        }

        /// <summary>Advances the clock once the poller waits on its timer.</summary>
        /// <param name="by">How far.</param>
        /// <param name="awaitTimer">Whether to wait for the poller's timer first; false when the test already did.</param>
        /// <returns>A task that completes once the clock moved and the poller had a moment to react.</returns>
        private async Task AdvanceAsync(TimeSpan by, bool awaitTimer = true)
        {
            if (awaitTimer)
            {
                await _time.TimerCreatedAsync();
            }

            _time.Advance(by);
            await Task.Delay(TimeSpan.FromMilliseconds(50), TestContext.Current.CancellationToken);
        }
    }
}
