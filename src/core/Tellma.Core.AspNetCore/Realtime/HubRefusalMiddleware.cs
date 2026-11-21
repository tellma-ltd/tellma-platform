// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;

namespace Tellma.Core.AspNetCore.Realtime
{
    /// <summary>
    ///     Counts the hub's connection attempts the pipeline refuses before the hub's front door: an
    ///     expired or missing session answered 401 by authentication, and a 403 from authorization or
    ///     the CSRF control, such as a cross-origin negotiate. They are the commonest refusals of a
    ///     stale client and of a probe, so the refused-connections alert must see them.
    /// </summary>
    /// <remarks>
    ///     It runs after routing, so the endpoint is known, and ahead of authentication. A request to
    ///     any other endpoint passes through untouched.
    /// </remarks>
    /// <param name="next">The rest of the pipeline.</param>
    /// <param name="door">The hub's front door, which counts and logs the refusal.</param>
    internal sealed class HubRefusalMiddleware(RequestDelegate next, HubFrontDoor door)
    {
        /// <summary>Runs the rest of the pipeline, then counts a hub request it answered 401 or 403.</summary>
        /// <param name="context">The request.</param>
        /// <returns>A task that completes when the request is answered.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            await next(context).ConfigureAwait(false);

            // A refusal that throws, the door's own, never reaches this line: the door counted it.
            if (context.GetEndpoint()?.Metadata.GetMetadata<TenantEndpointMetadata>()?.Surface == TenantSurface.Hub)
            {
                door.Answered(context);
            }
        }
    }
}
