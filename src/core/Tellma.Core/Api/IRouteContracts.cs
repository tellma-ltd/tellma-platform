// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Api
{
    /// <summary>
    ///     The route model of the web surface: one <see cref="RouteContract" /> per projected endpoint,
    ///     derived once from the stack registry. The endpoint projection maps exactly these routes and
    ///     the client metadata's contracts are written from them, so nothing else derives which
    ///     routes exist. A singleton over the realised composition.
    /// </summary>
    public interface IRouteContracts
    {
        /// <summary>
        ///     Every projected endpoint, in mapping order: each stack's standard operations then its
        ///     actions, each child query surface's query then its actions, then each routed service's
        ///     actions, in registration order.
        /// </summary>
        IReadOnlyList<RouteContract> Routes { get; }

        /// <summary>Finds the contract of one route.</summary>
        /// <param name="routeTemplate">The route's template, such as <c>POST /{tenantId}/api/web/centers/query</c>; compared ordinally.</param>
        /// <returns>The contract, or null for a route the projection does not map.</returns>
        [SuppressMessage(
            "Naming",
            "CA1716:Identifiers should not match keywords",
            Justification = "For is the route model's established lookup name; the platform's implementers are C#, where for is no member name.")]
        RouteContract? For(string routeTemplate);
    }
}
