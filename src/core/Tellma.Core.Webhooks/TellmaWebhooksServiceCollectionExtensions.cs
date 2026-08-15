// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Tellma.Core.Webhooks
{
    /// <summary>The service-side entry point of the webhook fronting.</summary>
    public static class TellmaWebhooksServiceCollectionExtensions
    {
        /// <summary>
        ///     Registers what the webhook fronting needs at runtime: the options bound from the
        ///     <c>Webhooks</c> section, the request instruments, and the startup check on receiver
        ///     keys. The endpoint itself is mapped separately, with <c>MapTellmaWebhooks</c>.
        /// </summary>
        /// <remarks>
        ///     Receivers are registered by their own connector adapters as
        ///     <c>IWebhookReceiver</c>; this package never knows what they are.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddTellmaWebhooks(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            // ValidateOnStart, unlike an adapter's options: this section belongs to the fronting
            // itself, so it is always in play whenever the fronting is registered at all.
            services.AddOptions<WebhookOptions>()
                .BindConfiguration(WebhookOptions.SectionName)
                .ValidateOnStart();
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<WebhookOptions>, WebhookOptionsValidator>());

            services.AddMetrics();
            services.TryAddSingleton<WebhookMetrics>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, WebhookStartupValidator>());

            return services;
        }
    }
}
