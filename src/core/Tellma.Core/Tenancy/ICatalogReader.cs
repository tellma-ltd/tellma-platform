// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>The catalog's migration state against the platform's latest catalog migration.</summary>
    internal enum CatalogHistoryState
    {
        /// <summary>The catalog's history lists the latest migration.</summary>
        Current,

        /// <summary>The catalog's history lacks the latest migration.</summary>
        Behind,

        /// <summary>The catalog database exists but has no migration history.</summary>
        MissingSchema,

        /// <summary>The catalog database cannot be opened: it is missing, or the login has no access to it.</summary>
        MissingDatabase,
    }

    /// <summary>The rows of one snapshot reload and the catalog version they were read at.</summary>
    /// <param name="Version">The version, read before the rows.</param>
    /// <param name="Rows">The rows.</param>
    internal sealed record CatalogReload(Guid Version, IReadOnlyList<TenantRow> Rows);

    /// <summary>The registry's reads of the catalog. One implementation reads SQL; the suites substitute a fake.</summary>
    internal interface ICatalogReader
    {
        /// <summary>Reads the catalog's migration state.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The state.</returns>
        Task<CatalogHistoryState> ReadHistoryAsync(CancellationToken cancellationToken);

        /// <summary>Runs the refresh probe: the catalog's current version.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The version.</returns>
        Task<Guid> ReadVersionAsync(CancellationToken cancellationToken);

        /// <summary>Runs the snapshot reload: the version, then every tenant row.</summary>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The version and the rows.</returns>
        Task<CatalogReload> ReadTenantsAsync(CancellationToken cancellationToken);
    }
}
