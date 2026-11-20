// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Jobs;

namespace Tellma.Core.Jobs
{
    // Spec 0020 §6.5: claimed batches run under a per-instance SemaphoreSlim(MaxParallelBatches) and
    // a per-(tenant, key) counter enforcing the handler's MaxConcurrency; a key with no free slot is
    // omitted from the poll's claims. A slot is reserved before its claim is appended and released
    // once the claim's batch ended, or at once when the claim leased nothing. A poll that had to omit
    // a key would otherwise back off to the longest interval with work waiting, so the release of any
    // slot wakes every tenant refused one since. The slots are sized on first use, not when the host
    // creates the worker, which it does before the startup gate reports any invalid option.
    /// <summary>The slots claimed batches run in on this instance.</summary>
    internal sealed class JobConcurrency : IDisposable
    {
        /// <summary>The instance's batch slots, sized on first use.</summary>
        private readonly Lazy<SemaphoreSlim> _slots;

        /// <summary>The most batches the instance runs at once, read on first use.</summary>
        private readonly Lazy<int> _maxParallelBatches;

        /// <summary>Wakes the pollers of tenants refused a slot.</summary>
        private readonly TenantPollerWake _wake;

        /// <summary>Guards the counters and the refused tenants.</summary>
        private readonly Lock _gate = new();

        /// <summary>The batches running per tenant and key.</summary>
        private readonly Dictionary<(int TenantId, string Key), int> _running = [];

        /// <summary>The tenants refused a slot since the last release.</summary>
        private readonly HashSet<int> _refused = [];

        /// <summary>Creates the slots from the options.</summary>
        /// <param name="options">The job options, whose <see cref="JobsOptions.MaxParallelBatches" /> sizes the instance's slots.</param>
        /// <param name="wake">Wakes the pollers of tenants refused a slot.</param>
        public JobConcurrency(IOptions<JobsOptions> options, TenantPollerWake wake)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(wake);
            _maxParallelBatches = new Lazy<int>(() => options.Value.MaxParallelBatches);
            _slots = new Lazy<SemaphoreSlim>(() => new SemaphoreSlim(MaxParallelBatches, MaxParallelBatches));
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

        /// <summary>The instance's slots free now.</summary>
        public int Free => Batches.CurrentCount;

        /// <summary>The instance's batch slots.</summary>
        private SemaphoreSlim Batches => _slots.Value;

        /// <summary>The batches of a tenant's key running now.</summary>
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

        /// <summary>Reserves a slot for one claim of a tenant's key, when the key and the instance both have one free.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="handler">The key's handler, whose <see cref="JobHandlerAttribute.MaxConcurrency" /> bounds the key.</param>
        /// <returns>The reservation; null when no slot is free.</returns>
        public JobSlotReservation? TryReserve(int tenantId, JobHandlerRegistration handler)
        {
            ArgumentNullException.ThrowIfNull(handler);
            lock (_gate)
            {
                (int, string) key = (tenantId, handler.Key);
                int running = _running.GetValueOrDefault(key);
                if (running >= handler.Options.MaxConcurrency || !Batches.Wait(0))
                {
                    _ = _refused.Add(tenantId);
                    return null;
                }

                _running[key] = running + 1;
                return new JobSlotReservation(this, tenantId, handler.Key);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_slots.IsValueCreated)
            {
                _slots.Value.Dispose();
            }
        }

        /// <summary>Frees a reserved slot and wakes the tenants refused one since the last release.</summary>
        /// <param name="tenantId">The reservation's tenant.</param>
        /// <param name="handlerKey">The reservation's key.</param>
        internal void Release(int tenantId, string handlerKey)
        {
            int[] refused;
            lock (_gate)
            {
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

                try
                {
                    _ = Batches.Release();
                }
                catch (ObjectDisposedException)
                {
                    // The host disposed the slots after its drain: a batch that outlived it frees nothing.
                }

                refused = [.. _refused];
                _refused.Clear();
            }

            // Outside the lock: a wake runs the poller's subscription.
            foreach (int tenant in refused)
            {
                _wake.Wake(tenant);
            }
        }
    }

    /// <summary>One claim's reserved slot, released once, however many times it is disposed.</summary>
    internal sealed class JobSlotReservation : IDisposable
    {
        /// <summary>The slots the reservation came from.</summary>
        private readonly JobConcurrency _owner;

        /// <summary>1 once released.</summary>
        private int _released;

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

        /// <summary>Releases the slot.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                _owner.Release(TenantId, HandlerKey);
            }
        }
    }
}
