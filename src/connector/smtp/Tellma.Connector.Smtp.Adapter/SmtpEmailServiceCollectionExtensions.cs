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

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>The activation entry point of the SMTP transport.</summary>
    public static class SmtpEmailServiceCollectionExtensions
    {
        /// <summary>
        ///     Declares the <c>smtp</c> transport to the email pipeline, which activates it when
        ///     <c>Email:Provider</c> names it and leaves it cold otherwise.
        /// </summary>
        /// <remarks>
        ///     Configuration is read here, not just bound, because whether the transport has a
        ///     sandbox channel at all is a composition-time fact: the pipeline needs to know at
        ///     registration whether to route a sandbox tenant's external mail to a trap or withhold
        ///     it. Adding or removing the <c>Email:Smtp:Sandbox</c> section therefore takes a
        ///     restart; every other setting is re-read per batch.
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <param name="configuration">The host's configuration, from which the <c>Email:Smtp</c>
        ///     section is read.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddSmtpEmail(this IServiceCollection services, IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);

            return services.AddSmtpEmail(configuration.GetSection(SmtpEmailOptions.SectionName));
        }

        /// <summary>
        ///     Declares the <c>smtp</c> transport, reading its settings from a section the host
        ///     names — for a composition that relocates the email configuration.
        /// </summary>
        /// <param name="services">The service collection.</param>
        /// <param name="section">The section corresponding to <c>Email:Smtp</c>.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddSmtpEmail(this IServiceCollection services, IConfigurationSection section)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(section);

            // Deliberately no ValidateOnStart: the pipeline warms only the active transport, which
            // is what lets a distribution compile in every adapter and configure just one.
            services.AddOptions<SmtpEmailOptions>().Bind(section);
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<SmtpEmailOptions>, SmtpEmailOptionsValidator>());
            services.TryAddSingleton<ISmtpClientFactory, SmtpClientFactory>();

            bool hasSandbox = section.GetSection(nameof(SmtpEmailOptions.Sandbox)).Exists();

            services.AddSingleton(new EmailTransportRegistration(
                TransportName,
                static sp => Create(sp, SmtpChannel.Live),
                hasSandbox ? static sp => Create(sp, SmtpChannel.Sandbox) : null));

            return services;
        }

        /// <summary>The configuration name that selects this transport.</summary>
        public const string TransportName = "smtp";

        private static SmtpEmailSender Create(IServiceProvider services, SmtpChannel channel)
        {
            IOptionsMonitor<SmtpEmailOptions> options =
                services.GetRequiredService<IOptionsMonitor<SmtpEmailOptions>>();

            // Reading the options here is what makes this adapter's IValidateOptions run inside the
            // startup gate, which warms the active transport, rather than on the first send — a
            // misconfigured deployment must not pass its health checks. An inactive adapter's
            // factory is never invoked, so its configuration is still never validated.
            _ = options.CurrentValue;

            return new SmtpEmailSender(
                channel,
                options,
                services.GetRequiredService<ISmtpClientFactory>(),
                services.GetRequiredService<ILogger<SmtpEmailSender>>());
        }
    }
}
