// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Identity.Hosting
{
    /// <summary>Endpoint mapping for the identity engine.</summary>
    public static class TellmaIdentityEndpointRouteBuilderExtensions
    {
        /// <summary>
        ///     Maps the engine's controllers and Razor Pages. This calls
        ///     <c>MapControllers</c>/<c>MapRazorPages</c>, which map every registered application
        ///     part — an in-proc host that uses MVC itself must therefore call this once
        ///     <em>instead of</em> its own mapping calls, or its routes would be registered twice.
        /// </summary>
        /// <param name="endpoints">The endpoint route builder.</param>
        /// <returns>The endpoint route builder, for chaining.</returns>
        public static IEndpointRouteBuilder MapTellmaIdentity(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            endpoints.MapControllers();
            endpoints.MapRazorPages();
            return endpoints;
        }
    }
}
