// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>The tenant surface an endpoint belongs to, which fixes the policies it may carry.</summary>
    public enum TenantSurface
    {
        /// <summary>The SPA's web API, <c>/{tenantId}/api/web</c>, on the session cookie.</summary>
        Web,

        /// <summary>The versioned public API, <c>/{tenantId}/api/v{version}</c>, on a bearer token.</summary>
        Api,

        /// <summary>The tenant's MCP server, <c>/{tenantId}/mcp</c>, on a bearer token.</summary>
        Mcp,

        /// <summary>The realtime hub, <c>/{tenantId}/hub</c>, on the session cookie.</summary>
        Hub,

        /// <summary>The blob endpoints, <c>/{tenantId}/blobs</c>, on the session cookie.</summary>
        Blobs,
    }
}
