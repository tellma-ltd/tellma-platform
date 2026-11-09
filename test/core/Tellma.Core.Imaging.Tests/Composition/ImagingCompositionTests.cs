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
using Tellma.Core.Imaging.Workers;

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
        public async Task A_non_positive_concurrency_fails_the_startup_check_naming_its_key()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:ImageConcurrency"] = "0" });

            IReadOnlyList<CompositionProblem> problems = await new ImagingStartupCheck().CheckAsync(provider, TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("Tellma:Blobs:ImageConcurrency", problem.Fix, StringComparison.Ordinal);
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
        }

        [Fact]
        public async Task The_startup_check_fails_naming_the_fix_when_no_pdf_worker_can_start()
        {
            var check = new ImagingStartupCheck(new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing", "Tellma.Core.Imaging.PdfWorker.dll")));

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(
                new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("PDF worker assembly", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("framework-dependent", problem.Fix, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("crash-before-ready", "ended before it rendered")]
        [InlineData("not-png", "did not render")]
        public async Task The_startup_check_fails_when_the_pdf_worker_does_not_render_the_probe(string mode, string description)
        {
            var check = new ImagingStartupCheck(new PdfWorkerLauncher(TestWorkers.StandInPath, [mode]));

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(
                new ServiceCollection().BuildServiceProvider(), TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains(description, problem.Problem, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
        }

        [Fact]
        public void The_worker_memory_limit_binds_from_the_blob_section()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:PdfWorkerMemoryLimit"] = "134217728" });

            Assert.Equal(128L * 1024 * 1024, provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.PdfWorkerMemoryLimit);
        }

        [Fact]
        public void The_worker_memory_limit_defaults_to_256_mib()
        {
            using ServiceProvider provider = Build([]);

            Assert.Equal(256L * 1024 * 1024, provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.PdfWorkerMemoryLimit);
        }

        [Fact]
        public async Task Too_small_a_worker_memory_limit_fails_the_startup_check_naming_its_key()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:PdfWorkerMemoryLimit"] = "1048576" });

            Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IImageProcessor>);
            IReadOnlyList<CompositionProblem> problems = await new ImagingStartupCheck().CheckAsync(provider, TestContext.Current.CancellationToken);
            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("Tellma:Blobs:PdfWorkerMemoryLimit", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Tellma:Blobs:PdfWorkerMemoryLimit", problem.Fix, StringComparison.Ordinal);
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
