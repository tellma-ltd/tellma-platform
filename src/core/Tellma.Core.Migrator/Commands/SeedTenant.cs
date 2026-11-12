// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     One entry of <c>Tellma:Seed:Tenants</c>: a tenant <c>migrate</c> registers under its own id
    ///     when the catalog lacks it, then provisions.
    /// </summary>
    /// <remarks>
    ///     The languages and calendars are written into the tenant's settings by the settings step,
    ///     which reads them from the same configuration.
    /// </remarks>
    internal sealed class SeedTenant
    {
        /// <summary>The tenant's id: the id its registration requests, in 1 to 999.</summary>
        public int Id { get; set; }

        /// <summary>The tenant's name.</summary>
        public string? Name { get; set; }

        /// <summary>Whether the tenant is live or a sandbox.</summary>
        public TenantCategory Category { get; set; } = TenantCategory.Live;

        /// <summary>The live tenant a sandbox derives from.</summary>
        public int? LiveTenantId { get; set; }

        /// <summary>The tenant's content languages, such as <c>en</c> and <c>ar</c>.</summary>
        public IList<string> Languages { get; } = [];

        /// <summary>The tenant's calendars, such as <c>gc</c> and <c>uq</c>.</summary>
        public IList<string> Calendars { get; } = [];
    }
}
