// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Azure.Identity;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.AspNetCore;
using Tellma.Core.Composition;
using Tellma.Core.Imaging;
using Tellma.Defaults.Azure.AspNetCore;

namespace Tellma.Defaults.Azure.Tests
{
    /// <summary>
    ///     What the Azure bundles compose: the email transports, the blob processor, and the one blob
    ///     store the Azure section and the environment choose, for both hosts; the webhook endpoint,
    ///     the key ring and the exporter for the web host, each only once and each of the last two only
    ///     when configured.
    /// </summary>
    public sealed class AzureDefaultsTests
    {
        private const string BlobUri = "https://acme.blob.core.windows.net/keys/ring.xml";

        private const string KeyId = "https://acme.vault.azure.net/keys/ring";

        // The blob repository is internal to its package, so it is recognized by name.
        private const string AzureBlobRepository = "AzureBlobXmlRepository";

        // The file-system blob store is internal to the runtime, so it is recognized by name.
        private const string FileSystemBlobStore = "FileSystemBlobStore";

        // The Azure blob store and its startup check are internal to their adapter, so they are recognized by name.
        private const string AzureBlobStore = "AzureBlobStore";

        private const string AzureBlobStoreCheck = "blobs.azure";

        private const string AzureServiceUriKey = "Tellma:Blobs:Azure:ServiceUri";

        private const string AzureServiceUri = "https://acme.blob.core.windows.net";

        private const string ConnectionString = "InstrumentationKey=00000000-0000-0000-0000-000000000000;IngestionEndpoint=https://example.invalid/";

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

        [Theory]
        [InlineData("Development", false, FileSystemBlobStore)]
        [InlineData("Production", false, AzureBlobStore)]
        [InlineData("Development", true, AzureBlobStore)]
        [InlineData("Production", true, AzureBlobStore)]
        public void The_defaults_register_the_skia_processor_and_one_store_the_Azure_section_and_the_environment_choose(
            string environment, bool azureSection, string store)
        {
            ServiceCollection services = Compose(
                azureSection ? new() { [AzureServiceUriKey] = AzureServiceUri } : [],
                static tellma => tellma.UseAzureDefaults().UseAzureDefaults(),
                new SuiteEnvironment { EnvironmentName = environment });

            Assert.Equal(typeof(SkiaImageProcessor), Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IImageProcessor)).ImplementationType);
            Assert.Equal(store, Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IBlobStore)).ImplementationType?.Name);
        }

        [Fact]
        public async Task Outside_development_a_missing_Azure_section_fails_the_Azure_store_s_startup_check()
        {
            await using ServiceProvider provider = Compose([], static tellma => tellma.UseAzureDefaults(), new SuiteEnvironment { EnvironmentName = Environments.Production })
                .BuildServiceProvider();

            CompositionProblem problem = Assert.Single(await AzureBlobStoreCheckAsync(provider));

            Assert.StartsWith("Neither Tellma:Blobs:Azure:ServiceUri nor Tellma:Blobs:Azure:ConnectionString is set", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_configured_endpoint_passes_the_Azure_store_s_startup_check_with_the_host_s_identity()
        {
            await using ServiceProvider provider = Compose(
                new() { [AzureServiceUriKey] = AzureServiceUri },
                static tellma => tellma.UseAzureDefaults(),
                new SuiteEnvironment { EnvironmentName = Environments.Production }).BuildServiceProvider();

            Assert.Empty(await AzureBlobStoreCheckAsync(provider));
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
                new() { [AzureWebDefaultsTellmaBuilderExtensions.ApplicationInsightsConnectionStringKey] = ConnectionString },
                static tellma => tellma.UseAzureWebDefaults())));
        }

        [Fact]
        public async Task The_platform_s_sampler_decides_over_the_exporter_s()
        {
            // --- The exporter alone installs a sampler of its own ---
            ServiceCollection alone = [];
            _ = alone.AddOpenTelemetry().WithTracing(static tracing => tracing.SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))).UseAzureMonitorExporter(static exporter => exporter.ConnectionString = ConnectionString);
            QuietExporter(alone);
            await using (ServiceProvider provider = alone.BuildServiceProvider())
            {
                Assert.IsNotType<ParentBasedSampler>(SamplerOf(provider.GetRequiredService<TracerProvider>()));
            }

            // --- A web host composing the bundle in its web delegate, as a distribution does ---
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                EnvironmentName = Environments.Development,
                ApplicationName = "Tellma.Defaults.Azure.Tests",
                ContentRootPath = AppContext.BaseDirectory,
            });
            _ = builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { [AzureWebDefaultsTellmaBuilderExtensions.ApplicationInsightsConnectionStringKey] = ConnectionString });
            _ = builder.AddTellma("acme", static _ => { }, static web => web.UseAzureWebDefaults());
            QuietExporter(builder.Services);
            await using WebApplication app = builder.Build();

            // --- The platform's sampler wins: an inbound trace's sampled flag never decides ---
            Assert.IsType<ParentBasedSampler>(SamplerOf(app.Services.GetRequiredService<TracerProvider>()));
        }

        /// <summary>
        ///     Keeps a built exporter from writing or sending anything: no offline storage and no live
        ///     metrics. Its statsbeat finds no endpoint to report to for the reserved ingestion endpoint.
        /// </summary>
        /// <param name="services">The services the exporter is registered in.</param>
        private static void QuietExporter(IServiceCollection services)
        {
            _ = services.ConfigureAll<AzureMonitorExporterOptions>(static options =>
            {
                options.DisableOfflineStorage = true;
                options.EnableLiveMetrics = false;
            });
        }

        /// <summary>The sampler a built tracer provider samples with, which the SDK does not expose.</summary>
        /// <param name="provider">The tracer provider.</param>
        /// <returns>The sampler.</returns>
        private static Sampler SamplerOf(TracerProvider provider)
        {
            const System.Reflection.BindingFlags Flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            object? sampler = provider.GetType().GetProperty("Sampler", Flags)?.GetValue(provider) ?? provider.GetType().GetField("Sampler", Flags)?.GetValue(provider);
            return Assert.IsAssignableFrom<Sampler>(sampler);
        }

        /// <summary>Runs the Azure blob store's startup check over a built provider.</summary>
        /// <param name="provider">The provider.</param>
        /// <returns>The problems it reports.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> AzureBlobStoreCheckAsync(ServiceProvider provider)
        {
            IStartupCheck check = Assert.Single(provider.GetServices<IStartupCheck>(), static check => check.Name == AzureBlobStoreCheck);
            return check.CheckAsync(provider, TestContext.Current.CancellationToken);
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
        /// <param name="environment">The host's environment; the suite's Development one when null.</param>
        /// <returns>The composed services.</returns>
        private static ServiceCollection Compose(Dictionary<string, string?> values, Action<TellmaBuilder> compose, IHostEnvironment? environment = null)
        {
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
            ServiceCollection services = [];
            _ = services.AddSingleton(configuration);
            _ = services.AddTellma("acme", configuration, environment ?? new SuiteEnvironment(), compose);
            return services;
        }

        /// <summary>A host environment over the suite's output, Development unless set otherwise.</summary>
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
