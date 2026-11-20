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
    ///     poll that failed, a key at its limit that neither wakes its tenant nor stops it backing off,
    ///     two tenants claiming at once, pollers started together never running past the instance's
    ///     bound, the first poll's stagger and each wait's jitter, the sample cadences, and a stop that
    ///     lets the poll in flight finish.
    /// </summary>
    public sealed class TenantPollerTests : IDisposable
    {
        /// <summary>The tenant polled.</summary>
        private const int TenantId = 4;

        /// <summary>A second tenant, polled alongside the first.</summary>
        private const int OtherTenantId = 5;

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

            // --- The audit claim that leased nothing woke nobody: the poller waits on its timer ---
            await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
            Assert.Equal(2, _poll.Plans.Count);

            // --- The batch ends: its slot is free, and the tenant whose key waited for it is woken at once ---
            Assert.True(_dispatch.Started.TryPeek(out JobPolledClaim? running));
            running.Reservation!.Dispose();
            await _poll.PolledAsync(3);
            Assert.Equal([WorkerTestModel.AuditKey, WorkerTestModel.RecomputeKey], Plan(3).Claims.Select(static claim => claim.Handler.Key));
            Assert.Equal(2, _concurrency.Free);
        }

        [Fact]
        public async Task A_tenant_whose_key_is_at_its_limit_backs_off_while_that_keys_batch_runs()
        {
            // --- Poll 1 fills the recompute key's batch of one; every later poll finds nothing ---
            _poll.Answer = (plan, number) => Task.FromResult(FakeJobPoll.Result(plan, handler => number == 1 && handler.Key == WorkerTestModel.RecomputeKey ? 1 : 0));
            Start();
            await _poll.PolledAsync(2);

            // --- One second, two, then four: the busy key's refusal and the idle key's empty claims wake nothing ---
            foreach ((int number, double seconds) in new[] { (3, 1.0), (4, 2.0), (5, 4.0) })
            {
                await AdvanceAsync(TimeSpan.FromSeconds(seconds - 0.1));
                Assert.Equal(number - 1, _poll.Plans.Count);
                _time.Advance(TimeSpan.FromSeconds(0.1));
                await _poll.PolledAsync(number);
                Assert.Equal([WorkerTestModel.AuditKey], Plan(number).Claims.Select(static claim => claim.Handler.Key));
            }

            // --- Only the batch's end wakes the tenant ---
            await _time.TimerCreatedAsync();
            Assert.True(_dispatch.Started.TryPeek(out JobPolledClaim? running));
            running.Reservation!.Dispose();
            await _poll.PolledAsync(6);
            Assert.Equal([WorkerTestModel.AuditKey, WorkerTestModel.RecomputeKey], Plan(6).Claims.Select(static claim => claim.Handler.Key));
        }

        [Fact]
        public async Task Two_tenants_polling_at_once_each_claim_a_due_key()
        {
            // --- The first poll waits for the second, so both plan before either round trip returns ---
            TaskCompletionSource both = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _poll.Answer = async (plan, number) =>
            {
                if (number == 2)
                {
                    both.TrySetResult();
                }

                if (number <= 2)
                {
                    await both.Task.ConfigureAwait(false);
                }

                return FakeJobPoll.Result(plan, handler => number <= 2 && handler.Key == WorkerTestModel.AuditKey ? 1 : 0);
            };
            _ = Start(tenantId: TenantId);
            _ = Start(tenantId: OtherTenantId);
            await _poll.PolledAsync(2);

            // --- Neither poll held the instance's slots for its empty keys: both planned every key ---
            Assert.Equal([TenantId, OtherTenantId], new[] { Plan(1).TenantId, Plan(2).TenantId }.Order());
            Assert.All([Plan(1), Plan(2)], static plan => Assert.Equal([WorkerTestModel.AuditKey, WorkerTestModel.RecomputeKey], plan.Claims.Select(static claim => claim.Handler.Key)));

            // --- Both tenants' audit claims started, one instance slot each ---
            await WaitUntilAsync(() => _dispatch.Started.Count == 2);
            Assert.Equal([TenantId, OtherTenantId], _dispatch.Started.Select(static polled => polled.Claim.TenantId).Order());
            Assert.Equal(2, _concurrency.Started);
            Assert.Equal(0, _concurrency.Free);
        }

        [Fact]
        public async Task Pollers_started_together_with_work_on_every_key_never_run_more_batches_than_the_instance_allows()
        {
            // --- Six tenants plan at once, before any round trip returns, and every claim leases a row ---
            const int Tenants = 6;
            TaskCompletionSource all = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _poll.Answer = async (plan, number) =>
            {
                if (number == Tenants)
                {
                    all.TrySetResult();
                }

                if (number <= Tenants)
                {
                    await all.Task.ConfigureAwait(false);
                }

                return FakeJobPoll.Result(plan, _ => number <= Tenants ? 1 : 0);
            };
            for (int tenant = 1; tenant <= Tenants; tenant++)
            {
                _ = Start(tenantId: tenant);
            }

            await _poll.PolledAsync(Tenants);
            Assert.All(_poll.Plans, static plan => Assert.Equal(2, plan.Claims.Count));

            // --- Two batches start, the instance's bound; the other ten claims are released, freeing their keys ---
            await WaitUntilAsync(() => _dispatch.Started.Count + _dispatch.Released.Count == Tenants * 2);
            Assert.Equal(2, _dispatch.Started.Count);
            Assert.Equal((2, 0), (_concurrency.Started, _concurrency.Free));
            foreach (JobPolledClaim polled in _dispatch.Released)
            {
                (int tenant, string key) = (polled.Claim.TenantId, polled.Claim.Handler.Key);
                Assert.Equal(_dispatch.Started.Count(started => started.Claim.TenantId == tenant && started.Claim.Handler.Key == key), _concurrency.Running(tenant, key));
            }

            // --- A batch ending frees one slot and wakes one tenant whose claim was released ---
            Assert.True(_dispatch.Started.TryPeek(out JobPolledClaim? running));
            running.Reservation!.Dispose();
            await _poll.PolledAsync(Tenants + 1);
            Assert.Contains(Plan(Tenants + 1).TenantId, _dispatch.Released.Select(static polled => polled.Claim.TenantId));
            Assert.True(_concurrency.Started <= _concurrency.MaxParallelBatches);
        }

        [Fact]
        public async Task A_dispatch_that_throws_part_way_frees_the_slot_of_every_claim_it_did_not_take()
        {
            // --- Both keys lease a row; the dispatcher throws at the first ---
            _dispatch.StartFailure = new InvalidOperationException("The dispatcher broke.");
            _poll.Answer = (plan, number) => Task.FromResult(FakeJobPoll.Result(plan, _ => number == 1 ? 1 : 0));
            Start();
            await _poll.PolledAsync(1);
            await _time.TimerCreatedAsync();

            // --- The poll is logged as failed, and neither key nor the instance keeps a slot ---
            Assert.Single(_logs.Collector.GetSnapshot(), static record => record.Id.Name == "JobPollFailed");
            Assert.Equal(
                (0, 0, 0, 2),
                (_concurrency.Running(TenantId, WorkerTestModel.AuditKey), _concurrency.Running(TenantId, WorkerTestModel.RecomputeKey), _concurrency.Started, _concurrency.Free));
        }

        [Fact]
        public async Task The_first_poll_waits_its_stagger_and_each_wait_is_lengthened_by_its_jitter()
        {
            // --- Half the shortest interval before the first poll ---
            Start(jitter: static () => 0.5);
            await AdvanceAsync(TimeSpan.FromSeconds(0.4));
            Assert.Empty(_poll.Plans);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(1);

            // --- The two seconds that follow nothing, lengthened by a tenth: 2.2 seconds ---
            await AdvanceAsync(TimeSpan.FromSeconds(2.1));
            Assert.Single(_poll.Plans);
            _time.Advance(TimeSpan.FromSeconds(0.1));
            await _poll.PolledAsync(2);
        }

        [Theory]
        [InlineData(30, 0, 30)]
        [InlineData(30, 0.5, 33)]
        [InlineData(30, 1, 36)]
        [InlineData(10, 2, 12)]
        public void A_wait_is_lengthened_by_at_most_a_fifth_of_itself(int seconds, double sample, int jittered)
        {
            Assert.Equal(TimeSpan.FromSeconds(jittered), TenantPoller.Jittered(TimeSpan.FromSeconds(seconds), sample));
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

            _poll.Dispose();
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

        /// <summary>Starts a poller of a tenant.</summary>
        /// <param name="readOnly">Whether the tenant is read-only.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="jitter">Draws the stagger's and the jitter's random part; zero by default, so the first poll comes at once and each wait is its interval.</param>
        /// <returns>The poller.</returns>
        private TenantPoller Start(bool readOnly = false, int tenantId = TenantId, Func<double>? jitter = null)
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
                "host-a/41",
                jitter ?? (static () => 0));
            TenantPoller poller = new(tenantId, readOnly, services);
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
