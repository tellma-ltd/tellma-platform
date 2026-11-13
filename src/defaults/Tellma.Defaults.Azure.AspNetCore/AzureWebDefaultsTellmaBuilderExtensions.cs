// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.AspNetCore.Routing;
using Tellma.Core.Composition;
using Tellma.Core.Webhooks;

namespace Tellma.Defaults.Azure.AspNetCore
{
    /// <summary>
    ///     The Azure web host's half of the defaults: what only a distribution's web host composes when
    ///     the distribution runs on Azure.
    /// </summary>
    public static class AzureWebDefaultsTellmaBuilderExtensions
    {
        /// <summary>The configuration key of the Application Insights connection string.</summary>
        public const string ApplicationInsightsConnectionStringKey = "APPLICATIONINSIGHTS_CONNECTION_STRING";

        /// <summary>
        ///     Composes the Azure web defaults: the webhook endpoint at <c>/api/webhooks/{key}</c>, mapped
        ///     by <c>MapTellma</c> as one of the platform's tenantless routes; the Data Protection key
        ///     ring in Azure Blob storage under a Key Vault key when <c>Tellma:DataProtection:BlobUri</c>
        ///     and <c>KeyId</c> are set; and the Azure Monitor exporter for logs, traces and metrics when
        ///     <c>APPLICATIONINSIGHTS_CONNECTION_STRING</c> is set. Calling it again changes nothing.
        /// </summary>
        /// <remarks>
        ///     Called from the web host's <c>composeWeb</c> delegate, never from the composition method
        ///     the migrator shares. The key ring and Key Vault are reached with the host's
        ///     <see cref="TokenCredential" />, a <see cref="DefaultAzureCredential" /> unless the host
        ///     registered another.
        /// </remarks>
        /// <param name="tellma">The composition builder.</param>
        /// <returns>The builder, for chaining.</returns>
        public static TellmaBuilder UseAzureWebDefaults(this TellmaBuilder tellma)
        {
            ArgumentNullException.ThrowIfNull(tellma);

            // A second call would add a second key-ring configuration and a second exporter, which
            // would send every signal twice; the marker keeps it harmless instead.
            IServiceCollection services = tellma.Services;
            if (services.Any(static descriptor => descriptor.ServiceType == typeof(AzureWebDefaultsMarker)))
            {
                return tellma;
            }

            _ = services.AddSingleton<AzureWebDefaultsMarker>();

            // --- Webhooks: the receivers' fronting, mapped among the platform's own routes ---
            _ = services.AddTellmaWebhooks();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IPlatformEndpointSource, WebhookEndpointSource>());

            // --- The host's credential, which every Azure resource below is reached with ---
            // TryAdd, so a host that registered a workload or user-assigned identity keeps it.
            services.TryAddSingleton<TokenCredential>(static _ => new DefaultAzureCredential());

            // --- Data Protection: the key ring in a blob, wrapped by a Key Vault key ---
            AzureKeyRing.Register(services, tellma.Configuration);

            // --- Azure Monitor: the exporter alone, behind the host's own instrumentation ---
            if (tellma.Configuration[ApplicationInsightsConnectionStringKey] is { } connectionString
                && !string.IsNullOrWhiteSpace(connectionString))
            {
                _ = services.AddOpenTelemetry().UseAzureMonitorExporter(exporter => exporter.ConnectionString = connectionString);
            }

            return tellma;
        }
    }

    /// <summary>Marks a service collection the Azure web defaults were composed into.</summary>
    internal sealed class AzureWebDefaultsMarker
    {
    }
}
