// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Provisioning
{
    /// <summary>
    ///     What the migrator prints after a tenant's provisioning steps: the stored definitions the
    ///     migrated schema no longer accepts, one line each. The migrator resolves it from the
    ///     tenant's system scope when one is registered, and skips the scan otherwise.
    /// </summary>
    internal interface ITenantDriftReport
    {
        /// <summary>Scans the tenant of the current scope.</summary>
        /// <param name="cancellationToken">Cancels the scan.</param>
        /// <returns>One printable line per drifted definition; empty when nothing drifted.</returns>
        Task<IReadOnlyList<string>> ScanAsync(CancellationToken cancellationToken);
    }
}
