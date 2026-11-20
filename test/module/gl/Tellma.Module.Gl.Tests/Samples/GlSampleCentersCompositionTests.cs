// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Module.Gl.Tests.Samples
{
    /// <summary>
    ///     The sample centers a composition provisions: GL's default list on its own, and the list of a
    ///     feature that requires GL and registers its own in its place, which the step the composition
    ///     resolves then writes and versions by.
    /// </summary>
    public sealed class GlSampleCentersCompositionTests
    {
        [Fact]
        public async Task Alone_the_feature_provisions_its_default_centers()
        {
            await using ServiceProvider provider = GlComposition.Build();
            await using AsyncServiceScope scope = provider.CreateAsyncScope();

            Assert.IsType<DefaultGlSampleCenters>(scope.ServiceProvider.GetRequiredService<IGlSampleCenters>());
            Assert.Equal(new DefaultGlSampleCenters().Version, scope.ServiceProvider.GetRequiredService<GlSampleCentersStep<Center>>().Version);
        }

        [Fact]
        public async Task A_dependent_features_centers_replace_the_default_in_the_step_the_composition_resolves()
        {
            await using ServiceProvider provider = GlComposition.Build(static tellma => tellma.AddFeature(new ReplacingFeature()));
            await using AsyncServiceScope scope = provider.CreateAsyncScope();

            // --- The step the provisioning runner creates: the platform registers it under its own type ---
            GlSampleCentersStep<Center> step = scope.ServiceProvider.GetRequiredService<GlSampleCentersStep<Center>>();

            Assert.IsType<ReplacementCenters>(scope.ServiceProvider.GetRequiredService<IGlSampleCenters>());
            Assert.Equal(ReplacementCenters.ListVersion, step.Version);
        }

        /// <summary>A distribution's feature that requires GL and ships its own sample centers.</summary>
        private sealed class ReplacingFeature : ITellmaFeature
        {
            /// <inheritdoc />
            public string Name => "gl-replacement";

            /// <inheritdoc />
            public void Declare(FeatureDeclaration declaration)
            {
                ArgumentNullException.ThrowIfNull(declaration);

                // Requiring GL orders this registration after GL's default, so it replaces it.
                _ = declaration.Requires(GlModule.FeatureName);
            }

            /// <inheritdoc />
            public void Contribute(FeatureContribution contribution)
            {
                ArgumentNullException.ThrowIfNull(contribution);
                _ = contribution.Singleton<IGlSampleCenters, ReplacementCenters>();
            }
        }

        /// <summary>The replacement list: one root, at a version the default never takes.</summary>
        private sealed class ReplacementCenters : IGlSampleCenters
        {
            /// <summary>The list's version.</summary>
            public const int ListVersion = 7;

            /// <inheritdoc />
            public int Version => ListVersion;

            /// <inheritdoc />
            public IReadOnlyList<SampleCenter> Centers { get; } =
                [new SampleCenter("ROOT", [new SampleCenterName("en", "Root")], CenterType.Abstract, null)];
        }
    }
}
