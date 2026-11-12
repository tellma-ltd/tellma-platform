// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.AspNetCore.Routing;

namespace Tellma.Core.AspNetCore
{
    /// <summary>The metadata a distribution stamps on the endpoints it maps by hand.</summary>
    public static class TellmaEndpointConventions
    {
        /// <summary>
        ///     Marks an endpoint that serves no tenant; required on every endpoint a distribution maps
        ///     outside a tenant group.
        /// </summary>
        /// <typeparam name="TBuilder">The convention builder's type.</typeparam>
        /// <param name="builder">The endpoint.</param>
        /// <param name="reason">Why the endpoint serves no tenant.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TBuilder AsTenantlessEndpoint<TBuilder>(this TBuilder builder, string reason)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            TenantlessEndpointMetadata metadata = new(reason);
            builder.Add(endpoint => endpoint.Metadata.Add(metadata));
            return builder;
        }

        /// <summary>
        ///     States whether a hand-mapped tenant endpoint changes data. Every hand-mapped tenant
        ///     endpoint but the hub's states it, or startup fails. A hand-mapped endpoint is never
        ///     allowed on a <c>ReadOnly</c> tenant when it mutates, so this also clears any read-only
        ///     allowance the endpoint carried.
        /// </summary>
        /// <typeparam name="TBuilder">The convention builder's type.</typeparam>
        /// <param name="builder">The endpoint, on a tenant group.</param>
        /// <param name="isMutation">Whether the endpoint changes data.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TBuilder WithMutation<TBuilder>(this TBuilder builder, bool isMutation)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.Add(endpoint =>
            {
                // The group stamps its metadata in a final convention, after this one runs, so the
                // stamp waits for it; metadata already present (stamped by hand) is amended in place.
                TenantEndpointMetadata? stamped = endpoint.Metadata.OfType<TenantEndpointMetadata>().LastOrDefault();
                endpoint.Metadata.Add(stamped is null ? new MutationStamp(isMutation) : stamped with { IsMutation = isMutation, AllowWhenReadOnly = false });
            });
            return builder;
        }

        /// <summary>Requires a securable action of the caller on a hand-mapped tenant endpoint.</summary>
        /// <typeparam name="TBuilder">The convention builder's type.</typeparam>
        /// <param name="builder">The endpoint, on a tenant group.</param>
        /// <param name="resource">The securable resource.</param>
        /// <param name="action">The action on it.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TBuilder RequireSecurable<TBuilder>(this TBuilder builder, string resource, string action)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            SecurableEndpointMetadata metadata = new(resource, action);
            builder.Add(endpoint => endpoint.Metadata.Add(metadata));
            return builder;
        }

        /// <summary>Lets any active member of the tenant call a hand-mapped tenant endpoint, with no securable.</summary>
        /// <typeparam name="TBuilder">The convention builder's type.</typeparam>
        /// <param name="builder">The endpoint, on a tenant group.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TBuilder AllowMember<TBuilder>(this TBuilder builder)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            MemberEndpointMetadata metadata = new();
            builder.Add(endpoint => endpoint.Metadata.Add(metadata));
            return builder;
        }

        /// <summary>Lets an endpoint accept a body of any declared media type up to a size.</summary>
        /// <typeparam name="TBuilder">The convention builder's type.</typeparam>
        /// <param name="builder">The endpoint.</param>
        /// <param name="maxBytes">The largest body accepted, in bytes.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TBuilder AcceptsBinary<TBuilder>(this TBuilder builder, long maxBytes)
            where TBuilder : IEndpointConventionBuilder
        {
            ArgumentNullException.ThrowIfNull(builder);
            AcceptsBinaryMetadata metadata = new(maxBytes);
            builder.Add(endpoint => endpoint.Metadata.Add(metadata));
            return builder;
        }
    }
}
