// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.AspNetCore.Observability;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Sessions
{
    /// <summary>
    ///     The requests a user did not make by acting: polling and refreshes that say so, the realtime
    ///     hub's negotiate and connection, and the client's telemetry. They renew neither session cookie,
    ///     so an idle tab's session expires when its user's would.
    /// </summary>
    internal static class BackgroundRequests
    {
        /// <summary>The <c>Tellma-Activity</c> value of a background request.</summary>
        public const string BackgroundActivity = "background";

        /// <summary>Whether a request is background activity.</summary>
        /// <param name="context">The request, past routing.</param>
        /// <returns>True when the request renews no session.</returns>
        public static bool IsBackground(HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            Endpoint? endpoint = context.GetEndpoint();

            // --- Any request on the hub's group: its negotiate and its connection ---
            if (endpoint?.Metadata.GetMetadata<TellmaGroupMetadata>()?.Group == TellmaGroup.Hub
                || endpoint?.Metadata.GetMetadata<TenantEndpointMetadata>()?.Surface == TenantSurface.Hub)
            {
                return true;
            }

            // --- The client's telemetry relay ---
            if (HttpMethods.IsPost(context.Request.Method)
                && context.Request.Path.Equals(ObservabilityComposition.ClientTelemetryPath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // --- A request that says it is background, unless it changes data, where the header is ignored ---
            return context.Request.Headers[TellmaHeaders.Activity] is [string activity]
                && string.Equals(activity, BackgroundActivity, StringComparison.OrdinalIgnoreCase)
                && endpoint?.Metadata.GetMetadata<TenantEndpointMetadata>() is not { IsMutation: true };
        }
    }
}
