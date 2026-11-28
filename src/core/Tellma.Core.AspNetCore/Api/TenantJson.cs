// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §8.5: tenant JSON carries Cache-Control: no-store; output caching never runs under
    // /{tenantId}, and the blob GET's caching is the blob surface's own.

    /// <summary>Which responses are tenant JSON, which no cache may store.</summary>
    internal static class TenantJson
    {
        /// <summary>The cache directive every tenant JSON response carries.</summary>
        public const string NoStore = "no-store";

        /// <summary>
        ///     Whether a response answers a tenant JSON surface: the web surface, the public API or
        ///     MCP. The endpoint is the matched one, or for a problem the exception handler writes,
        ///     the one that threw.
        /// </summary>
        /// <param name="context">The request.</param>
        /// <returns>True for a tenant JSON response.</returns>
        public static bool Is(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Endpoint? endpoint = context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint;
            return endpoint?.Metadata.GetMetadata<TenantEndpointMetadata>()?.Surface is TenantSurface.Web or TenantSurface.Api or TenantSurface.Mcp;
        }
    }
}
