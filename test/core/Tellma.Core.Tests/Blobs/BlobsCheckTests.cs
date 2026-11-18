// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Testing.Blobs;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The <c>blobs</c> startup check: each way the store and processor registrations, the kinds'
    ///     final policies, the blob options, the file-system root and the scratch directory can be
    ///     wrong is a problem with a fix, and a sound composition has none.
    /// </summary>
    public sealed class BlobsCheckTests : IDisposable
    {
        // A root the file-system cases create; removed with the suite's instance.
        private readonly string _root = Path.Join(Path.GetTempPath(), "tellma-blobs-check-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));

        /// <inheritdoc />
        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        [Fact]
        public async Task A_sound_composition_has_no_problem()
        {
            Assert.Empty(await ProblemsAsync(static tellma => tellma.AddFeature(BlobModel.Feature())));
        }

        [Fact]
        public async Task The_check_needs_infrastructure_and_is_named_blobs()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            IStartupCheck check = Assert.Single(provider.GetServices<IStartupCheck>(), static check => check is BlobsCheck);

            Assert.Equal(("blobs", true), (check.Name, check.RequiresInfrastructure));
        }

        [Fact]
        public async Task No_store_is_a_problem()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, static services => services.RemoveAll<IBlobStore>());

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("No blob store is registered.", problem.Problem);
            Assert.Contains("AddFileSystemBlobStore()", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Two_stores_are_a_problem_naming_both()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, services => services.AddFileSystemBlobStore(options => options.RootPath = _root));

            Assert.Equal(
                ["2 blob stores are registered (InMemoryBlobStore, FileSystemBlobStore); a process runs on exactly one."],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task An_image_kind_without_a_processor_is_a_problem_naming_the_image_kinds()
        {
            List<CompositionProblem> problems = await ProblemsAsync(
                static tellma => tellma.AddFeature(BlobModel.Feature()), static services => services.RemoveAll<IImageProcessor>());

            CompositionProblem problem = Assert.Single(problems);
            Assert.StartsWith("The blob kinds ", problem.Problem, StringComparison.Ordinal);
            Assert.Contains($"'{BlobModel.CoverKind}'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains($"'{BlobModel.StickerKind}'", problem.Problem, StringComparison.Ordinal);
            Assert.DoesNotContain($"'{BlobModel.ScanKind}'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("AddTellmaSkia()", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Two_processors_are_a_problem()
        {
            List<CompositionProblem> problems = await ProblemsAsync(
                static _ => { }, static services => services.AddSingleton<IImageProcessor>(new StubImageProcessor()));

            Assert.Equal(
                "2 image processors are registered (StubImageProcessor, StubImageProcessor); uploads use exactly one.",
                Assert.Single(problems).Problem);
        }

        [Fact]
        public void The_builders_processor_is_registered_once_and_a_second_type_beside_it()
        {
            IServiceCollection services = TestComposition.Compose(
                static tellma => tellma.Blobs(static blobs => blobs
                    .ImageProcessor<StubImageProcessor>()
                    .ImageProcessor<StubImageProcessor>()
                    .ImageProcessor<OtherProcessor>()),
                inMemoryBlobs: false);

            Assert.Equal(
                [typeof(StubImageProcessor), typeof(OtherProcessor)],
                services.Where(static descriptor => descriptor.ServiceType == typeof(IImageProcessor)).Select(static descriptor => descriptor.ImplementationType));
        }

        [Fact]
        public async Task A_transform_of_an_undeclared_kind_is_a_problem_naming_its_owner()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static tellma => tellma
                .AddFeature(BlobModel.Feature(static contribution => contribution.BlobKind("bm-ghost", static policy => policy)))
                .Blobs(static blobs => blobs.Kind("bm-phantom", static policy => policy)));

            Assert.Equal(
                [
                    "The blob kind 'bm-ghost' configured by feature 'bm' is not declared by any [BlobReference] of a stack's aggregate.",
                    "The blob kind 'bm-phantom' configured by the host is not declared by any [BlobReference] of a stack's aggregate.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task Every_unsound_part_of_a_final_policy_is_a_problem()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static tellma => tellma
                .AddFeature(BlobModel.Feature())
                .Blobs(static blobs => blobs
                    .Kind(BlobModel.CoverKind, static policy => policy with
                    {
                        MaxSize = 0,
                        AllowedContentTypes = ["Image/PNG", "image/jpeg; q=1"],
                        Image = new ImagePolicy(0, (ImageFit)99, (ImageFormat)99, MaxInputPixels: -1),
                        Thumbnail = new ThumbnailPolicy(0, (ImageFormat)99),
                        StagingTtl = TimeSpan.Zero,
                        ReadAccess = (BlobReadAccess)99,
                    })
                    .Kind(BlobModel.ScanKind, static policy => policy with { AllowedContentTypes = [] })));

            string cover = $"The policy of the blob kind '{BlobModel.CoverKind}' ";
            Assert.Equal(
                [
                    cover + "has a maximum size of 0 bytes, which is not positive.",
                    cover + "allows 'Image/PNG', which is not a lower-case media type without parameters, such as 'application/pdf'.",
                    cover + "allows 'image/jpeg; q=1', which is not a lower-case media type without parameters, such as 'application/pdf'.",
                    cover + "normalises images to 0 pixels, which is not positive.",
                    cover + "accepts images of at most -1 pixels, which is not positive.",
                    cover + "fits images with 99, which is not a defined fit.",
                    cover + "encodes images as 99, which is not a defined format.",
                    cover + "renders thumbnails of 0 pixels, which is not positive.",
                    cover + "encodes thumbnails as 99, which is not a defined format.",
                    cover + "stages uploads for 00:00:00, which is not a positive number of whole seconds a staged row can hold.",
                    cover + "has the read access 99, which is not a defined access.",
                    $"The policy of the blob kind '{BlobModel.ScanKind}' allows no content type.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task Options_that_are_not_positive_are_problems()
        {
            IConfiguration configuration = TestComposition.Configuration(TestComposition.Sound(
                ("Tellma:Blobs:StagingTtl", "00:00:00"),
                ("Tellma:Blobs:SweepBatchSize", "0"),
                ("Tellma:Blobs:MaxStagedPerUser", "-1"),
                ("Tellma:Blobs:ThumbnailTimeout", "-00:00:01")));

            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, configuration: configuration);

            Assert.Equal(
                [
                    nameof(BlobOptions.StagingTtl),
                    nameof(BlobOptions.SweepBatchSize),
                    nameof(BlobOptions.MaxStagedPerUser),
                    nameof(BlobOptions.ThumbnailTimeout),
                ],
                problems.Select(static problem => problem.Problem.Split(' ')[3]));
            Assert.All(problems, static problem => Assert.Contains("'Tellma:Blobs'", problem.Fix, StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_kind_above_the_upload_ceiling_is_a_problem_not_a_clamp()
        {
            IConfiguration configuration = TestComposition.Configuration(TestComposition.Sound(("Tellma:Blobs:MaxUploadSize", "5242880")));

            List<CompositionProblem> problems = await ProblemsAsync(static tellma => tellma.AddFeature(BlobModel.Feature()), configuration: configuration);

            // The cover (a photo) is 10 MiB and the scan (an attachment) 100 MiB; the sticker (a signature) is 2 MiB.
            Assert.Contains(problems, static problem => problem.Problem.StartsWith($"The blob kind '{BlobModel.CoverKind}' accepts up to 10485760 bytes, above", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.StartsWith($"The blob kind '{BlobModel.ScanKind}' accepts up to 104857600 bytes, above", StringComparison.Ordinal));
            Assert.DoesNotContain(problems, static problem => problem.Problem.Contains($"'{BlobModel.StickerKind}'", StringComparison.Ordinal));
        }

        [Fact]
        public async Task An_invalid_file_system_root_is_reported_by_the_stores_own_rules()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, static services =>
            {
                services.RemoveAll<IBlobStore>();
                services.AddFileSystemBlobStore(static options => options.RootPath = "relative/blobs");
            });

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("'relative/blobs' is not absolute", problem.Problem, StringComparison.Ordinal);
            Assert.Contains(FileSystemBlobStoreOptions.SectionName, problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_valid_file_system_root_is_probed_and_passes()
        {
            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, services =>
            {
                services.RemoveAll<IBlobStore>();
                services.AddFileSystemBlobStore(options => options.RootPath = _root);
            });

            Assert.Empty(problems.Select(static problem => problem.Problem));

            // On Unix the probe hard-links a file under the root, which it creates; Windows needs no probe.
            Assert.Equal(!OperatingSystem.IsWindows(), Directory.Exists(_root));
        }

        [Fact]
        public async Task A_relative_scratch_directory_is_a_problem()
        {
            IConfiguration configuration = TestComposition.Configuration(TestComposition.Sound((ScratchDirectory.ConfigurationKey, "scratch")));

            List<CompositionProblem> problems = await ProblemsAsync(static _ => { }, configuration: configuration);

            Assert.Equal(
                $"The scratch directory 'scratch' configured under '{ScratchDirectory.ConfigurationKey}' is not an absolute path.",
                Assert.Single(problems).Problem);
        }

        /// <summary>Composes, adjusts the services, and runs every check, returning the blobs check's problems.</summary>
        /// <param name="compose">The composition.</param>
        /// <param name="adjust">Changes the composed services, or null.</param>
        /// <param name="configuration">The configuration; the sound one when null.</param>
        /// <returns>The blobs check's problems.</returns>
        private static async Task<List<CompositionProblem>> ProblemsAsync(
            Action<TellmaBuilder> compose, Action<IServiceCollection>? adjust = null, IConfiguration? configuration = null)
        {
            configuration ??= TestComposition.Configuration();
            IServiceCollection services = TestComposition.Compose(compose, configuration);

            // A host registers its configuration, which the file-system store's options bind from.
            services.TryAddSingleton(configuration);
            adjust?.Invoke(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            IReadOnlyList<CompositionProblem> problems = await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken);
            return problems.From(CoreCheckNames.Blobs);
        }

        /// <summary>A second processor type, which the builder registers beside the first.</summary>
        private sealed class OtherProcessor : IImageProcessor
        {
            /// <inheritdoc />
            public Task<ProcessedImage> ProcessAsync(
                string kind, byte[] input, ImagePolicy policy, ThumbnailPolicy? thumbnail, TimeSpan timeout, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }

            /// <inheritdoc />
            public Task<byte[]?> ThumbnailAsync(
                string kind, Stream input, string contentType, ThumbnailPolicy policy, TimeSpan timeout, CancellationToken cancellationToken)
            {
                throw new NotSupportedException();
            }
        }
    }
}
