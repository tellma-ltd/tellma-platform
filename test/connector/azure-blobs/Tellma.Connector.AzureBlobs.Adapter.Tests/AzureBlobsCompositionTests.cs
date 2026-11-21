// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Connector.AzureBlobs.Tests
{
    /// <summary>
    ///     Composing the Azure blob store: one store however often it is added, the credential it
    ///     reaches an endpoint with, and the startup check that refuses settings naming no usable
    ///     account before the host serves traffic. Nothing here reaches Azure.
    /// </summary>
    public sealed class AzureBlobsCompositionTests
    {
        private const string Emulator = "UseDevelopmentStorage=true";

        private static readonly Uri Endpoint = new("https://acme.blob.core.windows.net");

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void Adding_the_store_twice_registers_one_store_check_and_validator_and_applies_both_settings_actions()
        {
            ServiceCollection services = [];
            services.AddAzureBlobStore(static options => options.ConnectionString = Emulator, credential: null);
            services.AddAzureBlobStore(static options => options.ContainerPrefix = "acme-", credential: null);

            _ = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IBlobStore));
            _ = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IStartupCheck));
            _ = Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IValidateOptions<AzureBlobStoreOptions>));

            using ServiceProvider provider = Build(services);
            AzureBlobStoreOptions options = provider.GetRequiredService<IOptions<AzureBlobStoreOptions>>().Value;
            Assert.Equal(Emulator, options.ConnectionString);
            Assert.Equal("acme-", options.ContainerPrefix);
            Assert.Equal("acme-t7", Assert.IsType<AzureBlobStore>(provider.GetRequiredService<IBlobStore>()).ContainerName(7));
        }

        [Fact]
        public void Without_a_credential_passed_the_store_uses_the_host_s_registered_one()
        {
            TokenCredential host = Credential();
            ServiceCollection services = [];
            _ = services.AddSingleton(host);
            services.AddAzureBlobStore(static options => options.ServiceUri = Endpoint, credential: null);

            using ServiceProvider provider = Build(services);

            Assert.Same(host, provider.GetRequiredService<AzureBlobStoreCredential>().Credential);
        }

        [Fact]
        public void A_credential_passed_wins_over_the_host_s_and_a_later_call_without_one_keeps_it()
        {
            TokenCredential passed = Credential();
            ServiceCollection services = [];
            _ = services.AddSingleton(Credential());
            services.AddAzureBlobStore(static options => options.ServiceUri = Endpoint, passed);
            services.AddAzureBlobStore(static _ => { }, credential: null);

            using ServiceProvider provider = Build(services);

            Assert.Same(passed, provider.GetRequiredService<AzureBlobStoreCredential>().Credential);
        }

        [Fact]
        public async Task The_check_passes_settings_that_name_a_usable_account()
        {
            ServiceCollection services = [];
            services.AddAzureBlobStore(static options => options.ServiceUri = Endpoint, Credential());

            IReadOnlyList<CompositionProblem> problems = await CheckAsync(services);

            Assert.Empty(problems);
        }

        [Fact]
        public async Task The_check_reports_every_broken_rule_of_the_settings()
        {
            ServiceCollection services = [];
            services.AddAzureBlobStore(static options => options.MaxParallelism = 0, credential: null);

            IReadOnlyList<CompositionProblem> problems = await CheckAsync(services);

            // --- One problem per rule, each naming the check and how to fix it ---
            Assert.Collection(
                problems,
                static problem => Assert.StartsWith("Neither Tellma:Blobs:Azure:ServiceUri", problem.Problem, StringComparison.Ordinal),
                static problem => Assert.StartsWith("Tellma:Blobs:Azure:MaxParallelism is 0", problem.Problem, StringComparison.Ordinal));
            Assert.All(problems, static problem =>
            {
                Assert.Equal(AzureBlobStoreCheck.CheckName, problem.Source);
                Assert.Contains("Tellma:Blobs:Azure:ServiceUri", problem.Fix, StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task The_check_reports_a_connection_string_that_does_not_parse()
        {
            ServiceCollection services = [];
            services.AddAzureBlobStore(static options => options.ConnectionString = "not a connection string", credential: null);

            CompositionProblem problem = Assert.Single(await CheckAsync(services));

            Assert.StartsWith("The Azure blob store's client cannot be built:", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_check_reports_an_endpoint_with_no_credential_passed_or_registered()
        {
            ServiceCollection services = [];
            services.AddAzureBlobStore(static options => options.ServiceUri = Endpoint, credential: null);

            CompositionProblem problem = Assert.Single(await CheckAsync(services));

            Assert.Contains("none was passed or registered", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_check_needs_deployment_settings()
        {
            Assert.True(new AzureBlobStoreCheck().RequiresInfrastructure);
        }

        /// <summary>A credential that is never asked for a token.</summary>
        /// <returns>The credential.</returns>
        private static TokenCredential Credential()
        {
            return DelegatedTokenCredential.Create(static (_, _) => throw new InvalidOperationException("No token is requested in these cases."));
        }

        /// <summary>The provider a host builds, with the logging the store writes to.</summary>
        /// <param name="services">The services.</param>
        /// <returns>The provider.</returns>
        private static ServiceProvider Build(ServiceCollection services)
        {
            _ = services.AddLogging();
            return services.BuildServiceProvider();
        }

        /// <summary>Runs the composed store's startup check.</summary>
        /// <param name="services">The services.</param>
        /// <returns>The problems it reports.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> CheckAsync(ServiceCollection services)
        {
            await using ServiceProvider provider = Build(services);
            IStartupCheck check = Assert.Single(provider.GetServices<IStartupCheck>());
            return await check.CheckAsync(provider, Token);
        }
    }
}
