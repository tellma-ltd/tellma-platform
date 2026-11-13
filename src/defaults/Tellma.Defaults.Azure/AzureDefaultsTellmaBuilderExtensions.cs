// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Connector.AcsEmail.Adapter;
using Tellma.Connector.SendGrid.Adapter;
using Tellma.Core.Composition;
using Tellma.Core.Email;

namespace Tellma.Defaults.Azure
{
    /// <summary>
    ///     The Azure deployment's common composition: what both of a distribution's hosts, the web
    ///     host and the migrator, compose when the distribution runs on Azure.
    /// </summary>
    public static class AzureDefaultsTellmaBuilderExtensions
    {
        /// <summary>
        ///     Composes the Azure defaults: the email pipeline, with the Azure Communication Services
        ///     (<c>acs-email</c>) and SendGrid (<c>sendgrid</c>) transports registered beside the
        ///     Development log sink, and the sender chosen by <c>Email:Provider</c>. Calling it again
        ///     changes nothing.
        /// </summary>
        /// <remarks>
        ///     Called from the distribution's composition method, which both hosts run, so the
        ///     migrator and the web host compose the same services. A distribution that wants a
        ///     different sender composes the email pipeline itself instead.
        /// </remarks>
        /// <param name="tellma">The composition builder.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TellmaBuilder UseAzureDefaults(this TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);

            // A transport registered twice would be a duplicate name, which the email pipeline
            // refuses at startup; the marker keeps a second call harmless instead.
            IServiceCollection services = tellma.Services;
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(AzureDefaultsMarker)))
            {
                return tellma;
            }

            _ = services.AddSingleton<AzureDefaultsMarker>();

            // --- Email: the pipeline, then the two Azure-hosted transports it selects between ---
            _ = services.AddTellmaEmail();
            _ = services.AddAcsEmail(tellma.Configuration);
            _ = services.AddSendGridEmail(tellma.Configuration);

            return tellma;
        }
    }

    /// <summary>Marks a service collection the Azure defaults were composed into.</summary>
    internal sealed class AzureDefaultsMarker
    {
    }
}
