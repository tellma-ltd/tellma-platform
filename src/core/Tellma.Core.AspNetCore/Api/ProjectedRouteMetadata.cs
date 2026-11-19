// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Api;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>What a projected route is mapped under: a stack, a child query surface or a routed service.</summary>
    internal enum ProjectedRouteKind
    {
        /// <summary>A stack's standard operation or action, under the stack's resource segment.</summary>
        Stack,

        /// <summary>A child query surface's query or action, under the surface's resource segment.</summary>
        ChildQuery,

        /// <summary>A routed service's action, under the service's route.</summary>
        Service,
    }

    /// <summary>
    ///     The route model's entry a projected web endpoint serves, with where it is mapped. The
    ///     audit reads it to judge the segments, and the request filter to tag the request's activity.
    /// </summary>
    /// <param name="Route">The route model's entry.</param>
    /// <param name="Kind">What the route is mapped under.</param>
    /// <param name="Segment">The stack's or surface's resource segment, or the service's route.</param>
    internal sealed record ProjectedRouteMetadata(RouteContract Route, ProjectedRouteKind Kind, string Segment)
    {
        /// <summary>
        ///     The value of the request's resource tag: the stack's or surface's resource, or the
        ///     service's route for a routed service, which has no resource.
        /// </summary>
        public string TagResource => Route.Resource ?? Segment;
    }
}
