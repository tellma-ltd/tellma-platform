// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Imaging.Tests.Infrastructure;

namespace Tellma.Core.Imaging.Tests.Composition
{
    /// <summary>
    ///     <c>AddTellmaSkia</c>: one processor registration however often it is called, resolvable
    ///     from a plain container, and the startup check that proves the natives load.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class ImagingCompositionTests
    {
        [Fact]
        public void It_registers_one_singleton_processor_and_one_check_however_often_it_is_called()
        {
            var services = new ServiceCollection();

            services.AddTellmaSkia();
            services.AddTellmaSkia();

            ServiceDescriptor processor = Assert.Single(services, static d => d.ServiceType == typeof(IImageProcessor));
            Assert.Equal(ServiceLifetime.Singleton, processor.Lifetime);
            Assert.Equal(typeof(SkiaImageProcessor), processor.ImplementationType);
            Assert.Single(services, static d => d.ServiceType == typeof(IStartupCheck));
        }

        [Fact]
        public void The_processor_resolves_from_a_container_with_logging()
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
            services.AddTellmaSkia();
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            Assert.IsType<SkiaImageProcessor>(provider.GetRequiredService<IImageProcessor>());
            Assert.Same(provider.GetRequiredService<IImageProcessor>(), provider.GetRequiredService<IImageProcessor>());
        }

        [Fact]
        public void The_concurrency_binds_from_the_blob_section()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:ImageConcurrency"] = "3" });

            Assert.Equal(3, provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.ImageConcurrency);
            Assert.Equal(3, Assert.IsType<SkiaImageProcessor>(provider.GetRequiredService<IImageProcessor>()).FreeImageSlots);
        }

        [Fact]
        public void The_concurrency_defaults_to_half_the_processors_and_at_least_one()
        {
            using ServiceProvider provider = Build([]);

            Assert.Equal(Math.Max(1, Environment.ProcessorCount / 2), provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.ImageConcurrency);
        }

        [Fact]
        public void A_non_positive_concurrency_is_refused()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:ImageConcurrency"] = "0" });

            Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IImageProcessor>);
        }

        [Fact]
        public void A_host_clock_is_kept()
        {
            var services = new ServiceCollection();
            var clock = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
            services.AddSingleton<TimeProvider>(clock);

            services.AddTellmaSkia();

            Assert.Same(clock, Assert.Single(services, static d => d.ServiceType == typeof(TimeProvider)).ImplementationInstance);
        }

        [Fact]
        public async Task The_startup_check_passes_when_both_native_libraries_load()
        {
            var check = new ImagingStartupCheck();

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(
                new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken);

            Assert.Equal("core.imaging", check.Name);
            Assert.False(check.RequiresInfrastructure);
            Assert.Empty(problems);
            Assert.Equal(1, PdfRenderGate.Semaphore.CurrentCount);
        }

        [Fact]
        public void The_null_services_argument_is_refused()
        {
            Assert.Throws<ArgumentNullException>(static () => ImagingComposition.AddTellmaSkia(null!));
        }

        /// <summary>A container with configuration, logging and the processor.</summary>
        /// <param name="settings">The configuration values.</param>
        /// <returns>The provider.</returns>
        private static ServiceProvider Build(Dictionary<string, string?> settings)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
            services.AddTellmaSkia();
            return services.BuildServiceProvider();
        }
    }
}
