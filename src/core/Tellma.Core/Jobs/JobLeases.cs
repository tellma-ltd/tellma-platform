// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Jobs;
using Tellma.Core.Abstractions.TableTypes;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §5.2 and §6.6: the leases this instance holds, per tenant, which the renewal extends
    // and whose answers cancel the items: a row absent from a renewal loses the whole claim's lease,
    // a row whose cancel was requested cancels that item alone, and the host's stop cancels every
    // claim. A row leaves its lease once its completion was attempted, whatever the attempt's end, so
    // a renewal never extends a row nobody runs. Each lease records when it was last renewed, which
    // schedules the next renewal, and when every item it still tracks had its cancel requested, after
    // which a handler that ignores the cancels is abandoned: a handler that stopped one item and runs
    // on with the others is not ignoring anything. Both are read on the clock's monotonic timestamp,
    // so a step of the wall clock never delays a renewal.
    /// <summary>The claims this instance holds a lease on, per tenant.</summary>
    /// <param name="time">The clock the leases' renewals and cancels are timed by.</param>
    internal sealed class JobLeases(TimeProvider time)
    {
        /// <summary>Guards the per-tenant lists.</summary>
        private readonly Lock _gate = new();

        /// <summary>The claims in flight, per tenant.</summary>
        private readonly Dictionary<int, List<JobLease>> _tenants = [];

        /// <summary>Serialises the renewals of each tenant, so two never interleave their verdicts.</summary>
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _renewals = new();

        /// <summary>The clock the leases' renewals and cancels are timed by.</summary>
        public TimeProvider Time { get; } = time ?? throw new ArgumentNullException(nameof(time));

        /// <summary>Starts tracking a claim's rows that go to the handler.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The claim's handler.</param>
        /// <param name="token">The claim's fencing token.</param>
        /// <param name="rows">The rows, in claim order.</param>
        /// <param name="hostToken">The host's token: its cancellation stops every item of the claim.</param>
        /// <returns>The lease.</returns>
        public JobLease Track(int tenantId, JobHandlerRegistration handler, Guid token, IReadOnlyList<ClaimedJob> rows, CancellationToken hostToken)
        {
            ArgumentNullException.ThrowIfNull(handler);
            ArgumentNullException.ThrowIfNull(rows);
            JobLease lease = new(this, tenantId, handler, token, rows, Time.GetTimestamp());
            lock (_gate)
            {
                if (!_tenants.TryGetValue(tenantId, out List<JobLease>? leases))
                {
                    leases = [];
                    _tenants.Add(tenantId, leases);
                }

                leases.Add(lease);
            }

            // Registered once tracked: a host already stopping stops the claim at once.
            lease.WatchHost(hostToken);
            return lease;
        }

        /// <summary>The claims of a tenant in flight on this instance.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The claims.</returns>
        public IReadOnlyList<JobLease> InFlight(int tenantId)
        {
            lock (_gate)
            {
                return _tenants.TryGetValue(tenantId, out List<JobLease>? leases) ? [.. leases] : [];
            }
        }

        /// <summary>The tenants with claims in flight on this instance.</summary>
        /// <returns>The tenants' ids.</returns>
        public IReadOnlyList<int> Tenants()
        {
            lock (_gate)
            {
                return [.. _tenants.Keys];
            }
        }

        /// <summary>Every claim in flight on this instance.</summary>
        /// <returns>The claims.</returns>
        public IReadOnlyList<JobLease> All()
        {
            lock (_gate)
            {
                return [.. _tenants.Values.SelectMany(static leases => leases)];
            }
        }

        /// <summary>The gate that serialises a tenant's renewals.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The gate.</returns>
        public SemaphoreSlim RenewalGate(int tenantId)
        {
            return _renewals.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        }

        /// <summary>Stops tracking a claim whose every row left it.</summary>
        /// <param name="lease">The claim.</param>
        internal void Remove(JobLease lease)
        {
            lock (_gate)
            {
                if (_tenants.TryGetValue(lease.TenantId, out List<JobLease>? leases) && leases.Remove(lease) && leases.Count == 0)
                {
                    _ = _tenants.Remove(lease.TenantId);
                }
            }
        }
    }

    /// <summary>
    ///     One claim's lease: its token, its rows' slots, and the tokens the claim's handler runs
    ///     under — the batch token every item token links to, and the scope token the job scope's
    ///     round trips observe, cancelled only when the lease is lost or the host stops.
    /// </summary>
    internal sealed class JobLease : IDisposable
    {
        /// <summary>The registry that tracks the lease.</summary>
        private readonly JobLeases _owner;

        /// <summary>Guards the slots.</summary>
        private readonly Lock _gate = new();

        /// <summary>The batch token's source.</summary>
        private readonly CancellationTokenSource _batch = new();

        /// <summary>The scope token's source.</summary>
        private readonly CancellationTokenSource _scope = new();

        /// <summary>The rows still tracked, by job id.</summary>
        private readonly Dictionary<int, JobSlot> _slots;

        /// <summary>The registration on the host's token.</summary>
        private CancellationTokenRegistration _hostStop;

        /// <summary>1 once the lease was lost.</summary>
        private int _lost;

        /// <summary>1 once the host stopped.</summary>
        private int _stopping;

        /// <summary>1 once disposed.</summary>
        private int _disposed;

        /// <summary>1 once the loss was reported to the log and the instruments.</summary>
        private int _lossReported;

        /// <summary>1 once the lease was abandoned: renewals no longer carry it.</summary>
        private int _abandoned;

        /// <summary>The clock's timestamp when a renewal last extended the lease; the claim's until the first.</summary>
        private long _renewedAt;

        /// <summary>The clock's timestamp when every item still tracked had its cancel requested; null until then.</summary>
        private long? _cancelledAt;

        /// <summary>Whether a cancel was requested for one of the lease's items.</summary>
        private bool _cancelRequested;

        /// <summary>The clock's timestamp and the length of the final renewal that bound the lease to the host's stop; null until one did.</summary>
        private (long At, int Seconds)? _boundToStop;

        /// <summary>Creates the lease.</summary>
        /// <param name="owner">The registry that tracks it.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The claim's handler.</param>
        /// <param name="token">The claim's fencing token.</param>
        /// <param name="rows">The rows, in claim order.</param>
        /// <param name="claimedAt">The clock's timestamp when the claim took the lease.</param>
        internal JobLease(JobLeases owner, int tenantId, JobHandlerRegistration handler, Guid token, IReadOnlyList<ClaimedJob> rows, long claimedAt)
        {
            _owner = owner;
            TenantId = tenantId;
            Handler = handler;
            Token = token;
            _renewedAt = claimedAt;
            _slots = rows.ToDictionary(static row => row.Job.Id, row => new JobSlot(row, _batch.Token));
        }

        /// <summary>The tenant.</summary>
        public int TenantId { get; }

        /// <summary>The claim's handler.</summary>
        public JobHandlerRegistration Handler { get; }

        /// <summary>The claim's fencing token.</summary>
        public Guid Token { get; }

        /// <summary>The length a renewal extends the lease by, in seconds.</summary>
        public int LeaseSeconds => Handler.Options.LeaseSeconds;

        // Spec 0020 §6.7: the final renewal binds the lease to the host's shutdown timeout. A handler
        // still running may flush or jump after it, and its renewal must not undo the shortening, so
        // once bound every renewal extends the lease only to the end the final one set.
        /// <summary>
        ///     The length a renewal sent now extends the lease by, in seconds: its own, or, once the
        ///     final renewal bound it, what is left until the end that renewal set, at least a second.
        /// </summary>
        public int RenewalSeconds
        {
            get
            {
                lock (_gate)
                {
                    if (_boundToStop is not (long at, int seconds))
                    {
                        return LeaseSeconds;
                    }

                    double left = seconds - _owner.Time.GetElapsedTime(at).TotalSeconds;
                    return Math.Max(1, (int)Math.Ceiling(left));
                }
            }
        }

        /// <summary>The scope token: cancelled when the lease is lost or the host stops, never for a requested cancel.</summary>
        public CancellationToken ScopeToken => _scope.Token;

        /// <summary>Whether a renewal found the lease lost.</summary>
        public bool IsLost => Volatile.Read(ref _lost) == 1;

        /// <summary>Whether the host stopped while the lease was held.</summary>
        public bool IsStopping => Volatile.Read(ref _stopping) == 1;

        /// <summary>Whether the lease was abandoned: its handler ignored a cancel, or outlived the host's drain.</summary>
        public bool IsAbandoned => Volatile.Read(ref _abandoned) == 1;

        // Spec 0020 §5.2: the renewal interval is the lease's fifth; a longer lease renewed more often
        // with its tenant's shorter ones is harmless.
        /// <summary>The longest a renewal may wait after the last one: a fifth of the lease.</summary>
        public TimeSpan RenewalInterval => TimeSpan.FromSeconds(LeaseSeconds / 5.0);

        /// <summary>The time since a renewal last extended the lease, or since the claim until the first.</summary>
        public TimeSpan SinceRenewal
        {
            get
            {
                lock (_gate)
                {
                    return _owner.Time.GetElapsedTime(_renewedAt);
                }
            }
        }

        /// <summary>Whether the lease is due a renewal: a renewal interval passed since the last.</summary>
        public bool IsRenewalDue => SinceRenewal >= RenewalInterval;

        /// <summary>The time since every item the lease still tracks had its cancel requested; null until then.</summary>
        public TimeSpan? SinceCancelled
        {
            get
            {
                lock (_gate)
                {
                    return _cancelledAt is long cancelledAt ? _owner.Time.GetElapsedTime(cancelledAt) : null;
                }
            }
        }

        /// <summary>The rows still tracked.</summary>
        public IReadOnlyList<JobSlot> Slots
        {
            get
            {
                lock (_gate)
                {
                    return [.. _slots.Values];
                }
            }
        }

        /// <summary>A tracked row's slot.</summary>
        /// <param name="jobId">The job.</param>
        /// <returns>The slot; null when the row left the lease.</returns>
        public JobSlot? Slot(int jobId)
        {
            lock (_gate)
            {
                return _slots.GetValueOrDefault(jobId);
            }
        }

        /// <summary>Cancels one item whose cancel was requested; the batch token follows once every item's is cancelled.</summary>
        /// <param name="jobId">The job.</param>
        public void RequestCancel(int jobId)
        {
            if (Slot(jobId) is not JobSlot slot || slot.Token.IsCancellationRequested)
            {
                return;
            }

            lock (_gate)
            {
                _cancelRequested = true;
            }

            slot.Cancel(JobCancellationReason.CancelRequested);
            CancelBatchOnceEveryItemIsCancelled();
        }

        /// <summary>Loses the lease: every item's reason becomes the lost lease, then the scope and batch tokens are cancelled.</summary>
        public void Lose()
        {
            if (Interlocked.Exchange(ref _lost, 1) == 1)
            {
                return;
            }

            Stop(JobCancellationReason.LeaseLost);
        }

        /// <summary>Records that a renewal extended the lease.</summary>
        /// <param name="at">The clock's timestamp when the renewal was sent.</param>
        public void Renewed(long at)
        {
            lock (_gate)
            {
                _renewedAt = at > _renewedAt ? at : _renewedAt;
            }
        }

        /// <summary>Binds the lease to end where the host's final renewal ends it; a second binding changes nothing.</summary>
        /// <param name="at">The clock's timestamp when the final renewal was sent.</param>
        /// <param name="leaseSeconds">The length the final renewal sets, in seconds.</param>
        public void BindToStop(long at, int leaseSeconds)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(leaseSeconds, 1);
            lock (_gate)
            {
                _boundToStop ??= (at, leaseSeconds);
            }
        }

        /// <summary>Claims the one report of the lease's loss, so the log and the count name each lost lease once.</summary>
        /// <returns>True for the first caller only.</returns>
        public bool TryReportLoss()
        {
            return Interlocked.Exchange(ref _lossReported, 1) == 0;
        }

        /// <summary>Abandons the lease: no renewal carries it again, so it lapses and its rows are claimed again once it did.</summary>
        /// <returns>True for the first caller only.</returns>
        public bool Abandon()
        {
            return Interlocked.Exchange(ref _abandoned, 1) == 0;
        }

        /// <summary>Removes rows whose completion was attempted; once none remain, the lease leaves the registry and is disposed.</summary>
        /// <param name="jobIds">The rows.</param>
        public void Leave(IEnumerable<int> jobIds)
        {
            ArgumentNullException.ThrowIfNull(jobIds);
            bool empty;
            List<JobSlot> left = [];
            lock (_gate)
            {
                foreach (int jobId in jobIds)
                {
                    if (_slots.Remove(jobId, out JobSlot? slot))
                    {
                        left.Add(slot);
                    }
                }

                empty = _slots.Count == 0;
            }

            foreach (JobSlot slot in left)
            {
                slot.Dispose();
            }

            if (empty)
            {
                Dispose();
            }
            else
            {
                // The rows left may be the last whose cancel was not requested.
                CancelBatchOnceEveryItemIsCancelled();
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            _owner.Remove(this);
            _hostStop.Dispose();
            foreach (JobSlot slot in Slots)
            {
                slot.Dispose();
            }

            _batch.Dispose();
            _scope.Dispose();
        }

        /// <summary>Stops every item when the host stops.</summary>
        /// <param name="hostToken">The host's token.</param>
        internal void WatchHost(CancellationToken hostToken)
        {
            _hostStop = hostToken.UnsafeRegister(static state =>
            {
                var lease = (JobLease)state!;
                _ = Interlocked.Exchange(ref lease._stopping, 1);
                lease.Stop(JobCancellationReason.HostStopping);
            }, this);
        }

        /// <summary>Gives every item a reason, then cancels the scope and batch tokens.</summary>
        /// <param name="reason">The reason.</param>
        private void Stop(JobCancellationReason reason)
        {
            // The reasons first, so a handler woken by its token reads why.
            foreach (JobSlot slot in Slots)
            {
                slot.Cancel(reason);
            }

            Cancel(_scope);
            Cancel(_batch);
        }

        // Spec 0020 §5.2: a handler is abandoned when it ignores a requested cancel, so the clock starts
        // only once nothing is left for it to run: every item still tracked was cancelled, at least
        // one for a requested cancel (a lost lease or the host's stop has its own path).
        /// <summary>Cancels the batch token, and starts the abandonment clock, once every item still tracked is cancelled after a requested cancel.</summary>
        private void CancelBatchOnceEveryItemIsCancelled()
        {
            bool every;
            lock (_gate)
            {
                every = _cancelRequested && _slots.Count > 0 && _slots.Values.All(static each => each.Token.IsCancellationRequested);
                if (every)
                {
                    _cancelledAt ??= _owner.Time.GetTimestamp();
                }
            }

            if (every)
            {
                Cancel(_batch);
            }
        }

        /// <summary>Cancels a source the lease may have disposed already.</summary>
        /// <param name="source">The source.</param>
        private static void Cancel(CancellationTokenSource source)
        {
            try
            {
                source.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The lease ended: nothing runs under it any longer.
            }
        }
    }

    /// <summary>
    ///     One leased row: its item token, the reason the token was cancelled for until the item binds
    ///     it, whether the handler observed the item, and the progress buffered for the next renewal.
    /// </summary>
    internal sealed class JobSlot : IDisposable
    {
        /// <summary>The points a reported percent must move from the carried one to renew at once.</summary>
        public const int JumpPoints = 5;

        /// <summary>The least time between two renewals a jump asks for.</summary>
        public static readonly TimeSpan JumpInterval = TimeSpan.FromSeconds(5);

        /// <summary>Guards the reason and the buffer.</summary>
        private readonly Lock _gate = new();

        /// <summary>The item token's source, linked to the batch token.</summary>
        private readonly CancellationTokenSource _item;

        /// <summary>The reason given before the item bound its own; none once bound.</summary>
        private JobCancellationReason _pending;

        /// <summary>Sets the item's reason; null until the item is created.</summary>
        private Func<JobCancellationReason, bool>? _reason;

        /// <summary>The buffered percent; null when unchanged.</summary>
        private byte? _percent;

        /// <summary>The buffered message key; null when unchanged.</summary>
        private string? _key;

        /// <summary>The buffered message arguments, which go with the key.</summary>
        private string? _argumentsJson;

        /// <summary>The buffered checkpoint; null when unchanged.</summary>
        private string? _stateJson;

        /// <summary>Counts the buffer's changes, so a carried snapshot clears only what it carried.</summary>
        private long _version;

        /// <summary>Counts the checkpoints reported, so a committed append promotes its own only when none followed.</summary>
        private long _stateVersion;

        /// <summary>Whether the handler reported progress, a checkpoint or a flush for the item.</summary>
        private volatile bool _observed;

        /// <summary>Whether the row's completion is being sent, so a renewal no longer judges the row.</summary>
        private volatile bool _completing;

        /// <summary>The percent the database last held for the row: the claimed one, then each one a renewal carried.</summary>
        private byte _carriedPercent;

        /// <summary>The clock's timestamp when a renewal last carried the row's progress, or a jump last asked for one; null until either.</summary>
        private long? _progressRenewedAt;

        /// <summary>Creates the slot.</summary>
        /// <param name="row">The claimed row.</param>
        /// <param name="batchToken">The claim's batch token.</param>
        public JobSlot(ClaimedJob row, CancellationToken batchToken)
        {
            ArgumentNullException.ThrowIfNull(row);
            Row = row;
            _carriedPercent = row.Job.ProgressPercent ?? 0;
            _item = CancellationTokenSource.CreateLinkedTokenSource(batchToken);
            Token = _item.Token;
        }

        /// <summary>The claimed row.</summary>
        public ClaimedJob Row { get; }

        /// <summary>The job's id.</summary>
        public int JobId => Row.Job.Id;

        /// <summary>The user the job's events go to; null when none.</summary>
        public int? RequestedById => Row.Job.RequestedById;

        /// <summary>The item token, captured so it stays readable once the row left its lease.</summary>
        public CancellationToken Token { get; }

        /// <summary>Whether the handler reported progress, a checkpoint or a flush for the item.</summary>
        public bool Observed => _observed;

        /// <summary>Whether the row's completion is being sent.</summary>
        public bool Completing => _completing;

        /// <summary>The checkpoints reported so far.</summary>
        public long StateVersion => Interlocked.Read(ref _stateVersion);

        /// <summary>The buffer's changes so far.</summary>
        public long BufferVersion
        {
            get
            {
                lock (_gate)
                {
                    return _version;
                }
            }
        }

        /// <summary>Records that the handler observed the item.</summary>
        public void MarkObserved()
        {
            _observed = true;
        }

        /// <summary>Records that the row's completion is being sent.</summary>
        public void BeginCompleting()
        {
            _completing = true;
        }

        /// <summary>Binds the item's reason, giving it any reason the slot was cancelled for before.</summary>
        /// <param name="reason">Sets the item's reason.</param>
        public void Bind(Func<JobCancellationReason, bool> reason)
        {
            ArgumentNullException.ThrowIfNull(reason);
            lock (_gate)
            {
                _reason = reason;
                if (_pending != JobCancellationReason.None)
                {
                    _ = reason(_pending);
                }
            }
        }

        /// <summary>Gives the item a reason, then cancels its token.</summary>
        /// <param name="reason">The reason.</param>
        public void Cancel(JobCancellationReason reason)
        {
            lock (_gate)
            {
                if (_reason is not null)
                {
                    _ = _reason(reason);
                }
                else if (Replaces(_pending, reason))
                {
                    _pending = reason;
                }
            }

            try
            {
                _item.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The row left its lease: nothing runs under its token any longer.
            }
        }

        /// <summary>Merges reported progress into the buffer: a value given replaces the buffered one, a null keeps it.</summary>
        /// <param name="percent">The percent; null when unchanged.</param>
        /// <param name="key">The message key; null when unchanged.</param>
        /// <param name="argumentsJson">The message arguments, which go with the key.</param>
        /// <param name="stateJson">The checkpoint; null when unchanged.</param>
        public void Buffer(byte? percent, string? key, string? argumentsJson, string? stateJson)
        {
            lock (_gate)
            {
                _percent = percent ?? _percent;
                if (key is not null)
                {
                    _key = key;
                    _argumentsJson = argumentsJson;
                }

                if (stateJson is not null)
                {
                    _stateJson = stateJson;
                    _ = Interlocked.Increment(ref _stateVersion);
                }

                _version++;
            }
        }

        /// <summary>The buffered row and the version it was taken at; null when nothing is buffered.</summary>
        /// <returns>The row and its version, or null.</returns>
        public (JobProgressList Row, long Version)? Buffered()
        {
            lock (_gate)
            {
                return _percent is null && _key is null && _stateJson is null
                    ? null
                    : (new JobProgressList
                    {
                        Id = JobId,
                        ProgressPercent = _percent,
                        ProgressKey = _key,
                        ProgressArgumentsJson = _argumentsJson,
                        StateJson = _stateJson,
                    }, _version);
            }
        }

        /// <summary>Clears the buffer a renewal carried, unless progress was reported since it was taken, and records what it carried.</summary>
        /// <param name="version">The version the carried row was taken at.</param>
        /// <param name="percent">The percent the carried row wrote; null when it kept the stored one.</param>
        /// <param name="at">The clock's timestamp when the renewal was sent.</param>
        public void Carried(long version, byte? percent, long at)
        {
            lock (_gate)
            {
                _carriedPercent = percent ?? _carriedPercent;
                _progressRenewedAt = at;
            }

            Clear(version, percent: true, message: true, state: true);
        }

        // Spec 0020 §5.2: a report whose percent is at least 5 points from the last carried percent
        // renews at once, but at most once per job per 5 s, counted from the last progress-carrying
        // renewal or the last jump that asked for one, so a burst of reports asks once.
        /// <summary>Whether a reported percent is a jump that renews at once; when it is, the jump is recorded.</summary>
        /// <param name="percent">The percent reported.</param>
        /// <param name="time">The clock, read on its monotonic timestamp.</param>
        /// <returns>True when the report should renew now.</returns>
        public bool TryJump(byte percent, TimeProvider time)
        {
            ArgumentNullException.ThrowIfNull(time);
            long now = time.GetTimestamp();
            lock (_gate)
            {
                if (Math.Abs(percent - _carriedPercent) < JumpPoints || (_progressRenewedAt is long last && time.GetElapsedTime(last, now) < JumpInterval))
                {
                    return false;
                }

                _progressRenewedAt = now;
                return true;
            }
        }

        /// <summary>Clears what a committed append wrote, unless progress was reported since it was appended.</summary>
        /// <param name="version">The buffer's version when the append was composed.</param>
        /// <param name="percent">Whether the append wrote a percent.</param>
        /// <param name="message">Whether the append wrote a message.</param>
        /// <param name="state">Whether the append wrote a checkpoint.</param>
        public void Clear(long version, bool percent, bool message, bool state)
        {
            lock (_gate)
            {
                if (_version != version)
                {
                    return;
                }

                _percent = percent ? null : _percent;
                (_key, _argumentsJson) = message ? (null, null) : (_key, _argumentsJson);
                _stateJson = state ? null : _stateJson;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _item.Dispose();
        }

        /// <summary>Whether a reason replaces the one given before: any replaces none, and a lost lease replaces any other.</summary>
        /// <param name="current">The reason given before.</param>
        /// <param name="reason">The new reason.</param>
        /// <returns>True when the new reason stands.</returns>
        private static bool Replaces(JobCancellationReason current, JobCancellationReason reason)
        {
            return current == JobCancellationReason.None
                || (reason == JobCancellationReason.LeaseLost && current != JobCancellationReason.LeaseLost);
        }
    }
}
