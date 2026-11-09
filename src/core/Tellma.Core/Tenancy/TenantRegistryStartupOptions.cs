// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     How the registry's first load behaves at startup. Not configuration: the web host keeps the
    ///     defaults, the migrator turns the catalog verification off, and the suites shorten the bound.
    /// </summary>
    internal sealed class TenantRegistryStartupOptions
    {
        /// <summary>The bound on the first load; 10 seconds.</summary>
        public static readonly TimeSpan DefaultFirstLoadTimeout = TimeSpan.FromSeconds(10);

        /// <summary>
        ///     Whether the catalog must be migrated to the platform's latest catalog migration before
        ///     any tenant is served, and a missing or behind catalog fails startup. The migrator turns
        ///     it off, since it creates and migrates the catalog itself.
        /// </summary>
        public bool VerifyCatalog { get; set; } = true;

        /// <summary>How long startup waits for the first load before the host starts without it.</summary>
        public TimeSpan FirstLoadTimeout { get; set; } = DefaultFirstLoadTimeout;
    }
}
