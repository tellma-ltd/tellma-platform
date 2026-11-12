// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore
{
    /// <summary>
    ///     The platform's route groups, which <c>MapTellma</c> returns so a distribution maps its own
    ///     endpoints onto them and inherits their policies, filters and metadata.
    /// </summary>
    public sealed class TellmaEndpoints
    {
        /// <summary>Creates the groups' holder.</summary>
        /// <param name="web">The web group.</param>
        /// <param name="api">The public API group.</param>
        /// <param name="hub">The hub group.</param>
        /// <param name="blobs">The blob group.</param>
        /// <param name="tenantless">The tenantless group.</param>
        internal TellmaEndpoints(RouteGroupBuilder web, RouteGroupBuilder api, RouteGroupBuilder hub, RouteGroupBuilder blobs, RouteGroupBuilder tenantless)
        {
            Web = web;
            Api = api;
            Hub = hub;
            Blobs = blobs;
            Tenantless = tenantless;
        }

        /// <summary>
        ///     <c>/{tenantId}/api/web</c>: the SPA's tenant API, on <c>Tellma.Web</c> behind the tenant
        ///     access filter.
        /// </summary>
        public RouteGroupBuilder Web { get; }

        /// <summary>
        ///     <c>/{tenantId}/api/v{version}</c>: the public API's seam, on <c>Tellma.Api</c> behind the
        ///     tenant access filter. Nothing is mapped on it yet.
        /// </summary>
        public RouteGroupBuilder Api { get; }

        /// <summary>
        ///     <c>/{tenantId}/hub</c>: the realtime hub, on <c>Tellma.Web</c>; the tenant middleware is
        ///     its front door, since no endpoint filter reaches a hub's endpoints.
        /// </summary>
        public RouteGroupBuilder Hub { get; }

        /// <summary><c>/{tenantId}/blobs</c>: the blob endpoints, on <c>Tellma.Web</c> behind the tenant access filter.</summary>
        public RouteGroupBuilder Blobs { get; }

        /// <summary>
        ///     <c>/api</c>: a distribution's tenantless endpoints, each of which calls
        ///     <c>AsTenantlessEndpoint</c>.
        /// </summary>
        public RouteGroupBuilder Tenantless { get; }
    }
}
