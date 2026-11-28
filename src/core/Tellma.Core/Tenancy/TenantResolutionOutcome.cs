// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>How a tenant request was resolved: the <c>outcome</c> tag of the resolutions counter.</summary>
    internal enum TenantResolutionOutcome
    {
        /// <summary>The tenant serves the request and the caller is one of its members.</summary>
        Served,

        /// <summary>No served tenant has the id: absent or retired.</summary>
        NotFound,

        /// <summary>The tenant is being provisioned, which a request sees as absent.</summary>
        Provisioning,

        /// <summary>The tenant is read-only and the request would change its data.</summary>
        ReadOnly,

        /// <summary>The tenant is frozen and the request would change its data.</summary>
        Frozen,

        /// <summary>The tenant is suspended.</summary>
        Suspended,

        /// <summary>The tenant serves the request, but the caller is not one of its active members.</summary>
        NotMember,

        /// <summary>The tenant snapshot cannot be served.</summary>
        CatalogUnavailable,
    }
}
