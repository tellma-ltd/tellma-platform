// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.AspNetCore.Tenancy
{
    /// <summary>
    ///     The access guard's front door on every tenant group but the hub: it decides, before the
    ///     handler runs, whether the bound principal may proceed against the bound tenant, then applies
    ///     the context's cultures to the handler.
    /// </summary>
    internal sealed class TenantAccessFilter : IEndpointFilter
    {
        /// <summary>The request item that records the guard admitted the request.</summary>
        private static readonly object GuardedKey = new();

        /// <inheritdoc />
        public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(next);
            await EnsureAsync(context.HttpContext).ConfigureAwait(false);

            // Applied here, not in the awaited guard, whose changes to the cultures end with it.
            ApplyCultures(context.HttpContext);
            return await next(context).ConfigureAwait(false);
        }

        /// <summary>
        ///     Wraps a guarded endpoint's request delegate so the guard runs before it whatever built the
        ///     endpoint: endpoint filters reach only route handlers, and an endpoint a data source maps
        ///     onto a group by other means would otherwise run unguarded.
        /// </summary>
        /// <param name="endpoint">The endpoint being built, after its request delegate is set.</param>
        public static void Wrap(EndpointBuilder endpoint)
        {
            ArgumentNullException.ThrowIfNull(endpoint);
            RequestDelegate? inner = endpoint.RequestDelegate;
            if (inner is null)
            {
                return;
            }

            endpoint.RequestDelegate = async context =>
            {
                await EnsureAsync(context).ConfigureAwait(false);
                ApplyCultures(context);
                await inner(context).ConfigureAwait(false);
            };
        }

        /// <summary>Runs the guard for a request, once.</summary>
        /// <param name="http">The request.</param>
        /// <returns>A task that completes when the request is admitted.</returns>
        /// <exception cref="InvalidOperationException">The endpoint carries no tenant metadata.</exception>
        internal static async Task EnsureAsync(HttpContext http)
        {
            if (http.Items.ContainsKey(GuardedKey))
            {
                return;
            }

            // --- The requirement: the endpoint's own statement, and its assurance bar if it has one ---
            Endpoint? endpoint = http.GetEndpoint();
            TenantEndpointMetadata metadata = endpoint?.Metadata.GetMetadata<TenantEndpointMetadata>()
                ?? throw new InvalidOperationException("The tenant access guard ran on an endpoint without tenant metadata.");
            RequireAssuranceMetadata? assurance = endpoint.Metadata.GetMetadata<RequireAssuranceMetadata>();
            TenantAccessRequirement requirement = new(metadata.IsMutation, assurance, metadata.AllowWhenReadOnly);

            // --- The guard of the request's own scope: the filter itself is shared by every request ---
            IServiceProvider services = http.RequestServices;
            await services.GetRequiredService<ITenantAccessGuard>()
                .EnsureAccessAsync(requirement, RequestInputs.Of(http), http.RequestAborted)
                .ConfigureAwait(false);
            http.Items[GuardedKey] = true;
        }

        /// <summary>Applies the request context's cultures and calendar to the caller's flow, for the handler.</summary>
        /// <param name="http">The request.</param>
        private static void ApplyCultures(HttpContext http)
        {
            http.RequestServices.GetRequiredService<IRequestContextAccessor>().Current.ApplyCultures();
        }
    }
}
