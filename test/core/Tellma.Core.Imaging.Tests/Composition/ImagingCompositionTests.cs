// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Composition
{
    /// <summary>
    ///     <c>AddTellmaSkia</c>: one processor registration however often it is called, resolvable
    ///     from a plain container, and the startup check that proves the natives load; and where a
    ///     host renders PDF thumbnails at all, which the processor and the check decide alike from
    ///     the host's environment, the check saying so in the log.
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
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
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
        public void The_pdf_worker_concurrency_binds_from_the_blob_section()
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { ["Tellma:Blobs:PdfWorkerConcurrency"] = "2" });

            Assert.Equal(2, provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.PdfWorkerConcurrency);
            Assert.Equal(2, Assert.IsType<SkiaImageProcessor>(provider.GetRequiredService<IImageProcessor>()).FreePdfSlots);
        }

        [Fact]
        public void The_pdf_worker_concurrency_defaults_to_a_quarter_of_the_processors_and_at_least_one()
        {
            using ServiceProvider provider = Build([]);

            Assert.Equal(Math.Max(1, Environment.ProcessorCount / 4), provider.GetRequiredService<IOptions<SkiaImageProcessorOptions>>().Value.PdfWorkerConcurrency);
        }

        [Theory]
        [InlineData("ImageConcurrency")]
        [InlineData("PdfWorkerConcurrency")]
        public void A_non_positive_concurrency_is_refused(string key)
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { [$"Tellma:Blobs:{key}"] = "0" });

            OptionsValidationException exception = Assert.Throws<OptionsValidationException>(provider.GetRequiredService<IImageProcessor>);
            Assert.Contains($"Tellma:Blobs:{key}", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("ImageConcurrency")]
        [InlineData("PdfWorkerConcurrency")]
        public async Task A_non_positive_concurrency_fails_the_startup_check_naming_its_key(string key)
        {
            using ServiceProvider provider = Build(new Dictionary<string, string?> { [$"Tellma:Blobs:{key}"] = "0" });

            IReadOnlyList<CompositionProblem> problems = await new ImagingStartupCheck().CheckAsync(provider, TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains($"Tellma:Blobs:{key}", problem.Fix, StringComparison.Ordinal);
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
            using ServiceProvider provider = Build([]);

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            Assert.Equal("core.imaging", check.Name);
            Assert.False(check.RequiresInfrastructure);
            Assert.Empty(problems);
        }

        [Fact]
        public async Task The_startup_check_fails_naming_the_fix_when_no_pdf_worker_can_start()
        {
            var check = new ImagingStartupCheck(MissingWorker());
            using ServiceProvider provider = Build([]);

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

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
            using ServiceProvider provider = Build([]);

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains(description, problem.Problem, StringComparison.Ordinal);
            Assert.False(string.IsNullOrWhiteSpace(problem.Fix));
        }

        [Theory]
        [InlineData("Development")]
        [InlineData("Production")]
        [InlineData("Staging")]
        [InlineData(null)]
        public void The_processor_renders_pdf_thumbnails_on_linux_and_elsewhere_only_in_development(string? environment)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            if (environment is not null)
            {
                services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(environment));
            }

            services.AddTellmaSkia();
            using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

            // A host that registers no environment is not in Development. The suites run on glibc.
            SkiaImageProcessor processor = Assert.IsType<SkiaImageProcessor>(provider.GetRequiredService<IImageProcessor>());
            PdfThumbnailSupport expected = OperatingSystem.IsLinux() || environment == "Development"
                ? PdfThumbnailSupport.Enabled
                : PdfThumbnailSupport.DisabledOutsideDevelopment;
            Assert.Equal(expected, processor.PdfThumbnails);
        }

        [Theory]
        [InlineData("DisabledOutsideDevelopment")]
        [InlineData("DisabledOnMusl")]
        public async Task A_host_that_renders_no_pdf_thumbnails_passes_the_startup_check_with_a_warning_and_starts_no_worker(string support)
        {
            // The check's worker is missing: starting it would be a problem.
            PdfThumbnailSupport disabled = Enum.Parse<PdfThumbnailSupport>(support);
            var check = new ImagingStartupCheck(MissingWorker(), disabled);
            var logger = new FakeLogger<SkiaImageProcessor>();
            using ServiceProvider provider = new ServiceCollection().AddSingleton<ILogger<SkiaImageProcessor>>(logger).BuildServiceProvider();

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            Assert.Empty(problems);
            FakeLogRecord record = Assert.Single(logger.Collector.GetSnapshot());
            Assert.Equal("BlobPdfThumbnailsDisabled", record.Id.Name);
            Assert.Equal(LogLevel.Warning, record.Level);
            Assert.Contains(PdfThumbnailPolicy.Reason(disabled), record.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_startup_check_decides_from_the_hosts_environment_whether_a_worker_is_probed()
        {
            // A Production host: on Linux it renders PDF thumbnails, so the missing worker is a
            // problem; elsewhere it renders none, starts no worker, and says so.
            var check = new ImagingStartupCheck(MissingWorker());
            var logger = new FakeLogger<SkiaImageProcessor>();
            using ServiceProvider provider = new ServiceCollection()
                .AddSingleton<ILogger<SkiaImageProcessor>>(logger)
                .AddSingleton<IHostEnvironment>(TestHostEnvironment.Production)
                .BuildServiceProvider();

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            string?[] events = [.. logger.Collector.GetSnapshot().Select(static record => record.Id.Name)];
            if (OperatingSystem.IsLinux())
            {
                Assert.Contains("PDF worker assembly", Assert.Single(problems).Problem, StringComparison.Ordinal);
                Assert.DoesNotContain("BlobPdfThumbnailsDisabled", events);
            }
            else
            {
                Assert.Empty(problems);
                Assert.Equal("BlobPdfThumbnailsDisabled", Assert.Single(events));
            }
        }

        [Theory]
        [InlineData("0\n", true)]
        [InlineData(null, true)]
        [InlineData("1\n", false)]
        [InlineData("3", false)]
        public async Task On_linux_the_startup_check_warns_when_the_kernel_lets_a_worker_open_the_memory_of_the_accounts_other_processes(string? scope, bool warns)
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Yama's ptrace scope is a Linux kernel setting.");
            var check = new ImagingStartupCheck(readPtraceScope: () => scope);
            var logger = new FakeLogger<SkiaImageProcessor>();
            using ServiceProvider provider = new ServiceCollection().AddSingleton<ILogger<SkiaImageProcessor>>(logger).BuildServiceProvider();

            IReadOnlyList<CompositionProblem> problems = await check.CheckAsync(provider, TestContext.Current.CancellationToken);

            // A warning, never a problem: the host starts, and PDF thumbnails render.
            Assert.Empty(problems);
            FakeLogRecord[] records = [.. logger.Collector.GetSnapshot()];
            if (warns)
            {
                FakeLogRecord record = Assert.Single(records);
                Assert.Equal("BlobPdfWorkerPtraceUnrestricted", record.Id.Name);
                Assert.Equal(LogLevel.Warning, record.Level);
                Assert.Contains("kernel.yama.ptrace_scope", record.Message, StringComparison.Ordinal);

                // It is about the account's other processes: the host closes itself, and says so.
                Assert.Contains("This host closes itself to its PDF workers", record.Message, StringComparison.Ordinal);
                Assert.Contains("any other process that runs under the host's account", record.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Empty(records);
            }
        }

        [Theory]
        [InlineData(null, "the Yama security module is not enabled")]
        [InlineData("0", "kernel.yama.ptrace_scope is 0")]
        [InlineData("0\n", "kernel.yama.ptrace_scope is 0")]
        [InlineData("1\n", null)]
        [InlineData("2", null)]
        [InlineData("3", null)]
        [InlineData("", "kernel.yama.ptrace_scope is not a number")]
        [InlineData("-1", "kernel.yama.ptrace_scope is not a number")]
        [InlineData("on", "kernel.yama.ptrace_scope is not a number")]
        public void Only_a_ptrace_scope_of_one_or_more_goes_unreported(string? scope, string? reported)
        {
            Assert.Equal(reported, ImagingStartupCheck.UnrestrictedPtrace(scope));
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

        /// <summary>A launcher whose worker assembly does not exist: starting a worker from it fails.</summary>
        /// <returns>The launcher.</returns>
        private static PdfWorkerLauncher MissingWorker()
        {
            return new PdfWorkerLauncher(Path.Combine(AppContext.BaseDirectory, "missing", "Tellma.Core.Imaging.PdfWorker.dll"));
        }

        /// <summary>
        ///     A container with configuration, logging and the processor, of a Development host, which
        ///     renders PDF thumbnails on every operating system the suites run on.
        /// </summary>
        /// <param name="settings">The configuration values.</param>
        /// <returns>The provider.</returns>
        private static ServiceProvider Build(Dictionary<string, string?> settings)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton<IHostEnvironment>(TestHostEnvironment.Development);
            services.AddTellmaSkia();
            return services.BuildServiceProvider();
        }
    }
}
