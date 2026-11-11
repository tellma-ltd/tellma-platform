// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore.Routing
{
    /// <summary>The final convention of a tenant group, which stamps each endpoint's tenant metadata.</summary>
    internal static class TenantGroupConventions
    {
        /// <summary>
        ///     Stamps <see cref="TenantEndpointMetadata" /> on an endpoint of a tenant group that carries
        ///     none: the group's surface, the mutation flag <c>WithMutation</c> stamped or, failing that,
        ///     the one its methods imply, and no read-only allowance.
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

            bool isMutation = endpoint.Metadata.OfType<MutationStamp>().LastOrDefault() is { } stamp
                ? stamp.IsMutation
                : MethodsMutate(endpoint.Metadata.OfType<HttpMethodMetadata>().LastOrDefault());
            endpoint.Metadata.Add(new TenantEndpointMetadata(surface, isMutation, AllowWhenReadOnly: false));
        }

        /// <summary>
        ///     Whether an endpoint's methods may change data: anything but <c>GET</c>, <c>HEAD</c>,
        ///     <c>OPTIONS</c> and <c>TRACE</c>, or any method at all when the endpoint names none. An
        ///     unstated endpoint is a mutation, so a read-only tenant refuses it.
        /// </summary>
        /// <param name="methods">The endpoint's method metadata, or null.</param>
        /// <returns>True when the endpoint may change data.</returns>
        public static bool MethodsMutate(HttpMethodMetadata? methods)
        {
            return methods is null
                || methods.HttpMethods.Count == 0
                || methods.HttpMethods.Any(static method => !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method)
                    && !HttpMethods.IsOptions(method) && !HttpMethods.IsTrace(method));
        }
    }
}
