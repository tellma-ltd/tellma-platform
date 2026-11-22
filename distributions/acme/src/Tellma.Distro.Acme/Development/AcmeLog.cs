// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Distro.Acme.Development
{
    /// <summary>The Acme distribution's structured log events.</summary>
    internal static partial class AcmeLog
    {
        /// <summary>The sample-data step wrote the rows a development tenant lacked.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="currencies">The currencies inserted.</param>
        /// <param name="customers">The customers inserted.</param>
        /// <param name="roles">The roles inserted.</param>
        /// <param name="users">The users inserted.</param>
        /// <param name="invoices">The invoices inserted.</param>
        [LoggerMessage(
            EventName = "SampleDataProvisioned",
            Level = LogLevel.Information,
            Message = "Wrote the sample data on tenant {TenantId}: {Currencies} currencies, {Customers} customers, {Roles} roles, {Users} users and {Invoices} invoices inserted.")]
        public static partial void SampleDataProvisioned(ILogger logger, int tenantId, int currencies, int customers, int roles, int users, int invoices);
    }
}
