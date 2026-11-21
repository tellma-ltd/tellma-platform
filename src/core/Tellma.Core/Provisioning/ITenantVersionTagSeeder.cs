// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     The migrator's seed of a tenant database's version tags: the row of every tag name the
    ///     composition knows, and the wholesale move of every tag after a run that changed the
    ///     database. The migrator calls it when one is registered and skips it otherwise.
    /// </summary>
    /// <remarks>
    ///     Both calls run outside any tenant scope, after the database lists the migrator's schema
    ///     fingerprint, on a tenant that may still be provisioning or be suspended.
    /// </remarks>
    internal interface ITenantVersionTagSeeder
    {
        /// <summary>Inserts the row of every tag name the database lacks; runs on every migration of the tenant.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the seed.</param>
        /// <returns>A task that completes when the rows are committed.</returns>
        Task SeedAsync(int tenantId, CancellationToken cancellationToken);

        /// <summary>Moves every tag of the database; runs only after a run that applied a migration or a provisioning step there.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the bump.</param>
        /// <returns>A task that completes when the tags are committed.</returns>
        Task BumpAllAsync(int tenantId, CancellationToken cancellationToken);
    }
}
