// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>The graph gate: every problem of the feature graph is thrown at once from the composition root.</summary>
    public class GraphGateTests
    {
        private const string Source = "core.composition";

        [Fact]
        public void A_requirement_no_added_feature_has_is_reported_naming_the_feature_that_requires_it()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("acme", declare: d => d.Requires("gl"))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal(Source, problem.Source);
            Assert.Contains("'acme'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("requires 'gl'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("AddFeature<TFeature>()", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_problem_is_thrown_at_once_in_one_exception_with_one_message_line_each()
        {
            TellmaCompositionException exception = Assert.Throws<TellmaCompositionException>(() => TestComposition.Compose(
                tellma => tellma
                    .AddFeature(new TestFeature<Tag.A>("acme", declare: d => d.Requires("missing").Options<SampleOptions>("Tellma:One")))
                    .AddFeature(new TestFeature<Tag.B>("acme"))
                    .AddFeature(new TestFeature<Tag.C>("left", declare: d => d.Requires("right").Options<SampleOptions>("Tellma:Two")))
                    .AddFeature(new TestFeature<Tag.D>("right", declare: d => d.Requires("left"), contribute: c => c.Add(new UnknownItem())))
                    .AddFeature(new TestFeature<Tag.A>("again")),
                slug: "Not-A-Slug"));

            // Slug, second instance, duplicate name, unknown requirement, cycle, options, unknown item.
            Assert.Equal(7, exception.Problems.Count);
            Assert.All(exception.Problems, problem => Assert.False(string.IsNullOrWhiteSpace(problem.Fix)));

            string[] lines = exception.Message.Split('\n');
            Assert.Equal(exception.Problems.Count + 1, lines.Length);
            Assert.Equal("The Tellma composition has 7 problems:", lines[0]);
            string[] expected = [.. exception.Problems.Select(static p => "- " + p.Describe())];
            Assert.Equal(expected, lines[1..]);
        }

        [Fact]
        public void The_graph_gate_throws_before_any_item_is_realised()
        {
            ServiceCollection services = new();

            _ = Assert.Throws<TellmaCompositionException>(() => TestComposition.Compose(
                tellma => tellma.AddFeature(new TestFeature<Tag.A>(
                    "acme",
                    declare: d => d.Requires("missing"),
                    contribute: c => c.Singleton<ISampleService, SampleService>())),
                services: services));

            Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(ISampleService));
            Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService));
        }

        [Fact]
        public void A_two_feature_cycle_is_reported_with_its_path()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("left", declare: d => d.Requires("right")))
                .AddFeature(new TestFeature<Tag.B>("right", declare: d => d.Requires("left"))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("'left' -> 'right' -> 'left'", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_feature_that_requires_itself_is_a_cycle()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("acme", declare: d => d.Requires("acme"))));

            Assert.Contains("'acme' -> 'acme'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Each_separate_cycle_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("a", declare: d => d.Requires("b")))
                .AddFeature(new TestFeature<Tag.B>("b", declare: d => d.Requires("a")))
                .AddFeature(new TestFeature<Tag.C>("c", declare: d => d.Requires("d")))
                .AddFeature(new TestFeature<Tag.D>("d", declare: d => d.Requires("c"))));

            Assert.Equal(2, problems.Count);
            Assert.Contains("'a' -> 'b' -> 'a'", problems[0].Problem, StringComparison.Ordinal);
            Assert.Contains("'c' -> 'd' -> 'c'", problems[1].Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_features_with_one_name_are_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("gl"))
                .AddFeature(new TestFeature<Tag.B>("gl")));

            Assert.Contains("both have the name 'gl'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_feature_named_core_collides_with_the_platform_feature()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("core")));

            Assert.Contains("both have the name 'core'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_feature_type_added_as_two_instances_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme"))
                .AddFeature(new TestFeature<Tag.A>("acme")));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("added twice, as two different instances", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_instance_added_twice_and_a_type_added_twice_are_no_ops()
        {
            TestFeature<Tag.A> feature = new("acme");

            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(feature)
                .AddFeature(feature)
                .AddFeature<PlainFeature>()
                .AddFeature<PlainFeature>()
                .AddFeature<CoreFeature>());

            string[] expected = ["core", "acme", "plain"];
            Assert.Equal(expected, Record(services).Features.Select(static f => f.Name));
        }

        [Fact]
        public void An_options_type_bound_to_two_paths_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("gl", declare: d => d.Options<SampleOptions>("Tellma:Gl")))
                .AddFeature(new TestFeature<Tag.B>("acme", declare: d => d.Options<SampleOptions>("Tellma:Acme"))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("'Tellma:Gl' (by 'gl') and 'Tellma:Acme' (by 'acme')", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void One_options_binding_declared_twice_binds_once()
        {
            IServiceCollection services = TestComposition.Compose(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("gl", declare: d => d.Options<SampleOptions>("Tellma:Gl")))
                .AddFeature(new TestFeature<Tag.B>("acme", declare: d => d.Options<SampleOptions>("Tellma:Gl"))));

            Assert.Single(Record(services).BoundOptions, static bound => bound.OptionsType == typeof(SampleOptions));
        }

        [Theory]
        [InlineData("Acme")]
        [InlineData("1acme")]
        [InlineData("-acme")]
        [InlineData("acme_pharma")]
        [InlineData("acme.pharma")]
        [InlineData("abcdefghijklmnop")]
        [InlineData("")]
        public void A_slug_that_is_not_lowercase_letter_first_and_at_most_15_characters_is_reported(string slug)
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static _ => { }, slug: slug);

            Assert.Contains($"slug '{slug}'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("acme")]
        [InlineData("et-pharma")]
        [InlineData("a1")]
        [InlineData("abcdefghijklmno")]
        public void A_valid_slug_composes(string slug)
        {
            IServiceCollection services = TestComposition.Compose(static _ => { }, slug: slug);

            Assert.Equal(slug, Record(services).Slug);
        }

        [Theory]
        [InlineData("Gl")]
        [InlineData("gl..sales")]
        [InlineData("1gl")]
        [InlineData("gl.")]
        [InlineData("gl_sales")]
        [InlineData("")]
        public void A_feature_name_that_is_not_lowercase_dotted_segments_is_reported(string name)
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>(name)));

            Assert.Contains("is not lowercase dotted segments", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_item_no_realizer_handles_is_reported_naming_its_type()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: c => c.Add(new UnknownItem()))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains(typeof(UnknownItem).FullName!.Replace('+', '.'), problem.Problem, StringComparison.Ordinal);
            Assert.Contains("no realizer", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Nested_features_join_the_graph_gate()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>(
                    "acme",
                    contribute: c => c.Feature(new TestFeature<Tag.B>("acme.sales", declare: d => d.Requires("missing"))))));

            Assert.Contains("'acme.sales'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_nested_feature_that_requires_its_parent_is_a_cycle()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>(
                    "acme",
                    contribute: c => c.Feature(new TestFeature<Tag.B>("acme.sales", declare: d => d.Requires("acme"))))));

            Assert.Contains("'acme' -> 'acme.sales' -> 'acme'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_requirement_given_by_attribute_and_by_declaration_counts_once()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.AddFeature<AttributedFeature>());

            // One missing 'gl', not two.
            Assert.Contains("requires 'gl'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_second_leaf_for_one_default_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .UseEntity<SampleEntity, SampleLeaf>()
                .UseEntity<SampleEntity, SampleLeaf>()
                .UseEntity<SampleEntity, OtherLeaf>());

            Assert.Contains("was given two leaves", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void An_environment_name_that_makes_no_deployment_id_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(
                static _ => { },
                environment: new TestEnvironment("My Environment"));

            Assert.Contains("'My Environment'", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void Selections_after_the_composition_method_returns_are_refused()
        {
            TellmaBuilder? captured = null;
            _ = TestComposition.Compose(tellma => captured = tellma);

            _ = Assert.Throws<InvalidOperationException>(() => captured!.AddFeature<PlainFeature>());
            _ = Assert.Throws<InvalidOperationException>(() => captured!.UseEntity<SampleEntity, SampleLeaf>());
        }

        [Fact]
        public void A_second_composition_on_one_service_collection_is_refused()
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });

            _ = Assert.Throws<InvalidOperationException>(() => TestComposition.Compose(static _ => { }, services: services));
        }

        private static TellmaCompositionRecord Record(IServiceCollection services)
        {
            return (TellmaCompositionRecord)services.Single(static d => d.ServiceType == typeof(TellmaCompositionRecord)).ImplementationInstance!;
        }

        /// <summary>An item kind no realizer of the platform knows.</summary>
        private sealed record UnknownItem : FeatureContributionItem;
    }
}
