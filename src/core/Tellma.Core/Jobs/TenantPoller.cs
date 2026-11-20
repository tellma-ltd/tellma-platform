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
    // a read-only tenant only the keys whose handler runs then), polls, hands the claims that leased
    // rows to the dispatcher and frees the others' slots, then waits on whichever fires first of a
    // PeriodicTimer over the adaptive interval and a coalescing wake-up channel (capacity 1,
    // DropWrite). A full claim or full tick polls again at once; something short of that resets the
    // interval to the minimum; nothing doubles it up to the maximum; a nudge resets it to the minimum
    // and wakes the channel; a failed poll is logged and waits the maximum (§6.1). The backlog sample
    // rides one poll a minute and the orphan sample one an hour, from the first poll on. A stop takes
    // effect after the current poll; only the worker's abort cancels a poll in flight.
    /// <summary>Polls one tenant's jobs on this instance.</summary>
    internal sealed class TenantPoller : IDisposable
    {
        /// <summary>How often a poll carries the backlog sample.</summary>
        internal static readonly TimeSpan BacklogEvery = TimeSpan.FromMinutes(1);

        /// <summary>How often a poll carries the orphan sample and the retired-key cancel.</summary>
        internal static readonly TimeSpan OrphansEvery = TimeSpan.FromHours(1);

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
            TimeSpan interval = _options.MinPollInterval;
            while (!_stop.IsCancellationRequested)
            {
                interval = Next(interval, await PollOnceAsync().ConfigureAwait(false), _options.MinPollInterval, _options.MaxPollInterval);
                if (interval > TimeSpan.Zero && await WaitAsync(interval).ConfigureAwait(false))
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

        /// <summary>Starts the claims that leased rows and frees the slots of those that leased none.</summary>
        /// <param name="result">What the round trip returned.</param>
        private void Dispatch(JobPollResult result)
        {
            foreach (JobPolledClaim polled in result.Claims)
            {
                if (polled.Claim.Rows.Value.Count > 0)
                {
                    _dispatch.Start(polled);
                }
                else
                {
                    polled.Reservation?.Dispose();
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
        /// <summary>Starts one claim's batch on the thread pool; its slot is released once it ended.</summary>
        /// <param name="polled">The claim, with rows leased, and its slot.</param>
        void Start(JobPolledClaim polled);
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
    internal sealed record TenantPollerServices(
        IJobPoll Poll,
        JobConcurrency Concurrency,
        JobHandlerRegistry Registry,
        IJobDispatch Dispatch,
        TenantPollerWake Wake,
        JobsOptions Options,
        TimeProvider Time,
        ILogger Logger,
        string Owner);
}
