// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Communication.Email;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Webhooks;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>The activation entry point of the ACS Email transport.</summary>
    public static class AcsEmailServiceCollectionExtensions
    {
        /// <summary>The configuration name that selects this transport.</summary>
        public const string TransportName = "acs-email";

        /// <summary>
        ///     Declares the <c>acs-email</c> transport to the email pipeline, and registers its Event
        ///     Grid receiver when subscription tokens are configured.
        /// </summary>
        /// <remarks>
        ///     The transport declares no sandbox channel: ACS has no validate-only mode, so the
        ///     pipeline withholds a sandbox tenant's external mail itself. Authentication uses a
        ///     <c>TokenCredential</c> resolved from the container, defaulting to
        ///     <see cref="DefaultAzureCredential" /> — a host that needs a specific identity registers
        ///     its own and wins.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The host's configuration, from which the
        ///     <c>Email:AcsEmail</c> section is read.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddAcsEmail(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return services.AddAcsEmail(configuration.GetSection(AcsEmailOptions.SectionName));
        }

        /// <summary>
        ///     Declares the <c>acs-email</c> transport, reading its settings from a section the host
        ///     names — for a composition that relocates the email configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="section">The section corresponding to <c>Email:AcsEmail</c>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddAcsEmail(this IServiceCollection services, IConfigurationSection section)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(section);

            // Deliberately no ValidateOnStart: only the active transport is warmed.
            services.AddOptions<AcsEmailOptions>().Bind(section);
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<AcsEmailOptions>, AcsEmailOptionsValidator>());

            // TryAdd, so a host that registered a workload or user-assigned identity keeps it.
            services.TryAddSingleton<TokenCredential>(static _ => new DefaultAzureCredential());

            // No sandbox factory: ACS has no validate-only mode, so the router withholds a sandbox
            // tenant's external mail rather than pretending to send it.
            services.AddSingleton(
                new EmailTransportRegistration(TransportName, Create, Sandbox: null));

            string[] tokens = ReadTokens(section);
            if (tokens.Length > 0)
            {
                // Read at composition, like the transport's sandbox channel: whether the endpoint
                // exists at all is a registration-time fact, so adding the first token needs a
                // restart. Captured here rather than resolved from options so a deployment draining
                // events after migrating away is not forced to satisfy the send-side validation.
                //
                // This adapter emits no email instruments of its own: ACS stamps no deployment
                // envelope, because each deployment's events return only to its own subscription on
                // its own resource, so there are no foreign events for it to drop and meter.
                services.AddScoped<IWebhookReceiver>(sp => new AcsEmailEventsWebhookReceiver(
                    tokens,
                    sp.GetRequiredService<IEmailDeliveryEventDispatcher>(),
                    sp.GetRequiredService<ILogger<AcsEmailEventsWebhookReceiver>>()));
            }

            return services;
        }

        private static string[] ReadTokens(IConfigurationSection section)
        {
            return [.. section
                .GetSection($"{nameof(AcsEmailOptions.Webhook)}:{nameof(AcsEmailWebhookOptions.Tokens)}")
                .GetChildren()
                .Select(static child => child.Value)
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value!)];
        }

        /// <summary>
        ///     Builds the Azure client options this transport insists on, so the suite can pin them
        ///     against the same code a deployment runs rather than against a copy.
        /// </summary>
        /// <param name="options">The transport's options.</param>
        /// <returns>The client options.</returns>
        internal static EmailClientOptions BuildClientOptions(AcsEmailOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            EmailClientOptions clientOptions = new();

            // The single most important line in this adapter: Azure.Core otherwise retries 429 and
            // 5xx three times with backoff, which is exactly the durable retry the contract reserves
            // for the caller — and which would silently duplicate mail on an ambiguous failure.
            clientOptions.Retry.MaxRetries = 0;
            clientOptions.Retry.NetworkTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

            return clientOptions;
        }

        private static AcsEmailSender Create(IServiceProvider services)
        {
            TokenCredential credential = services.GetRequiredService<TokenCredential>();
            IOptionsMonitor<AcsEmailOptions> options = services.GetRequiredService<IOptionsMonitor<AcsEmailOptions>>();

            // Reading the options here is what makes this adapter's IValidateOptions run inside the
            // startup gate, which warms the active transport, rather than on the first send — a
            // misconfigured deployment must not pass its health checks. An inactive adapter's
            // factory is never invoked, so its configuration is still never validated.
            _ = options.CurrentValue;

            return new AcsEmailSender(
                options,
                current => BuildClient(current, credential),
                services.GetRequiredService<ILogger<AcsEmailSender>>());
        }

        private static EmailClient BuildClient(AcsEmailOptions options, TokenCredential credential)
        {
            Uri endpoint = options.Endpoint
                ?? throw new InvalidOperationException(
                    $"{AcsEmailOptions.SectionName}:{nameof(AcsEmailOptions.Endpoint)} is not configured.");

            return new EmailClient(endpoint, credential, BuildClientOptions(options));
        }
    }
}
