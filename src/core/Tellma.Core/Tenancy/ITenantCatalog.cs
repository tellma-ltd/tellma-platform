// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The catalog's write side: registration, state transitions, renames and relocations.
    /// </summary>
    /// <remarks>
    ///     Every write is one catalog transaction that also replaces <c>catalog.CatalogState</c>'s
    ///     version, then refreshes this instance's registry, which notifies the tenant-state
    ///     listeners: the writing instance is consistent at once and every other instance within one
    ///     refresh interval. A write that committed is never reported as failed because the refresh
    ///     after it failed; the refresh loop converges instead. A singleton.
    /// </remarks>
    public interface ITenantCatalog
    {
        /// <summary>
        ///     Registers a tenant as <see cref="TenantState.Provisioning" />, with the id the sequence
        ///     allocates (from 1000) or the requested one.
        /// </summary>
        /// <param name="registration">The tenant to register.</param>
        /// <param name="actor">Who registers it: a subject, a client id, or <c>migrator</c>; 1 to 128 characters.</param>
        /// <param name="requestedId">
        ///     An id in 1–999, which the sequence never issues; passed only by the migrator's seed path
        ///     and its provisioning command.
        /// </param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The registered tenant.</returns>
        /// <exception cref="ArgumentException">A name, actor or location part is malformed.</exception>
        /// <exception cref="TenantStateException">
        ///     <c>tenant-registration-refused</c>: the requested id is outside 1–999 or taken; a live
        ///     tenant names a live tenant; a sandbox names a tenant that is not live; the server is not
        ///     allowed; the profile is not configured; or the location is another tenant's.
        /// </exception>
        Task<TenantDescriptor> RegisterAsync(
            TenantRegistration registration, string actor, int? requestedId = null, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Moves a tenant to another state: the suspension hook of the control plane and the
        ///     migrator's <c>set-state</c>.
        /// </summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <param name="state">The state to move to.</param>
        /// <param name="reason">The operator's reason, or null; at most 1024 characters.</param>
        /// <param name="actor">Who moves it: a subject, a client id, or <c>migrator</c>; 1 to 128 characters.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>The tenant in its new state.</returns>
        /// <exception cref="ArgumentException">The reason or actor is malformed.</exception>
        /// <exception cref="TenantNotFoundException">The catalog has no such tenant.</exception>
        /// <exception cref="TenantStateException">
        ///     <c>tenant-illegal-transition</c>: the state table forbids the transition, or a concurrent
        ///     transition changed the tenant's state first.
        /// </exception>
        Task<TenantDescriptor> SetStateAsync(
            int tenantId, TenantState state, string? reason, string actor, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Mirrors the tenant's name group and content-language codes from its settings, so the
        ///     tenant picker localizes without opening the tenant's database.
        /// </summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <param name="names">The names and codes, each name paired with the code at its position.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the names are mirrored.</returns>
        /// <exception cref="ArgumentException">A name or code is malformed, or a name is not paired with its code.</exception>
        /// <exception cref="TenantNotFoundException">The catalog has no such tenant.</exception>
        Task RenameAsync(int tenantId, TenantDisplayNames names, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Moves a tenant's database: its server, database and credential profile. The tenant must be
        ///     <see cref="TenantState.Frozen" /> or <see cref="TenantState.Provisioning" />, since a write
        ///     made after the copy was taken would be lost.
        /// </summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <param name="location">The new location.</param>
        /// <param name="actor">Who moves it; 1 to 128 characters, recorded in logs.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>A task that completes when the location is changed.</returns>
        /// <exception cref="ArgumentException">A location part or the actor is malformed.</exception>
        /// <exception cref="TenantNotFoundException">The catalog has no such tenant.</exception>
        /// <exception cref="TenantStateException">
        ///     <c>tenant-location-refused</c>: the tenant is neither frozen nor provisioning, the server
        ///     is not allowed, the profile is not configured, or the location is another tenant's.
        /// </exception>
        Task RelocateAsync(int tenantId, TenantLocation location, string actor, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Every tenant as the catalog holds it now, read from the store rather than the snapshot,
        ///     ordered by id. Rows the snapshot would refuse are listed too, with an empty connection
        ///     string, so an operator can see and repair them.
        /// </summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The tenants.</returns>
        Task<IReadOnlyList<TenantInfo>> ListAsync(CancellationToken cancellationToken = default);
    }
}
