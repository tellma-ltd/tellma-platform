// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     The names of the web host's authorization policies. Each names its scheme explicitly, so a
    ///     cookie never authenticates a bearer surface and a bearer never authenticates the web
    ///     surface.
    /// </summary>
    public static class TellmaPolicies
    {
        /// <summary>The session cookie, authenticated: the SPA's surfaces.</summary>
        public const string Web = "Tellma.Web";

        /// <summary>A bearer token with the <c>tellma_api</c> scope for the distribution's own origin: the public API.</summary>
        public const string Api = "Tellma.Api";

        /// <summary>A bearer token with the <c>tellma_api</c> scope for the tenant's MCP resource.</summary>
        public const string Mcp = "Tellma.Mcp";

        /// <summary>
        ///     A bearer token with the <c>tellma_control_plane</c> scope for the distribution's own
        ///     origin: the admin surface.
        /// </summary>
        public const string ControlPlane = "Tellma.ControlPlane";
    }
}
