// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Core.Settings
{
    /// <summary>
    ///     The database routed for a tenant holds another tenant's settings, or is unprovisioned
    ///     outside the platform's own provisioning work: nothing is served from it. It surfaces as an
    ///     internal server error, never as a client error.
    /// </summary>
    /// <param name="expectedTenantId">The routed tenant.</param>
    /// <param name="actualTenantId">The tenant the database's settings name; zero for an unprovisioned database.</param>
    internal sealed class TenantDatabaseMismatchException(int expectedTenantId, int actualTenantId)
        : InvalidOperationException(string.Create(
            CultureInfo.InvariantCulture,
            $"The database routed for tenant {expectedTenantId} holds the settings of tenant {actualTenantId}; check the catalog's location of the tenant, and that the database was not restored or cloned from another tenant."))
    {
        /// <summary>The routed tenant.</summary>
        public int ExpectedTenantId { get; } = expectedTenantId;

        /// <summary>The tenant the database's settings name; zero for an unprovisioned database.</summary>
        public int ActualTenantId { get; } = actualTenantId;
    }
}
