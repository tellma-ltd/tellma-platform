// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Composition;
using Tellma.Defaults.Azure.AspNetCore;

namespace Tellma.Defaults.Azure.Tests
{
    /// <summary>
    ///     What the Azure bundles compose: the email transports for both hosts; the webhook endpoint,
    ///     the key ring and the exporter for the web host, each only once and each of the last two only
    ///     when configured.
    /// </summary>
    public sealed class AzureDefaultsTests
    {
        private const string BlobUri = "https://acme.blob.core.windows.net/keys/ring.xml";

        private const string KeyId = "https://acme.vault.azure.net/keys/ring";

        // The blob repository is internal to its package, so it is recognized by name.
        private const string AzureBlobRepository = "AzureBlobXmlRepository";

        [Fact]
        public void The_defaults_register_the_email_pipeline_with_both_Azure_transports_once()
        {
            ServiceCollection services = Compose([], static tellma => tellma.UseAzureDefaults().UseAzureDefaults());

            string[] transports = [.. services
                .Select(static descriptor => descriptor.ImplementationInstance)
                .OfType<EmailTransportRegistration>()
                .Select(static registration => registration.Name)
                .Order(StringComparer.Ordinal)];
            Assert.Equal(["acs-email", "log-sink", "sendgrid"], transports);
            Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(IEmailSender));
        }

        [Fact]
        public void The_web_defaults_contribute_the_webhook_endpoint_once()
        {
            ServiceCollection services = Compose([], static tellma => tellma.UseAzureWebDefaults().UseAzureWebDefaults());

            _ = Assert.Single(services, static descriptor => descriptor.ImplementationType == typeof(WebhookEndpointSource));
        }

        [Fact]
        public void The_host_s_own_credential_is_kept()
        {
            TokenCredential own = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId("00000000-0000-0000-0000-000000000001"));
            ServiceCollection services = Compose([], tellma =>
            {
                _ = tellma.Services.AddSingleton(own);
                _ = tellma.UseAzureWebDefaults();
            });

            using ServiceProvider provider = services.BuildServiceProvider();
            Assert.Same(own, provider.GetRequiredService<TokenCredential>());
        }

        [Fact]
        public void Without_a_credential_the_host_reaches_Azure_as_its_default_identity()
        {
            using ServiceProvider provider = Compose([], static tellma => tellma.UseAzureWebDefaults()).BuildServiceProvider();

            _ = Assert.IsType<DefaultAzureCredential>(provider.GetRequiredService<TokenCredential>());
        }

        [Fact]
        public void A_configured_key_ring_is_kept_in_the_blob_under_the_vault_key()
        {
            using ServiceProvider provider = Compose(
                new() { ["Tellma:DataProtection:BlobUri"] = BlobUri, ["Tellma:DataProtection:KeyId"] = KeyId },
                static tellma => tellma.UseAzureWebDefaults()).BuildServiceProvider();

            KeyManagementOptions keys = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.Equal(AzureBlobRepository, keys.XmlRepository?.GetType().Name);
            Assert.NotNull(keys.XmlEncryptor);
        }

        [Theory]
        [InlineData(null, null)]
        [InlineData(BlobUri, null)]
        [InlineData(null, KeyId)]
        [InlineData("http://acme.blob.core.windows.net/keys/ring.xml", KeyId)]
        public void A_key_ring_that_is_not_configured_whole_is_not_kept_in_Azure(string? blobUri, string? keyId)
        {
            using ServiceProvider provider = Compose(
                new() { ["Tellma:DataProtection:BlobUri"] = blobUri, ["Tellma:DataProtection:KeyId"] = keyId },
                static tellma => tellma.UseAzureWebDefaults()).BuildServiceProvider();

            // The options check refuses the halves at startup; nothing is wired to them meanwhile.
            KeyManagementOptions keys = provider.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
            Assert.NotEqual(AzureBlobRepository, keys.XmlRepository?.GetType().Name);
        }

        [Fact]
        public void The_exporter_is_added_only_with_a_connection_string()
        {
            Assert.False(HasExporter(Compose([], static tellma => tellma.UseAzureWebDefaults())));
            Assert.True(HasExporter(Compose(
                new() { [AzureWebDefaultsTellmaBuilderExtensions.ApplicationInsightsConnectionStringKey] = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example.invalid/" },
                static tellma => tellma.UseAzureWebDefaults())));
        }

        /// <summary>Whether any registration comes from the Azure Monitor exporter's assembly.</summary>
        /// <param name="services">The services.</param>
        /// <returns>True when one does.</returns>
        private static bool HasExporter(ServiceCollection services)
        {
            System.Reflection.Assembly exporter = typeof(AzureMonitorExporterOptions).Assembly;
            return services.Any(descriptor =>
                descriptor.ServiceType.Assembly == exporter
                || descriptor.ImplementationType?.Assembly == exporter
                || descriptor.ImplementationInstance?.GetType().Assembly == exporter
                || descriptor.ImplementationFactory?.Method.DeclaringType?.Assembly == exporter
                || descriptor.ServiceType.GenericTypeArguments.Any(argument => argument.Assembly == exporter));
        }

        /// <summary>Composes a distribution of nothing but the bundles, as a host's composition would.</summary>
        /// <param name="values">The configuration.</param>
        /// <param name="compose">The composition.</param>
        /// <returns>The composed services.</returns>
        private static ServiceCollection Compose(Dictionary<string, string?> values, Action<TellmaBuilder> compose)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            ServiceCollection services = [];
            _ = services.AddSingleton(configuration);
            _ = services.AddTellma("acme", configuration, new SuiteEnvironment(), compose);
            return services;
        }

        /// <summary>A Development host environment over the suite's output.</summary>
        private sealed class SuiteEnvironment : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = Environments.Development;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Defaults.Azure.Tests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
