// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>
    ///     A tenant surface a host adapter contributes outside the platform's tenant groups, such as
    ///     the tenant MCP server at <c>/{tenantId}/mcp</c>. <c>MapTellma</c> maps every registered
    ///     source after its groups, and the distribution's information document announces each
    ///     source's route under its key.
    /// </summary>
    /// <remarks>
    ///     Internal, so only the platform's own adapters can map a tenant endpoint outside the groups;
    ///     the endpoint audit still holds each endpoint a source maps to its surface's rules, the
    ///     front door its surface requires included.
    /// </remarks>
    internal interface ITenantSurfaceSource
    {
        /// <summary>The surface's key in the information document's surfaces, such as <c>mcp</c>.</summary>
        string Key { get; }

        /// <summary>The surface's route template as the information document announces it, such as <c>/{tenantId}/mcp</c>.</summary>
        string Route { get; }

        /// <summary>Maps the surface's endpoints.</summary>
        /// <param name="endpoints">The application's route builder.</param>
        void Map(IEndpointRouteBuilder endpoints);
    }
}
