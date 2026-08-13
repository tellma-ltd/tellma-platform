// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Tellma.Core.Webhooks
{
    /// <summary>The HTTP entry point of the webhook fronting.</summary>
    public static class TellmaWebhooksEndpointRouteBuilderExtensions
    {
        /// <summary>
        ///     Maps <c>/api/webhooks/{key}</c> for GET and POST, so no connector ever writes a
        ///     controller of its own.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         The endpoint allows anonymous access — a webhook caller holds no platform
        ///         credential, and the receiver's own signature verification is the authentication —
        ///         and is excluded from antiforgery for the same reason. It carries
        ///         <see cref="WebhookEndpointMetadata" /> so a host's tenant-resolution middleware can
        ///         recognize and skip it.
        ///     </para>
        ///     <para>
        ///         The route shape is a platform contract: operators configure provider dashboards
        ///         against it, so it must stay stable across releases, and a receiver key is part of
        ///         its connector's public surface.
        ///     </para>
        /// </remarks>
        /// <param name="endpoints">The endpoint route builder.</param>
        /// <returns>The endpoint convention builder, for further configuration.</returns>
        /// <exception cref="InvalidOperationException">The services the fronting needs were not
        ///     registered.</exception>
        public static IEndpointConventionBuilder MapTellmaWebhooks(this IEndpointRouteBuilder endpoints)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            return endpoints.ServiceProvider.GetService<IOptions<WebhookOptions>>() is null
                || endpoints.ServiceProvider.GetService<WebhookMetrics>() is null
                ? throw new InvalidOperationException(
                    $"The webhook fronting is not registered. Call services.{nameof(TellmaWebhooksServiceCollectionExtensions.AddTellmaWebhooks)}() before mapping the endpoint.")
                : endpoints
                .MapMethods(WebhookEndpoint.RoutePattern, [HttpMethods.Get, HttpMethods.Post], WebhookEndpoint.HandleAsync)
                .AllowAnonymous()
                .DisableAntiforgery()
                .WithMetadata(new WebhookEndpointMetadata())
                .ExcludeFromDescription();
        }
    }
}
