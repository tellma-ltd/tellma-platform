// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Data.Ids
{
    /// <summary>
    ///     Forgets a tenant's id buffers whenever the database behind the tenant may change: when it
    ///     enters or leaves <see cref="TenantState.Frozen" />, the state every relocation and
    ///     point-in-time restore passes through, and when it leaves the registry. A restored database's
    ///     sequence restarts below ids the old one issued, so a buffer kept across the cut-over would
    ///     hand out ids the new database issues again.
    /// </summary>
    /// <remarks>
    ///     The freeze sets the database read-only, so no range is read while the tenant is frozen;
    ///     forgetting on the way out as well covers a range a batch read just before the freeze.
    /// </remarks>
    /// <param name="allocator">The id allocator.</param>
    internal sealed class IdBufferTenantListener(IdAllocator allocator) : ITenantStateListener
    {
        /// <inheritdoc />
        public Task OnStateChangedAsync(TenantDescriptor tenant, TenantState? previous)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            if (tenant.State == TenantState.Frozen || previous == TenantState.Frozen)
            {
                allocator.ForgetTenant(tenant.Id);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task OnRemovedAsync(TenantDescriptor tenant)
        {
            ArgumentNullException.ThrowIfNull(tenant);
            allocator.ForgetTenant(tenant.Id);
            return Task.CompletedTask;
        }
    }
}
