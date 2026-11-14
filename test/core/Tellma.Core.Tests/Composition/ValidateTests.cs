// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>Host-free validation of a composition.</summary>
    public class ValidateTests
    {
        [Fact]
        public void A_sound_composition_validates_without_a_host_and_without_running_its_checks()
        {
            IReadOnlyList<CompositionProblem> problems = TellmaComposition.Validate(
                TestComposition.Slug,
                Empty(),
                static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Singleton<IStartupCheck, FailingCheck>())));

            // No public origin is configured: the startup checks would fail.
            Assert.Empty(problems);
        }

        [Fact]
        public void Graph_problems_are_returned_rather_than_thrown()
        {
            IReadOnlyList<CompositionProblem> problems = TellmaComposition.Validate(
                "Acme",
                Empty(),
                static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", declare: d => d.Requires("gl"))));

            Assert.Equal(2, problems.Count);
            Assert.Contains("slug 'Acme'", problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("requires 'gl'", problems[1].Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Realisation_problems_of_a_sound_graph_are_returned()
        {
            IReadOnlyList<CompositionProblem> problems = TellmaComposition.Validate(
                TestComposition.Slug,
                Empty(),
                static tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                    "acme",
                    contribute: c => c.Add(new ServiceContributionItem(typeof(ISampleService), typeof(FirstExtension), null, ServiceLifetimeKind.Singleton)))));

            Assert.Contains("does not implement", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_composition_sees_the_environment_the_configuration_names_else_production()
        {
            string? named = null;
            string? defaulted = null;

            _ = TellmaComposition.Validate(
                TestComposition.Slug,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["environment"] = "Development" }).Build(),
                tellma => named = tellma.Environment.EnvironmentName);
            _ = TellmaComposition.Validate(TestComposition.Slug, Empty(), tellma => defaulted = tellma.Environment.EnvironmentName);

            Assert.Equal("Development", named);
            Assert.Equal("Production", defaulted);
        }

        [Fact]
        public void Each_validation_composes_into_a_fresh_collection_of_its_own()
        {
            List<IServiceCollection> seen = [];
            void Compose(TellmaBuilder tellma)
            {
                seen.Add(tellma.Services);
            }

            Assert.Empty(TellmaComposition.Validate(TestComposition.Slug, Empty(), Compose));
            Assert.Empty(TellmaComposition.Validate(TestComposition.Slug, Empty(), Compose));

            Assert.Equal(2, seen.Count);
            Assert.NotSame(seen[0], seen[1]);
        }

        private static IConfiguration Empty()
        {
            return new ConfigurationBuilder().Build();
        }
    }
}
