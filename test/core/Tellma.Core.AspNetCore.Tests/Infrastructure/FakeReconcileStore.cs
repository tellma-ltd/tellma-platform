// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>
    ///     The reconciliation's store in memory: a lock shared by every store built over the same
    ///     <see cref="LockHolder" />, each tenant's users, and the reconciliations it was asked for.
    /// </summary>
    /// <param name="lockHolder">The lock the stores of one test share.</param>
    internal sealed class FakeReconcileStore(FakeReconcileStore.LockHolder lockHolder) : IMembershipReconcileStore
    {
        /// <summary>Creates a store with its own lock.</summary>
        public FakeReconcileStore()
            : this(new LockHolder())
        {
        }

        /// <summary>Each tenant's users.</summary>
        public ConcurrentDictionary<int, IReadOnlyList<TenantMembershipRecord>> Users { get; } = new();

        /// <summary>The tenants whose read fails.</summary>
        public ConcurrentDictionary<int, bool> Failing { get; } = new();

        /// <summary>The reconciliations asked for, in order.</summary>
        public ConcurrentQueue<(int TenantId, IReadOnlyList<TenantMembershipRecord> Records)> Reconciled { get; } = new();

        /// <summary>How many runs took the lock.</summary>
        public int Leads { get; private set; }

        /// <inheritdoc />
        public Task<IAsyncDisposable?> TryLeadAsync(CancellationToken cancellationToken)
        {
            if (!lockHolder.TryTake())
            {
                return Task.FromResult<IAsyncDisposable?>(null);
            }

            Leads++;
            return Task.FromResult<IAsyncDisposable?>(new Lease(lockHolder));
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<TenantMembershipRecord>> ReadUsersAsync(int tenantId, CancellationToken cancellationToken)
        {
            return Failing.ContainsKey(tenantId)
                ? Task.FromException<IReadOnlyList<TenantMembershipRecord>>(new InvalidOperationException("The tenant's database is unreachable."))
                : Task.FromResult(Users.GetValueOrDefault(tenantId) ?? []);
        }

        /// <inheritdoc />
        public Task<int> ReconcileAsync(int tenantId, IReadOnlyList<TenantMembershipRecord> records, CancellationToken cancellationToken)
        {
            Reconciled.Enqueue((tenantId, records));
            return Task.FromResult(records.Count);
        }

        /// <summary>The lock: taken by one store at a time.</summary>
        internal sealed class LockHolder
        {
            private int _held;

            /// <summary>Whether a store holds the lock.</summary>
            public bool IsHeld => Volatile.Read(ref _held) == 1;

            /// <summary>Takes the lock when it is free.</summary>
            /// <returns>True when taken.</returns>
            public bool TryTake()
            {
                return Interlocked.CompareExchange(ref _held, 1, 0) == 0;
            }

            /// <summary>Releases the lock.</summary>
            public void Release()
            {
                _ = Interlocked.Exchange(ref _held, 0);
            }
        }

        /// <summary>The held lock.</summary>
        /// <param name="holder">The lock.</param>
        private sealed class Lease(LockHolder holder) : IAsyncDisposable
        {
            /// <inheritdoc />
            public ValueTask DisposeAsync()
            {
                holder.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
