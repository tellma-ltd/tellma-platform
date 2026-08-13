// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Email
{
    /// <summary>The activation entry point of the email pipeline.</summary>
    public static class TellmaEmailServiceCollectionExtensions
    {
        /// <summary>
        ///     Registers the email pipeline: the options bound from the <c>Email</c> section and
        ///     validated at startup, the sandbox router as the composition's sole
        ///     <see cref="IEmailSender" />, the Development log-sink transport, the delivery-event
        ///     dispatcher, and the email telemetry.
        /// </summary>
        /// <remarks>
        ///     <para>Connector adapters are added separately, one call each, and declare themselves
        ///     to this pipeline through <see cref="EmailTransportRegistration" />. Two seams the
        ///     composition must also supply, both checked at startup: a
        ///     <c>DeploymentIdentity</c> and an <c>ISandboxContext</c>.</para>
        ///     <para>Calling this more than once is harmless; every registration is idempotent.</para>
        /// </remarks>
        /// <param name="services">The service collection.</param>
        /// <returns>The service collection, for chaining.</returns>
        public static IServiceCollection AddTellmaEmail(this IServiceCollection services)
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddOptions<EmailOptions>()
                .BindConfiguration(EmailOptions.SectionName)
                .ValidateOnStart();

            // TryAddEnumerable so a second AddTellmaEmail() does not double-report every failure.
            services.TryAddEnumerable(
                ServiceDescriptor.Singleton<IValidateOptions<EmailOptions>, EmailOptionsValidator>());

            // AddMetrics is idempotent and is what supplies the IMeterFactory the instruments are
            // created from; a meter built any other way is invisible to collectors and exporters.
            services.AddMetrics();
            services.TryAddSingleton(TimeProvider.System);

            services.TryAddSingleton<EmailMetrics>();
            services.TryAddSingleton<EmailTransportSelector>();

            // Scoped, because it consults the ambient ISandboxContext. This single registration is
            // what guarantees no send path bypasses the sandbox policy.
            services.TryAddScoped<IEmailSender, EmailRouter>();
            services.TryAddScoped<IEmailDeliveryEventDispatcher, EmailDeliveryEventDispatcher>();

            // The Development default, so a fresh clone sends mail with no configuration at all. Its
            // sandbox factory is the same sink on the sandbox channel: in development, a sandbox
            // tenant's external mail is worth seeing too.
            //
            // Added by hand rather than through TryAddEnumerable, which cannot tell two transport
            // registrations apart (they share both service and implementation type) and would
            // therefore swallow every adapter's registration after the first.
            if (!services.Any(IsLogSinkRegistration))
            {
                services.AddSingleton(
                    new EmailTransportRegistration(
                        EmailTransportResolution.LogSinkTransportName,
                        static sp => CreateLogSink(sp, EmailChannel.Live),
                        static sp => CreateLogSink(sp, EmailChannel.Sandbox)));
            }

            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, EmailStartupValidator>());

            return services;
        }

        private static bool IsLogSinkRegistration(ServiceDescriptor descriptor)
        {
            return descriptor.ServiceType == typeof(EmailTransportRegistration)
                && descriptor.ImplementationInstance is EmailTransportRegistration registration
                && string.Equals(
                    registration.Name,
                    EmailTransportResolution.LogSinkTransportName,
                    StringComparison.OrdinalIgnoreCase);
        }

        private static LogSinkEmailSender CreateLogSink(IServiceProvider services, EmailChannel channel)
        {
            return new LogSinkEmailSender(
                services.GetRequiredService<IHostEnvironment>(),
                services.GetRequiredService<ILogger<LogSinkEmailSender>>(),
                channel);
        }
    }
}
