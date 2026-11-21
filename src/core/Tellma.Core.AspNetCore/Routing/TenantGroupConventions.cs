// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>The final convention of a tenant group, which stamps each endpoint's tenant metadata.</summary>
    internal static class TenantGroupConventions
    {
        /// <summary>
        ///     Stamps <see cref="TenantEndpointMetadata" /> on an endpoint of a tenant group that carries
        ///     none: the group's surface, the mutation flag <c>WithMutation</c> stamped, and no read-only
        ///     allowance. A hub endpoint is always a read, as its front door asks; any other endpoint
        ///     that stated nothing is stamped a mutation and marked unstated, which the audit refuses.
        /// </summary>
        /// <param name="endpoint">The endpoint being built.</param>
        /// <param name="surface">The group's surface.</param>
        public static void Complete(EndpointBuilder endpoint, TenantSurface surface)
        {
            ArgumentNullException.ThrowIfNull(endpoint);

            // Metadata a projection or the endpoint itself stamped is the endpoint's own statement.
            if (endpoint.Metadata.OfType<TenantEndpointMetadata>().Any())
            {
                return;
            }

            // --- The hub: its connections never write, whatever method SignalR maps them with ---
            if (surface == TenantSurface.Hub)
            {
                endpoint.Metadata.Add(new TenantEndpointMetadata(surface, IsMutation: false, AllowWhenReadOnly: false));
                return;
            }

            // --- Every other surface: what WithMutation stated, or a refused, fail-safe mutation ---
            if (endpoint.Metadata.OfType<MutationStamp>().LastOrDefault() is { } stamp)
            {
                endpoint.Metadata.Add(new TenantEndpointMetadata(surface, stamp.IsMutation, AllowWhenReadOnly: false));
            }
            else
            {
                endpoint.Metadata.Add(new TenantEndpointMetadata(surface, IsMutation: true, AllowWhenReadOnly: false));
                endpoint.Metadata.Add(MutationUnstatedMetadata.Instance);
            }
        }
    }
}
