// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     The problem codes of the tenancy exceptions: the <c>Code</c> of
    ///     <see cref="TenantNotFoundException" />, <see cref="TenantUnavailableException" /> and
    ///     <see cref="TenantStateException" />. Codes are kebab-case and never renamed.
    /// </summary>
    public static class TenantProblemCodes
    {
        /// <summary>The tenant does not exist, is provisioning or retired, or the caller is not a member: 404.</summary>
        public const string NotFound = "tenant-not-found";

        /// <summary>The tenant snapshot is older than its staleness bound, or was never loaded: 503.</summary>
        public const string CatalogUnavailable = "catalog-unavailable";

        /// <summary>The tenant database's recorded schema does not include the running model's: 503.</summary>
        public const string SchemaBehind = "tenant-schema-behind";

        /// <summary>The tenant is suspended: 403.</summary>
        public const string Suspended = "tenant-suspended";

        /// <summary>The tenant is read-only and the request would change its data: 403.</summary>
        public const string ReadOnly = "tenant-read-only";

        /// <summary>The tenant is frozen and the request would change its data: 403.</summary>
        public const string Frozen = "tenant-frozen";

        /// <summary>A registration the catalog refused: a requested id, a live tenant link or a location: 409.</summary>
        public const string RegistrationRefused = "tenant-registration-refused";

        /// <summary>A state transition the state table forbids, or one lost to a concurrent transition: 409.</summary>
        public const string IllegalTransition = "tenant-illegal-transition";

        /// <summary>A relocation the catalog refused: a tenant neither frozen nor provisioning, or a location not allowed: 409.</summary>
        public const string LocationRefused = "tenant-location-refused";
    }
}
