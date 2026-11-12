// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>The core feature's client metadata: the distribution, the composed features and the reserved route words.</summary>
    public class CoreClientAssetContributorTests
    {
        [Fact]
        public void The_core_feature_writes_distribution_features_and_routing_into_the_core_chunk()
        {
            ClientAssetContext context = Contribute(TestComposition.Sound(("Tellma:DisplayName", "Acme ERP")));

            Assert.Equal(["distribution", "features", "routing"], context.Sections.Select(static section => section.Name));
            Assert.All(context.Sections, static section => Assert.Equal(ClientMetadataChunk.Core, section.Chunk));

            // --- distribution: the slug, the configured names, both versions ---
            using var distribution = JsonDocument.Parse(context.Sections[0].Json);
            Assert.Equal(TestComposition.Slug, distribution.RootElement.GetProperty("slug").GetString());
            Assert.Equal("Acme ERP", distribution.RootElement.GetProperty("displayName").GetString());
            Assert.Equal(JsonValueKind.Null, distribution.RootElement.GetProperty("copyright").ValueKind);
            Assert.False(string.IsNullOrEmpty(distribution.RootElement.GetProperty("version").GetString()));
            Assert.False(string.IsNullOrEmpty(distribution.RootElement.GetProperty("platformVersion").GetString()));

            // --- features: every composed feature in realisation order, each with a version ---
            using var features = JsonDocument.Parse(context.Sections[1].Json);
            Assert.Equal(["core", "plain"], features.RootElement.EnumerateArray().Select(static feature => feature.GetProperty("key").GetString()));
            Assert.All(features.RootElement.EnumerateArray(), static feature => Assert.False(string.IsNullOrEmpty(feature.GetProperty("version").GetString())));

            // --- routing: the four reserved lists, as declared ---
            using var routing = JsonDocument.Parse(context.Sections[2].Json);
            Assert.Equal(TellmaRoutes.ServerFirstSegments, routing.RootElement.GetProperty("serverFirstSegments").EnumerateArray().Select(static segment => segment.GetString()!));
            Assert.Equal(TellmaRoutes.SpaTenantSegments, routing.RootElement.GetProperty("spaTenantSegments").EnumerateArray().Select(static segment => segment.GetString()!));
        }

        [Fact]
        public void The_copyright_is_written_when_configured()
        {
            ClientAssetContext context = Contribute(TestComposition.Sound(("Tellma:DisplayName", "Acme ERP"), ("Tellma:Copyright", "© 2026 Acme")));

            using var distribution = JsonDocument.Parse(context.Sections[0].Json);
            Assert.Equal("© 2026 Acme", distribution.RootElement.GetProperty("copyright").GetString());
        }

        [Fact]
        public void A_missing_display_name_fails_the_contribution()
        {
            InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => Contribute(TestComposition.SoundConfiguration));

            Assert.Contains("Tellma:DisplayName", failure.Message, StringComparison.Ordinal);
        }

        /// <summary>Runs every contributor of a composition with the plain feature over the given configuration.</summary>
        /// <param name="configuration">The configuration.</param>
        /// <returns>The emit's context.</returns>
        private static ClientAssetContext Contribute(IReadOnlyDictionary<string, string?> configuration)
        {
            using ServiceProvider provider = TestComposition
                .Compose(static tellma => tellma.AddFeature(new PlainFeature()), TestComposition.Configuration(configuration))
                .BuildServiceProvider();

            ClientAssetContext context = new(provider);
            foreach (IClientAssetContributor contributor in provider.GetServices<IClientAssetContributor>())
            {
                contributor.Contribute(context);
            }

            return context;
        }
    }
}
