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
    // a renewal never extends a row nobody runs.
    /// <summary>The claims this instance holds a lease on, per tenant.</summary>
    internal sealed class JobLeases
    {
        /// <summary>Guards the per-tenant lists.</summary>
        private readonly Lock _gate = new();

        /// <summary>The claims in flight, per tenant.</summary>
        private readonly Dictionary<int, List<JobLease>> _tenants = [];

        /// <summary>Serialises the renewals of each tenant, so two never interleave their verdicts.</summary>
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _renewals = new();

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
            JobLease lease = new(this, tenantId, handler, token, rows);
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

        /// <summary>Creates the lease.</summary>
        /// <param name="owner">The registry that tracks it.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The claim's handler.</param>
        /// <param name="token">The claim's fencing token.</param>
        /// <param name="rows">The rows, in claim order.</param>
        internal JobLease(JobLeases owner, int tenantId, JobHandlerRegistration handler, Guid token, IReadOnlyList<ClaimedJob> rows)
        {
            _owner = owner;
            TenantId = tenantId;
            Handler = handler;
            Token = token;
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

        /// <summary>The scope token: cancelled when the lease is lost or the host stops, never for a requested cancel.</summary>
        public CancellationToken ScopeToken => _scope.Token;

        /// <summary>Whether a renewal found the lease lost.</summary>
        public bool IsLost => Volatile.Read(ref _lost) == 1;

        /// <summary>Whether the host stopped while the lease was held.</summary>
        public bool IsStopping => Volatile.Read(ref _stopping) == 1;

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

            slot.Cancel(JobCancellationReason.CancelRequested);
            if (Slots.All(static each => each.Token.IsCancellationRequested))
            {
                Cancel(_batch);
            }
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

        /// <summary>Creates the slot.</summary>
        /// <param name="row">The claimed row.</param>
        /// <param name="batchToken">The claim's batch token.</param>
        public JobSlot(ClaimedJob row, CancellationToken batchToken)
        {
            ArgumentNullException.ThrowIfNull(row);
            Row = row;
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

        /// <summary>Clears the buffer a renewal carried, unless progress was reported since it was taken.</summary>
        /// <param name="version">The version the carried row was taken at.</param>
        public void Carried(long version)
        {
            Clear(version, percent: true, message: true, state: true);
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
