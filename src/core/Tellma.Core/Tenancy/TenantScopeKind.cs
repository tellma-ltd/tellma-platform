// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Tenancy
{
    /// <summary>What bound a service scope to a tenant: the <c>kind</c> tag of the scopes counter.</summary>
    internal enum TenantScopeKind
    {
        /// <summary>A request, whose scope the tenant middleware bound.</summary>
        Request,

        /// <summary>Background work, whose scope the tenant scope factory created.</summary>
        Background,
    }
}
