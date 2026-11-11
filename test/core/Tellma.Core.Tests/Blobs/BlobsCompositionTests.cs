// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Blobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     <c>AddFileSystemBlobStore</c>: configuration binding, the caller's action, the Development
    ///     default root, validation on first resolution, and one store however often it is called.
    /// </summary>
    public class BlobsCompositionTests
    {
        private static readonly string AbsoluteRoot = Path.Combine(Path.GetTempPath(), "tellma-blobs-composition");

        [Fact]
        public void The_root_binds_from_the_file_system_section()
        {
            using ServiceProvider provider = Build(
                "Production", new Dictionary<string, string?> { ["Tellma:Blobs:FileSystem:RootPath"] = AbsoluteRoot }, configure: null);

            Assert.Equal(AbsoluteRoot, Options(provider).RootPath);
            FileSystemBlobStore store = Assert.IsType<FileSystemBlobStore>(provider.GetRequiredService<IBlobStore>());
            Assert.Equal(AbsoluteRoot, store.RootPath);
        }

        [Fact]
        public void The_callers_action_runs_after_binding_and_wins()
        {
            string configured = Path.Combine(AbsoluteRoot, "configured");

            using ServiceProvider provider = Build(
                "Production",
                new Dictionary<string, string?> { ["Tellma:Blobs:FileSystem:RootPath"] = AbsoluteRoot },
                options => options.RootPath = configured);

            Assert.Equal(configured, Options(provider).RootPath);
        }

        [Fact]
        public void In_development_an_unset_root_defaults_under_local_application_data()
        {
            using ServiceProvider provider = Build("Development", [], configure: null);

            string expected = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify),
                "Tellma",
                TestComposition.Slug,
                "blobs");
            Assert.Equal(expected, Options(provider).RootPath);
            Assert.Equal(expected, FileSystemBlobStoreDefaults.DevelopmentRoot(TestComposition.Slug));
        }

        [Fact]
        public void In_development_a_configured_root_is_kept()
        {
            using ServiceProvider provider = Build(
                "Development", new Dictionary<string, string?> { ["Tellma:Blobs:FileSystem:RootPath"] = AbsoluteRoot }, configure: null);

            Assert.Equal(AbsoluteRoot, Options(provider).RootPath);
        }

        [Theory]
        [InlineData("Production")]
        [InlineData("Staging")]
        public void Outside_development_an_unset_root_fails_when_the_store_is_resolved(string environment)
        {
            using ServiceProvider provider = Build(environment, [], configure: null);

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IBlobStore>);
            Assert.Contains("is not set", Assert.Single(exception.Failures), StringComparison.Ordinal);
        }

        [Fact]
        public void A_relative_root_fails_when_the_store_is_resolved()
        {
            using ServiceProvider provider = Build(
                "Development", new Dictionary<string, string?> { ["Tellma:Blobs:FileSystem:RootPath"] = "blobs" }, configure: null);

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IBlobStore>);
            Assert.Contains("is not absolute", Assert.Single(exception.Failures), StringComparison.Ordinal);
        }

        [Fact]
        public void Without_a_deployment_identity_there_is_no_development_default()
        {
            ServiceCollection services = new();
            services.AddSingleton(TestComposition.Configuration(new Dictionary<string, string?>()));
            services.AddSingleton<IHostEnvironment>(new TestEnvironment("Development"));
            services.AddLogging();
            services.AddFileSystemBlobStore();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IBlobStore>);
        }

        [Fact]
        public void Calling_it_twice_registers_one_store_and_applies_both_actions_in_order()
        {
            ServiceCollection services = Services("Production", []);
            services.AddFileSystemBlobStore(options => options.RootPath = "first");
            services.AddFileSystemBlobStore(options => options.RootPath = AbsoluteRoot);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Single(services, static descriptor => descriptor.ServiceType == typeof(IBlobStore));
            Assert.Equal(AbsoluteRoot, Options(provider).RootPath);
            Assert.Same(provider.GetRequiredService<IBlobStore>(), provider.GetRequiredService<IBlobStore>());
        }

        [Fact]
        public void The_store_composes_beside_add_tellma()
        {
            // AddTellma registers the deployment identity the Development default is named after.
            ServiceCollection services = new();
            IConfiguration configuration = TestComposition.Configuration();
            TestEnvironment environment = new("Development");
            services.AddSingleton(configuration);
            services.AddSingleton<IHostEnvironment>(environment);
            services.AddLogging();
            TestComposition.Compose(static _ => { }, configuration, environment, services: services);
            services.AddFileSystemBlobStore();
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Equal(FileSystemBlobStoreDefaults.DevelopmentRoot(TestComposition.Slug), Options(provider).RootPath);
            Assert.IsType<FileSystemBlobStore>(provider.GetRequiredService<IBlobStore>());
        }

        /// <summary>The services of a host that composed the file-system store.</summary>
        /// <param name="environment">The environment name.</param>
        /// <param name="configuration">The configuration values.</param>
        /// <param name="configure">The caller's action.</param>
        /// <returns>The built provider.</returns>
        private static ServiceProvider Build(
            string environment, Dictionary<string, string?> configuration, Action<FileSystemBlobStoreOptions>? configure)
        {
            ServiceCollection services = Services(environment, configuration);
            services.AddFileSystemBlobStore(configure);
            return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        }

        /// <summary>What a host registers before composing a store: configuration, environment, identity and logging.</summary>
        /// <param name="environment">The environment name.</param>
        /// <param name="configuration">The configuration values.</param>
        /// <returns>The services.</returns>
        private static ServiceCollection Services(string environment, Dictionary<string, string?> configuration)
        {
            ServiceCollection services = new();
            services.AddSingleton(TestComposition.Configuration(configuration));
            services.AddSingleton<IHostEnvironment>(new TestEnvironment(environment));
            services.AddSingleton(new DeploymentIdentity(TestComposition.Slug, environment));
            services.AddLogging();
            return services;
        }

        /// <summary>The resolved options, Development default and validation applied.</summary>
        /// <param name="provider">The provider.</param>
        /// <returns>The options.</returns>
        private static FileSystemBlobStoreOptions Options(IServiceProvider provider)
        {
            return provider.GetRequiredService<IOptions<FileSystemBlobStoreOptions>>().Value;
        }
    }
}
