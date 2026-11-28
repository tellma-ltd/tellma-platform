// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tests.Infrastructure
{
    /// <summary>The catalog's write side in memory, recording what the admin surface asked of it.</summary>
    internal sealed class FakeTenantCatalog : ITenantCatalog
    {
        private readonly Lock _sync = new();
        private readonly List<TenantInfo> _tenants = [];
        private int _nextId = 1000;

        /// <summary>The registrations received, with their actor and requested id.</summary>
        public List<(TenantRegistration Registration, string Actor, int? RequestedId)> Registrations { get; } = [];

        /// <summary>The state changes received, with their reason and actor.</summary>
        public List<(int TenantId, TenantState State, string? Reason, string Actor)> StateChanges { get; } = [];

        /// <summary>Adds a tenant.</summary>
        /// <param name="id">The id.</param>
        /// <param name="state">The state.</param>
        public void Add(int id, TenantState state = TenantState.Active)
        {
            lock (_sync)
            {
                _tenants.Add(Info(new TenantDescriptor(id, $"Tenant {id}", null, null, ["en"], TenantCategory.Live, state, null)));
            }
        }

        /// <inheritdoc />
        public Task<TenantDescriptor> RegisterAsync(
            TenantRegistration registration, string actor, int? requestedId = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(registration);
            lock (_sync)
            {
                Registrations.Add((registration, actor, requestedId));
                TenantDescriptor descriptor = new(
                    requestedId ?? _nextId++, registration.Name, null, null, ["en"], registration.Category, TenantState.Provisioning, registration.LiveTenantId);
                _tenants.Add(Info(descriptor));
                return Task.FromResult(descriptor);
            }
        }

        /// <inheritdoc />
        public Task<TenantDescriptor> SetStateAsync(
            int tenantId, TenantState state, string? reason, string actor, CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                StateChanges.Add((tenantId, state, reason, actor));
                int index = _tenants.FindIndex(tenant => tenant.Id == tenantId);
                if (index < 0)
                {
                    throw new TenantNotFoundException(tenantId);
                }

                if (_tenants[index].Descriptor.State == TenantState.Retired)
                {
                    throw new TenantStateException(TenantProblemCodes.IllegalTransition, "A retired tenant stays retired.");
                }

                TenantDescriptor descriptor = _tenants[index].Descriptor with { State = state };
                _tenants[index] = Info(descriptor);
                return Task.FromResult(descriptor);
            }
        }

        /// <inheritdoc />
        public Task RenameAsync(int tenantId, TenantDisplayNames names, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The admin surface renames no tenant.");
        }

        /// <inheritdoc />
        public Task RelocateAsync(int tenantId, TenantLocation location, string actor, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("The admin surface relocates no tenant.");
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default)
        {
            lock (_sync)
            {
                return Task.FromResult<IReadOnlyList<TenantInfo>>([.. _tenants]);
            }
        }

        /// <summary>A tenant's catalog entry.</summary>
        /// <param name="descriptor">Its descriptor.</param>
        /// <returns>The entry.</returns>
        private static TenantInfo Info(TenantDescriptor descriptor)
        {
            return new TenantInfo(
                descriptor,
                new TenantLocation("sql01.database.windows.net", $"tenant-{descriptor.Id}", "default"),
                "Data Source=sql01.database.windows.net",
                null,
                Guid.NewGuid());
        }
    }
}
