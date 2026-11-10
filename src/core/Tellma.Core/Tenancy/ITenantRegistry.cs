// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The process's in-memory snapshot of the catalog's tenants, with a bounded staleness. Reads
    ///     do no I/O; a hosted loop probes the catalog's version every refresh interval and reloads the
    ///     snapshot only when the version differs.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The snapshot fails closed. Once it was last confirmed against the catalog longer ago
    ///         than <c>Tellma:Catalog:MaxStaleness</c>, or before it was ever loaded,
    ///         <see cref="Find" /> and <see cref="Get" /> throw <see cref="TenantUnavailableException" />
    ///         with the code <c>catalog-unavailable</c>, so a catalog outage never turns into a
    ///         suspension that stays ignored.
    ///     </para>
    ///     <para>
    ///         A row whose server is outside <c>Tellma:Sql:AllowedServers</c>, whose location breaks the
    ///         location grammar, or whose profile is not configured stays out of the snapshot and is
    ///         logged at Error, so its tenant answers not-found and no row can send the application's
    ///         credential to another host.
    ///     </para>
    ///     <para>A singleton, safe to read from any thread.</para>
    /// </remarks>
    public interface ITenantRegistry
    {
        /// <summary>
        ///     The snapshot's tenants, ordered by id; empty before the first load. Unlike
        ///     <see cref="Find" />, it does not fail when the snapshot is stale: a caller that acts on
        ///     a tenant re-resolves it through <see cref="Get" />.
        /// </summary>
        IReadOnlyList<TenantInfo> Tenants { get; }

        /// <summary>The <c>catalog.CatalogState</c> version the snapshot was loaded from; <see cref="Guid.Empty" /> before the first load.</summary>
        Guid SnapshotVersion { get; }

        /// <summary>
        ///     When the snapshot was last confirmed against the catalog: loaded, or found current by a
        ///     version probe. Null before the first load. The staleness bound counts from it.
        /// </summary>
        DateTimeOffset? SnapshotLoadedAt { get; }

        /// <summary>
        ///     Whether the snapshot may be served: loaded, and confirmed within the staleness bound.
        ///     Readiness reports it.
        /// </summary>
        bool IsAvailable { get; }

        /// <summary>The tenant of an id, from the snapshot.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The tenant, or null when the snapshot does not hold it.</returns>
        /// <exception cref="TenantUnavailableException">The snapshot is unavailable (<c>catalog-unavailable</c>).</exception>
        TenantInfo? Find(int tenantId);

        /// <summary>The tenant of an id, from the snapshot.</summary>
        /// <param name="tenantId">The tenant id.</param>
        /// <returns>The tenant.</returns>
        /// <exception cref="TenantNotFoundException">The snapshot does not hold the tenant.</exception>
        /// <exception cref="TenantUnavailableException">The snapshot is unavailable (<c>catalog-unavailable</c>).</exception>
        [SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Find and Get are the registry's two lookups, and Get is the throwing one.")]
        TenantInfo Get(int tenantId);

        /// <summary>
        ///     Refreshes the snapshot: probes the catalog's version and reloads when it differs from the
        ///     snapshot's, or always when <paramref name="force" /> is set. Single-flight: refreshes
        ///     never overlap, a refresh asked for while one runs joins it, and a forced refresh asked for
        ///     while one runs runs right after it, so it always reads what was committed before it was
        ///     asked for.
        /// </summary>
        /// <param name="force">Reload even when the version is unchanged.</param>
        /// <param name="cancellationToken">Stops waiting for the refresh; the refresh itself runs on.</param>
        /// <returns>A task that completes when the refresh did, failing as it failed.</returns>
        Task RefreshAsync(bool force, CancellationToken cancellationToken = default);
    }
}
