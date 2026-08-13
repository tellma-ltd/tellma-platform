// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>The activation entry point of the SendGrid transport.</summary>
    public static class SendGridEmailServiceCollectionExtensions
    {
        /// <summary>The configuration name that selects this transport.</summary>
        public const string TransportName = "sendgrid";

        /// <summary>
        ///     Declares the <c>sendgrid</c> transport to the email pipeline, and registers its
        ///     delivery-event receiver when verification keys are configured.
        /// </summary>
        /// <remarks>
        ///     The receiver's presence is deliberately independent of <c>Email:Provider</c>: a
        ///     deployment that has migrated to another transport keeps draining the tail of its
        ///     in-flight SendGrid events. Its presence is decided here rather than at runtime because
        ///     an endpoint that exists but always answers 401 is worse than one that is not there,
        ///     and because configuration is only readable at composition — so adding the first
        ///     verification key takes a restart.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The host's configuration, from which the
        ///     <c>Email:SendGrid</c> section is read.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddSendGridEmail(
            this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return services.AddSendGridEmail(configuration.GetSection(SendGridEmailOptions.SectionName));
        }

        /// <summary>
        ///     Declares the <c>sendgrid</c> transport, reading its settings from a section the host
        ///     names — for a composition that relocates the email configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="section">The section corresponding to <c>Email:SendGrid</c>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddSendGridEmail(
            this IServiceCollection services, IConfigurationSection section)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(section);

            // Deliberately no ValidateOnStart: only the active transport is warmed, so an adapter
            // that is compiled in but not selected must not demand configuration.
            services.AddOptions<SendGridEmailOptions>().Bind(section);
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<SendGridEmailOptions>, SendGridEmailOptionsValidator>());

            // No resilience handler by design: durable retry policy belongs to the caller, and a
            // retry here would silently duplicate mail the caller believes failed.
            services.AddHttpClient(SendGridEmailSender.HttpClientName);

            services.AddSingleton(new EmailTransportRegistration(
                TransportName,
                static sp => Create(sp, SendGridChannel.Live),
                // Always present: the sandbox channel is a per-request provider mode on the same
                // credentials, so it needs no configuration of its own.
                static sp => Create(sp, SendGridChannel.Sandbox)));

            string[] verificationKeys = ReadVerificationKeys(section);
            if (verificationKeys.Length > 0)
            {
                // Built here, from the keys as configured, rather than from the bound options: a
                // deployment draining events after migrating away from SendGrid has webhook keys and
                // no API key, and must not be forced to satisfy the send-side validation. A malformed
                // key throws right here, which is where an operator can still see it.
                services.AddSingleton(new SendGridWebhookVerifier(verificationKeys));
                services.TryAddSingleton<SendGridEmailMetrics>();
                services.AddScoped<IWebhookReceiver, SendGridEventsWebhookReceiver>();
            }

            return services;
        }

        private static string[] ReadVerificationKeys(IConfigurationSection section)
        {
            return [.. section
                .GetSection($"{nameof(SendGridEmailOptions.Webhook)}:{nameof(SendGridWebhookOptions.VerificationKeys)}")
                .GetChildren()
                .Select(static child => child.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)];
        }

        private static SendGridEmailSender Create(IServiceProvider services, SendGridChannel channel)
        {
            IOptionsMonitor<SendGridEmailOptions> options =
                services.GetRequiredService<IOptionsMonitor<SendGridEmailOptions>>();

            // Reading the options here is what makes this adapter's IValidateOptions run inside the
            // startup gate, which warms the active transport, rather than on the first send — a
            // misconfigured deployment must not pass its health checks. An inactive adapter's
            // factory is never invoked, so its configuration is still never validated.
            _ = options.CurrentValue;

            return new SendGridEmailSender(
                channel,
                options,
                services.GetRequiredService<IHttpClientFactory>(),
                services.GetRequiredService<DeploymentIdentity>(),
                services.GetRequiredService<ILogger<SendGridEmailSender>>());
        }
    }
}
