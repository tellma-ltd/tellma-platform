// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using System.Threading.Channels;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.2–§6.5: one poller per served tenant. It reserves a slot per key it may claim (on
    // a read-only tenant only the keys whose handler runs then), polls, starts each claim that leased
    // rows in a free instance slot, has the dispatcher release uncounted a claim that found none, and
    // frees the slots of the claims that leased nothing. It then waits on whichever fires first of a
    // PeriodicTimer over the adaptive interval and a coalescing wake-up channel (capacity 1,
    // DropWrite). A full claim or full tick polls again at once; something short of that resets the
    // interval to the minimum; nothing doubles it up to the maximum; a nudge resets it to the minimum
    // and wakes the channel; a failed poll is logged and waits the maximum (§6.1). The backlog sample
    // rides one poll a minute and the orphan sample one an hour, from the first poll on. A stop takes
    // effect after the current poll; only the worker's abort cancels a poll in flight.
    //
    // Pollers the worker starts together would otherwise poll in lock-step for as long as they idle,
    // the same intervals from the same instant: the first poll waits a random part of the shortest
    // interval, and every wait is lengthened by a random part of a fifth of itself. A nudge cuts
    // either short.
    /// <summary>Polls one tenant's jobs on this instance.</summary>
    internal sealed class TenantPoller : IDisposable
    {
        /// <summary>How often a poll carries the backlog sample.</summary>
        internal static readonly TimeSpan BacklogEvery = TimeSpan.FromMinutes(1);

        /// <summary>How often a poll carries the orphan sample and the retired-key cancel.</summary>
        internal static readonly TimeSpan OrphansEvery = TimeSpan.FromHours(1);

        /// <summary>The most a wait is lengthened by, as a part of itself.</summary>
        internal const double JitterFraction = 0.2;

        /// <summary>Runs each poll's round trip.</summary>
        private readonly IJobPoll _poll;

        /// <summary>The instance's batch slots.</summary>
        private readonly JobConcurrency _concurrency;

        /// <summary>The handlers this instance runs.</summary>
        private readonly JobHandlerRegistry _registry;

        /// <summary>Starts the batches of the claims that leased rows.</summary>
        private readonly IJobDispatch _dispatch;

        /// <summary>The seam an enqueue on this instance nudges the poller through.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>The job options: the poll intervals.</summary>
        private readonly JobsOptions _options;

        /// <summary>The clock the waits and the sample cadences run on.</summary>
        private readonly TimeProvider _time;

        /// <summary>Draws the random part of the first poll's delay and of each wait's lengthening.</summary>
        private readonly Func<double> _jitter;

        /// <summary>The job machinery's logger.</summary>
        private readonly ILogger _logger;

        /// <summary>The lease owner the claims and the stamp name.</summary>
        private readonly string _owner;

        /// <summary>The coalescing wake-up channel: one pending wake at most.</summary>
        private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

        /// <summary>Cancelled to stop polling after the current poll.</summary>
        private readonly CancellationTokenSource _stop = new();

        /// <summary>Cancelled to abort the poll in flight.</summary>
        private readonly CancellationTokenSource _abort = new();

        /// <summary>Guards the start.</summary>
        private readonly Lock _gate = new();

        /// <summary>The wait on the channel, kept across waits so at most one is outstanding.</summary>
        private Task<bool>? _signalled;

        /// <summary>The polling loop; null until started.</summary>
        private Task? _run;

        /// <summary>Whether the tenant is read-only.</summary>
        private volatile bool _readOnly;

        /// <summary>When the next poll carries the backlog sample.</summary>
        private DateTimeOffset _nextBacklogAt = DateTimeOffset.MinValue;

        /// <summary>When the next poll carries the orphan sample.</summary>
        private DateTimeOffset _nextOrphansAt = DateTimeOffset.MinValue;

        /// <summary>Creates the poller; it polls once started.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="readOnly">Whether the tenant is read-only.</param>
        /// <param name="services">What the poller polls with.</param>
        public TenantPoller(int tenantId, bool readOnly, TenantPollerServices services)
        {
            ArgumentNullException.ThrowIfNull(services);
            TenantId = tenantId;
            _readOnly = readOnly;
            _poll = services.Poll;
            _concurrency = services.Concurrency;
            _registry = services.Registry;
            _dispatch = services.Dispatch;
            _wake = services.Wake;
            _options = services.Options;
            _time = services.Time;
            _jitter = services.Jitter;
            _logger = services.Logger;
            _owner = services.Owner;
        }

        /// <summary>The tenant.</summary>
        public int TenantId { get; }

        /// <summary>Whether the tenant is read-only, so the poll claims only the keys whose handler runs then.</summary>
        public bool ReadOnly
        {
            get => _readOnly;
            set => _readOnly = value;
        }

        /// <summary>The polling loop: completes once the poller stopped.</summary>
        public Task Completion
        {
            get
            {
                lock (_gate)
                {
                    return _run ?? Task.CompletedTask;
                }
            }
        }

        // Spec 0020 §6.4: the interval after a poll, from what the poll found; null for a failed poll.
        /// <summary>The wait before the next poll.</summary>
        /// <param name="current">The wait before the poll that just ended.</param>
        /// <param name="finding">What the poll found; null when it failed.</param>
        /// <param name="min">The shortest wait.</param>
        /// <param name="max">The longest wait.</param>
        /// <returns>The wait: zero to poll again at once.</returns>
        internal static TimeSpan Next(TimeSpan current, JobPollFinding? finding, TimeSpan min, TimeSpan max)
        {
            return finding switch
            {
                null => max,
                JobPollFinding.Full => TimeSpan.Zero,
                JobPollFinding.Some => min,
                JobPollFinding.Nothing => current < min ? min : current >= max / 2 ? max : current * 2,
                _ => throw new ArgumentOutOfRangeException(nameof(finding), finding, "The finding is not defined."),
            };
        }

        /// <summary>A wait lengthened by its jitter.</summary>
        /// <param name="wait">The wait.</param>
        /// <param name="sample">The random part, from zero up to one.</param>
        /// <returns>The wait, lengthened by up to <see cref="JitterFraction" /> of itself.</returns>
        internal static TimeSpan Jittered(TimeSpan wait, double sample)
        {
            return wait + (wait * (JitterFraction * Math.Clamp(sample, 0, 1)));
        }

        /// <summary>Starts polling; starting twice does nothing.</summary>
        public void Start()
        {
            lock (_gate)
            {
                _run ??= Task.Run(RunAsync);
            }
        }

        /// <summary>Stops polling once the current poll ended.</summary>
        public void Stop()
        {
            _stop.Cancel();
        }

        /// <summary>Stops polling and cancels the poll in flight.</summary>
        public void Abort()
        {
            _stop.Cancel();
            _abort.Cancel();
        }

        /// <summary>Resets the interval to the minimum and wakes the poller, once however many nudges arrive before it wakes.</summary>
        public void Nudge()
        {
            _ = _signal.Writer.TryWrite(true);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _stop.Dispose();
            _abort.Dispose();
        }

        /// <summary>The polling loop.</summary>
        /// <returns>A task that completes once the poller stopped.</returns>
        private async Task RunAsync()
        {
            using IDisposable subscription = _wake.Subscribe(TenantId, Nudge);

            // --- The first poll waits a random part of the shortest interval, so pollers started
            // together spread out; a timer runs on whole milliseconds, so a shorter part polls at once ---
            TimeSpan stagger = _options.MinPollInterval * Math.Clamp(_jitter(), 0, 1);
            if (stagger >= TimeSpan.FromMilliseconds(1))
            {
                _ = await WaitAsync(stagger).ConfigureAwait(false);
            }

            TimeSpan interval = _options.MinPollInterval;
            while (!_stop.IsCancellationRequested)
            {
                interval = Next(interval, await PollOnceAsync().ConfigureAwait(false), _options.MinPollInterval, _options.MaxPollInterval);
                if (interval > TimeSpan.Zero && await WaitAsync(Jittered(interval, _jitter())).ConfigureAwait(false))
                {
                    // A nudge woke the poller: the poll that follows starts from the minimum.
                    interval = _options.MinPollInterval;
                }
            }
        }

        /// <summary>Runs one poll and starts the claims it leased; a failure is logged.</summary>
        /// <returns>What the poll found; null when it failed.</returns>
        private async Task<JobPollFinding?> PollOnceAsync()
        {
            // --- The plan: a slot per key the poll may claim, and the samples due ---
            DateTimeOffset now = _time.GetUtcNow();
            bool backlog = now >= _nextBacklogAt;
            bool orphans = now >= _nextOrphansAt;
            bool readOnly = _readOnly;
            List<JobPollClaimRequest> claims = [];
            foreach (JobHandlerRegistration handler in _registry.Handlers)
            {
                if ((!readOnly || handler.Options.RunsWhenReadOnly) && _concurrency.TryReserve(TenantId, handler) is JobSlotReservation reservation)
                {
                    claims.Add(new JobPollClaimRequest(handler, reservation));
                }
            }

            JobPollPlan plan = new(TenantId, readOnly, _stop.IsCancellationRequested, claims, backlog, orphans, _owner);

            // --- The poll: once its round trip returned, the claims start and the samples are taken ---
            bool executed = false;
            try
            {
                JobPollResult result = await _poll.PollAsync(
                    plan,
                    result =>
                    {
                        executed = true;
                        _nextBacklogAt = backlog ? now + BacklogEvery : _nextBacklogAt;
                        _nextOrphansAt = orphans ? now + OrphansEvery : _nextOrphansAt;
                        Dispatch(result);
                    },
                    _abort.Token).ConfigureAwait(false);
                return result.Finding;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // A round trip that never returned leased nothing this poller tracks: the slots are freed.
                if (!executed)
                {
                    foreach (JobPollClaimRequest claim in claims)
                    {
                        claim.Reservation?.Dispose();
                    }
                }

                if (!(_abort.IsCancellationRequested && failure is OperationCanceledException))
                {
                    using IDisposable? scope = _logger.BeginScope(new JobLogScope(TenantId, userId: null, [], originTraceParent: null));
                    JobsLog.JobPollFailed(_logger, failure, _options.MaxPollInterval);
                }

                return null;
            }
        }

        /// <summary>
        ///     Starts the claims that leased rows, each in a free instance slot, has the dispatcher
        ///     release those that found none, and frees the slots of those that leased nothing.
        /// </summary>
        /// <param name="result">What the round trip returned.</param>
        private void Dispatch(JobPollResult result)
        {
            IReadOnlyList<JobPolledClaim> claims = result.Claims;
            int next = 0;
            try
            {
                for (; next < claims.Count; next++)
                {
                    JobPolledClaim polled = claims[next];
                    if (polled.Claim.Rows.Value.Count == 0)
                    {
                        polled.Reservation?.Dispose();
                    }
                    else if (polled.Reservation?.TryStart() ?? true)
                    {
                        _dispatch.Start(polled);
                    }
                    else
                    {
                        // No instance slot is left: the rows go back uncounted, and the tenant waits for a freed slot.
                        _dispatch.Release(polled);
                    }
                }
            }
            finally
            {
                // A throw part-way leaves no slot reserved for a claim the dispatcher never took, so no
                // key stays at its limit; the claim's rows wait for their lease to lapse.
                for (; next < claims.Count; next++)
                {
                    claims[next].Reservation?.Dispose();
                }
            }
        }

        /// <summary>Waits for the interval, a nudge or the stop, whichever comes first.</summary>
        /// <param name="interval">The interval.</param>
        /// <returns>True when a nudge woke the poller.</returns>
        private async Task<bool> WaitAsync(TimeSpan interval)
        {
            using PeriodicTimer timer = new(interval, _time);
            Task<bool> tick = timer.WaitForNextTickAsync(_stop.Token).AsTask();
            _signalled ??= _signal.Reader.WaitToReadAsync(_stop.Token).AsTask();
            Task first = await Task.WhenAny(tick, _signalled).ConfigureAwait(false);
            if (first != _signalled || !_signalled.IsCompletedSuccessfully)
            {
                return false;
            }

            // The wake is consumed; a nudge after this point wakes the next wait.
            _signalled = null;
            _ = _signal.Reader.TryRead(out _);
            return true;
        }
    }

    /// <summary>Starts the batches of the claims a poll leased; the poller's seam over <see cref="JobDispatcher" />.</summary>
    internal interface IJobDispatch
    {
        /// <summary>Starts one claim's batch on the thread pool; its slot is released once it ended. It throws only before the batch started.</summary>
        /// <param name="polled">The claim, with rows leased, and its slot, started.</param>
        void Start(JobPolledClaim polled);

        /// <summary>Releases one claim's rows uncounted on the thread pool, the instance having no slot to run them in; its slot is released once they were. It throws only before the release started.</summary>
        /// <param name="polled">The claim, with rows leased, and its slot, not started.</param>
        void Release(JobPolledClaim polled);
    }

    /// <summary>What a tenant poller polls with.</summary>
    /// <param name="Poll">Runs each poll's round trip.</param>
    /// <param name="Concurrency">The instance's batch slots.</param>
    /// <param name="Registry">The handlers this instance runs.</param>
    /// <param name="Dispatch">Starts the batches of the claims that leased rows.</param>
    /// <param name="Wake">The seam an enqueue on this instance nudges the poller through.</param>
    /// <param name="Options">The job options.</param>
    /// <param name="Time">The clock.</param>
    /// <param name="Logger">The job machinery's logger.</param>
    /// <param name="Owner">The lease owner the claims and the stamp name.</param>
    /// <param name="Jitter">Draws a random part, from zero up to one, of the first poll's delay and of each wait's lengthening.</param>
    internal sealed record TenantPollerServices(
        IJobPoll Poll,
        JobConcurrency Concurrency,
        JobHandlerRegistry Registry,
        IJobDispatch Dispatch,
        TenantPollerWake Wake,
        JobsOptions Options,
        TimeProvider Time,
        ILogger Logger,
        string Owner,
        Func<double> Jitter);
}
