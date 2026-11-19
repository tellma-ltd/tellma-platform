// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Blobs;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>
    ///     The kind registry over the realised model: a kind at every depth of an aggregate, owned by
    ///     the securable that governs it; the features' then the host's transforms applied in order and
    ///     seen alike by the registry and the metadata; and the declarations the composition refuses.
    /// </summary>
    public class BlobKindRegistryTests
    {
        [Fact]
        public async Task A_root_kind_is_owned_by_the_stack_with_no_path()
        {
            BlobKindDescriptor cover = await FindAsync(static tellma => tellma.AddFeature(BlobModel.Feature()), BlobModel.CoverKind);

            Assert.Equal(
                (BlobModel.Albums, "bm.BmAlbum", new TableName("bm", "Albums"), "CoverId", string.Empty),
                (cover.OwnerResource, cover.OwnerEntity, cover.OwnerTable, cover.OwnerColumn, cover.OwnerPath));
            Assert.Equal(BlobKindPolicy.Photo, cover.Policy);
        }

        [Fact]
        public async Task A_child_with_a_query_surface_owns_its_kind_with_no_path()
        {
            BlobKindDescriptor scan = await FindAsync(static tellma => tellma.AddFeature(BlobModel.Feature()), BlobModel.ScanKind);

            Assert.Equal(
                (BlobModel.Pages, "bm.BmPage", new TableName("bm", "Pages"), "ScanId", string.Empty),
                (scan.OwnerResource, scan.OwnerEntity, scan.OwnerTable, scan.OwnerColumn, scan.OwnerPath));
        }

        [Fact]
        public async Task A_grandchild_without_a_query_surface_is_owned_by_the_stack_through_its_parent_navigations()
        {
            BlobKindDescriptor sticker = await FindAsync(static tellma => tellma.AddFeature(BlobModel.Feature()), BlobModel.StickerKind);

            Assert.Equal(
                (BlobModel.Albums, "bm.BmSticker", new TableName("bm", "Stickers"), "ImageId", "Spread.Album"),
                (sticker.OwnerResource, sticker.OwnerEntity, sticker.OwnerTable, sticker.OwnerColumn, sticker.OwnerPath));
            Assert.Equal(BlobKindPolicy.Signature, sticker.Policy);
        }

        [Fact]
        public async Task Every_declared_kind_is_listed_once_and_an_undeclared_one_is_not_found()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.AddFeature(BlobModel.Feature()))
                .BuildServiceProvider(validateScopes: true);
            IBlobKindRegistry registry = provider.GetRequiredService<IBlobKindRegistry>();

            string[] kinds = [.. registry.All.Select(static kind => kind.Kind)];
            Assert.Subset(new HashSet<string>(kinds), new HashSet<string> { BlobModel.CoverKind, BlobModel.ScanKind, BlobModel.StickerKind, "user-image", "user-signature" });
            Assert.Equal(kinds.Length, kinds.Distinct(StringComparer.Ordinal).Count());
            Assert.Null(registry.Find("bm-none"));
            Assert.Same(registry, provider.GetRequiredService<IBlobKindRegistry>());
        }

        [Fact]
        public async Task The_features_transforms_run_before_the_hosts_and_the_metadata_sees_the_same_policy()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(BlobModel.Feature(static contribution => contribution
                    .BlobKind(BlobModel.ScanKind, static policy => policy with { MaxSize = 4096 })))
                .Blobs(static blobs => blobs
                    .Kind(BlobModel.ScanKind, static policy => policy with { MaxSize = policy.MaxSize / 2 })
                    .Kind(BlobModel.ScanKind, static policy => policy with { Thumbnail = null })))
                .BuildServiceProvider(validateScopes: true);

            BlobKindPolicy policy = provider.GetRequiredService<IBlobKindRegistry>().Find(BlobModel.ScanKind)!.Policy;
            Assert.Equal(2048L, policy.MaxSize);
            Assert.Null(policy.Thumbnail);

            BlobReferenceMetadata reference = Assert.Single(provider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(BmPage)).BlobReferences);
            Assert.Same(policy, reference.Policy);
        }

        [Fact]
        public async Task A_transform_that_throws_or_returns_no_policy_is_the_kinds_problem()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(BlobModel.Feature(static contribution => contribution
                    .BlobKind(BlobModel.CoverKind, static _ => throw new InvalidOperationException("No covers."))))
                .Blobs(static blobs => blobs.Kind(BlobModel.ScanKind, static _ => null!)));

            List<CompositionProblem> metadata = problems.From(CoreCheckNames.EntityMetadata);
            Assert.Contains(metadata, static problem =>
                problem.Problem.Contains($"'{BlobModel.CoverKind}'", StringComparison.Ordinal)
                && problem.Problem.Contains("the transform from feature 'bm' threw InvalidOperationException: No covers.", StringComparison.Ordinal));
            Assert.Contains(metadata, static problem =>
                problem.Problem.Contains($"'{BlobModel.ScanKind}'", StringComparison.Ordinal)
                && problem.Problem.Contains("the transform from the host returned no policy", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_kind_on_an_entity_no_stack_carries_is_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(BlobModel.Feature(static contribution => contribution.Model<BmLooseConfiguration>())));

            CompositionProblem problem = Assert.Single(problems.From(CoreCheckNames.EntityMetadata), static problem => problem.Problem.Contains("'bm-loose'", StringComparison.Ordinal));
            Assert.Contains("neither a stack's root nor a child entity of a stack's aggregate", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("contribution.Entity<BmLoose>()", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public void A_contributed_kind_without_a_name_or_a_transform_fails_the_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(BlobModel.Feature(static contribution => contribution
                    .Add(new BlobKindContributionItem(null!, static policy => policy))
                    .Add(new BlobKindContributionItem(BlobModel.ScanKind, null!)))));

            Assert.Equal(2, problems.Count(static problem => problem.Source == CompositionNames.Source && problem.Problem.Contains("contributes a blob-kind transform without a kind or without a transform", StringComparison.Ordinal)));
        }

        [Fact]
        public void The_blob_settings_cannot_change_once_the_composition_has_returned()
        {
            BlobsBuilder? captured = null;
            _ = TestComposition.Compose(tellma => tellma.Blobs(blobs => captured = blobs));

            Assert.NotNull(captured);
            _ = Assert.Throws<InvalidOperationException>(() => captured.Kind(BlobModel.ScanKind, static policy => policy));
            _ = Assert.Throws<InvalidOperationException>(() => captured.ImageProcessor<Testing.Blobs.StubImageProcessor>());
        }

        /// <summary>Composes and finds one kind, which must exist.</summary>
        /// <param name="compose">The composition.</param>
        /// <param name="kind">The kind.</param>
        /// <returns>The kind's descriptor.</returns>
        private static async Task<BlobKindDescriptor> FindAsync(Action<TellmaBuilder> compose, string kind)
        {
            await using ServiceProvider provider = TestComposition.Compose(compose).BuildServiceProvider(validateScopes: true);
            BlobKindDescriptor? descriptor = provider.GetRequiredService<IBlobKindRegistry>().Find(kind);
            Assert.NotNull(descriptor);
            return descriptor;
        }
    }
}
