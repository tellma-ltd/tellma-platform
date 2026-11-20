// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.5: claimed batches run under a per-instance bound of MaxParallelBatches and a
    // per-(tenant, key) counter enforcing the handler's MaxConcurrency; a key with no free slot is
    // omitted from the poll's claims.
    //
    // The two bounds are held differently, so an idle poll never starves another tenant and the
    // instance never runs more than MaxParallelBatches batches:
    // - A key's own slot is reserved before its claim is appended, and released once the claim's
    //   batch ended, or at once when the claim leased nothing. It is the tenant's alone.
    // - An instance slot is taken only when a claim that leased rows starts, and only while one is
    //   free. A poll plans its keys while a slot is free, so the polls planned in the same instant
    //   may lease more batches than the free slots; a claim that finds none left is released
    //   uncounted by its dispatcher, and its tenant waits for a freed slot.
    //
    // A poll that had to omit a key, or to release a claim, would otherwise back off with work
    // waiting, so:
    // - a key refused at its MaxConcurrency wakes its tenant once one of its own batches ended, and
    //   never earlier, so the tenant does not poll back to back while the key is still at its limit;
    // - a tenant refused an instance slot, at its plan or at its start, is woken when a started batch
    //   frees one, one tenant per freed slot, first refused first.
    //
    // The bound is read on first use, not when the host creates the worker, which it does before the
    // startup gate reports any invalid option.
    /// <summary>The slots claimed batches run in on this instance.</summary>
    internal sealed class JobConcurrency
    {
        /// <summary>The most batches the instance runs at once, read on first use.</summary>
        private readonly Lazy<int> _maxParallelBatches;

        /// <summary>Wakes the pollers of tenants refused a slot.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>Guards the counters and the refusals.</summary>
        private readonly Lock _gate = new();

        /// <summary>The slots reserved per tenant and key: the batches planned or running.</summary>
        private readonly Dictionary<(int TenantId, string Key), int> _running = [];

        /// <summary>The tenants' keys refused at their own limit, each waiting for one of its batches to end.</summary>
        private readonly HashSet<(int TenantId, string Key)> _keyRefused = [];

        /// <summary>The tenants refused an instance slot, first refused first.</summary>
        private readonly Queue<int> _instanceRefused = [];

        /// <summary>The tenants in <see cref="_instanceRefused" />, so each waits there once.</summary>
        private readonly HashSet<int> _instanceRefusedSet = [];

        /// <summary>The batches started and not yet ended: the instance slots taken.</summary>
        private int _started;

        /// <summary>Creates the slots from the options.</summary>
        /// <param name="options">The job options, whose <see cref="JobsOptions.MaxParallelBatches" /> bounds the instance.</param>
        /// <param name="wake">Wakes the pollers of tenants refused a slot.</param>
        public JobConcurrency(IOptions<JobsOptions> options, TenantPollerWake wake)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(wake);
            _maxParallelBatches = new Lazy<int>(() => options.Value.MaxParallelBatches);
            _wake = wake;
        }

        /// <summary>Creates the slots.</summary>
        /// <param name="maxParallelBatches">The most batches the instance runs at once.</param>
        /// <param name="wake">Wakes the pollers of tenants refused a slot.</param>
        internal JobConcurrency(int maxParallelBatches, TenantPollerWake wake)
            : this(Options.Create(new JobsOptions { MaxParallelBatches = maxParallelBatches }), wake)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maxParallelBatches, 1);
        }

        /// <summary>The most batches the instance runs at once.</summary>
        public int MaxParallelBatches => _maxParallelBatches.Value;

        /// <summary>The instance's slots free now; zero while the started batches reach the bound.</summary>
        public int Free
        {
            get
            {
                lock (_gate)
                {
                    return MaxParallelBatches - _started;
                }
            }
        }

        /// <summary>The batches started and not yet ended on this instance.</summary>
        public int Started
        {
            get
            {
                lock (_gate)
                {
                    return _started;
                }
            }
        }

        /// <summary>The slots of a tenant's key reserved now: its batches planned or running.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handlerKey">The key.</param>
        /// <returns>The count.</returns>
        public int Running(int tenantId, string handlerKey)
        {
            lock (_gate)
            {
                return _running.GetValueOrDefault((tenantId, handlerKey));
            }
        }

        /// <summary>Reserves a slot of a tenant's key for one claim, when the key has one free and the instance a slot to start it in.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The key's handler, whose <see cref="JobHandlerAttribute.MaxConcurrency" /> bounds the key.</param>
        /// <returns>The reservation; null when the key is at its limit or the instance has no free slot.</returns>
        public JobSlotReservation? TryReserve(int tenantId, JobHandlerRegistration handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_gate)
            {
                (int, string) key = (tenantId, handler.Key);
                int running = _running.GetValueOrDefault(key);

                // --- The key's own limit: the tenant is woken once one of the key's batches ended ---
                if (running >= handler.Options.MaxConcurrency)
                {
                    _ = _keyRefused.Add(key);
                    return null;
                }

                // --- The instance's bound: the tenant waits its turn for a freed slot ---
                if (_started >= MaxParallelBatches)
                {
                    QueueInstanceRefused(tenantId);
                    return null;
                }

                _ = _keyRefused.Remove(key);
                _running[key] = running + 1;
                return new JobSlotReservation(this, tenantId, handler.Key);
            }
        }

        /// <summary>Takes an instance slot for a reservation whose claim leased rows, when one is free; its tenant waits for a freed slot otherwise.</summary>
        /// <param name="reservation">The reservation, still reserved.</param>
        /// <returns>True when the slot was taken and the batch may start; false when no slot is free or the reservation was released.</returns>
        internal bool TryStart(JobSlotReservation reservation)
        {
            lock (_gate)
            {
                if (_started >= MaxParallelBatches)
                {
                    QueueInstanceRefused(reservation.TenantId);
                    return false;
                }

                // The reservation moves to running under the gate, so the count and its state agree.
                if (!reservation.TryMarkRunning())
                {
                    return false;
                }

                _started++;
                return true;
            }
        }

        /// <summary>Frees a reservation's slots and wakes the tenants its end lets claim again.</summary>
        /// <param name="tenantId">The reservation's tenant.</param>
        /// <param name="handlerKey">The reservation's key.</param>
        /// <param name="started">Whether its batch started, so it held an instance slot too.</param>
        internal void Release(int tenantId, string handlerKey, bool started)
        {
            List<int> woken = [];
            lock (_gate)
            {
                // --- The key's slot ---
                (int, string) key = (tenantId, handlerKey);
                int running = _running.GetValueOrDefault(key) - 1;
                if (running > 0)
                {
                    _running[key] = running;
                }
                else
                {
                    _ = _running.Remove(key);
                }

                // A claim that leased nothing ended no batch: it frees its key's slot and wakes nobody.
                if (started)
                {
                    // --- The tenant whose key waited for one of its batches to end ---
                    _started--;
                    if (_keyRefused.Remove(key))
                    {
                        woken.Add(tenantId);
                    }

                    // --- One tenant refused an instance slot, once a slot is actually free ---
                    if (_started < MaxParallelBatches && _instanceRefused.TryDequeue(out int next))
                    {
                        _ = _instanceRefusedSet.Remove(next);
                        if (!woken.Contains(next))
                        {
                            woken.Add(next);
                        }
                    }
                }
            }

            // Outside the lock: a wake runs the poller's subscription.
            foreach (int tenant in woken)
            {
                _wake.Wake(tenant);
            }
        }

        /// <summary>Queues a tenant refused an instance slot, once however often it is refused before it is woken; the caller holds the gate.</summary>
        /// <param name="tenantId">The tenant.</param>
        private void QueueInstanceRefused(int tenantId)
        {
            if (_instanceRefusedSet.Add(tenantId))
            {
                _instanceRefused.Enqueue(tenantId);
            }
        }
    }

    // A reservation moves once from reserved to started and once to released; a start after the
    // release does nothing, so a claim's batch never takes an instance slot it would not give back.
    /// <summary>One claim's reserved slot, released once, however many times it is disposed.</summary>
    internal sealed class JobSlotReservation : IDisposable
    {
        /// <summary>The state: reserved.</summary>
        private const int Reserved = 0;

        /// <summary>The state: its batch started, holding an instance slot.</summary>
        private const int Running = 1;

        /// <summary>The state: released.</summary>
        private const int Released = 2;

        /// <summary>The slots the reservation came from.</summary>
        private readonly JobConcurrency _owner;

        /// <summary>The state: <see cref="Reserved" />, <see cref="Running" /> or <see cref="Released" />.</summary>
        private int _state;

        /// <summary>Creates the reservation.</summary>
        /// <param name="owner">The slots it came from.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handlerKey">The key.</param>
        internal JobSlotReservation(JobConcurrency owner, int tenantId, string handlerKey)
        {
            _owner = owner;
            TenantId = tenantId;
            HandlerKey = handlerKey;
        }

        /// <summary>The tenant.</summary>
        public int TenantId { get; }

        /// <summary>The key.</summary>
        public string HandlerKey { get; }

        /// <summary>Takes an instance slot for the claim's batch, when one is free; once, and never after the release.</summary>
        /// <returns>True when the batch may start; false when the instance has no free slot, so the claim is to be released.</returns>
        public bool TryStart()
        {
            return Volatile.Read(ref _state) == Reserved && _owner.TryStart(this);
        }

        /// <summary>Moves the reservation from reserved to running; the slots' gate is held.</summary>
        /// <returns>True when it was still reserved.</returns>
        internal bool TryMarkRunning()
        {
            return Interlocked.CompareExchange(ref _state, Running, Reserved) == Reserved;
        }

        /// <summary>Releases the slots.</summary>
        public void Dispose()
        {
            int previous = Interlocked.Exchange(ref _state, Released);
            if (previous != Released)
            {
                _owner.Release(TenantId, HandlerKey, started: previous == Running);
            }
        }
    }
}
