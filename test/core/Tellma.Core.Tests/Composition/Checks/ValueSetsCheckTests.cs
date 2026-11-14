// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Data.Metadata;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.value-sets</c>: every set is well formed, names are unique and differ from the
    ///     model's enum names, and every property that draws from a set names a composed one.
    /// </summary>
    public class ValueSetsCheckTests
    {
        private const string Check = "core.value-sets";

        private static readonly ValueSetEntry[] Grades = [new("A", "Grade_A", null), new("B", "Grade_B", null), new("C", null, "C")];

        [Fact]
        public async Task The_platforms_own_sets_pass()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));
        }

        [Fact]
        public async Task The_platforms_sets_pass_with_an_added_language_and_every_language_offered()
        {
            // The language and region sets grow with what the distribution describes.
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddLanguage(new LanguageInfo("ha", "Hausa", "Ha", false), new LanguageFormats("ha", "NG", ["NG", "NE"], null))
                .Languages(["en", "ha"]));

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_sound_set_passes_joins_the_registry_and_serves_the_property_that_draws_from_it()
        {
            static void Compose(TellmaBuilder tellma)
            {
                Graded(tellma, new ValueSetDescriptor("fixture.Grade", Grades, new Dictionary<string, string> { ["Alpha"] = "A" }));
            }

            Assert.Empty((await TestComposition.CheckAsync(Compose)).From(Check));

            await using ServiceProvider provider = TestComposition.Compose(Compose).BuildServiceProvider(validateScopes: true);
            IValueSetRegistry registry = provider.GetRequiredService<IValueSetRegistry>();
            Assert.Equal(["core.Language", "core.Calendar", "core.TimeZone", "core.Region", "fixture.Grade"], registry.All.Select(static set => set.Name));

            // The metadata names the set of each property; the registry resolves it.
            EntityMetadata graded = provider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(GradedItem));
            Assert.Equal("fixture.Grade", graded.Properties.Single(static property => property.Name == "Grade").ValueSet);
            Assert.Equal("core.TimeZone", graded.Properties.Single(static property => property.Name == "Zone").ValueSet);
            Assert.All(
                graded.Properties.Where(static property => property.ValueSet is not null),
                property => Assert.NotNull(registry.Find(property.ValueSet!)));
        }

        [Theory]
        [InlineData("Grade")]
        [InlineData("fixture.grade")]
        [InlineData("Fixture.Grade")]
        [InlineData("fixture.Grade.Level")]
        [InlineData("fixture.Grade_Level")]
        [InlineData("fixture.")]
        [InlineData(".Grade")]
        [InlineData("fixture Grade")]
        [InlineData("fixture.Grade\n")]
        [InlineData("1fixture.Grade")]
        [InlineData("")]
        [InlineData(null)]
        public async Task A_name_that_is_not_a_schema_and_a_pascal_case_name_is_reported(string? name)
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(new ValueSetDescriptor(name!, Grades)));

            Assert.Equal($"The value set '{name}' of the feature 'fixture' is not named <schema>.<PascalCase>.", problem.Problem);
            Assert.Contains("'gl.AccountClass'", problem.Fix, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("gl.AccountClass")]
        [InlineData("my_pack2.X")]
        [InlineData("a.B2b")]
        public async Task A_schema_and_a_pascal_case_name_pass(string name)
        {
            Assert.Empty(await ContributedAsync(new ValueSetDescriptor(name, Grades)));
        }

        [Fact]
        public async Task A_value_listed_twice_without_regard_to_case_is_reported()
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", [new("A", "Grade_A", null), new("a", "Grade_a", null)])));

            Assert.Equal("The value set 'fixture.Grade' of the feature 'fixture' holds the value 'a' more than once, without regard to case.", problem.Problem);
        }

        [Theory]
        [InlineData("Grade_A", "A", "both a label key and a label")]
        [InlineData(null, null, "neither a label key nor a label")]
        public async Task A_value_with_both_or_neither_of_a_label_key_and_a_label_is_reported(string? labelKey, string? label, string expected)
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", [new("A", labelKey, label), new("B", "Grade_B", null)])));

            Assert.Equal($"The value set 'fixture.Grade' of the feature 'fixture' gives the value 'A' {expected}.", problem.Problem);
            Assert.Contains("exactly one of LabelKey", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_entry_without_a_value_is_reported_and_not_thrown()
        {
            List<CompositionProblem> problems = await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", [new("A", "Grade_A", null), new(string.Empty, null, "Empty"), new(null!, null, "Null"), null!]));

            Assert.Equal(3, problems.Count);
            Assert.All(problems, static problem => Assert.Equal("The value set 'fixture.Grade' of the feature 'fixture' holds an entry without a value.", problem.Problem));
        }

        [Fact]
        public async Task A_set_without_a_list_of_values_is_reported_and_not_thrown()
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(new ValueSetDescriptor("fixture.Grade", null!)));

            Assert.Equal("The value set 'fixture.Grade' of the feature 'fixture' has no list of values.", problem.Problem);
        }

        [Fact]
        public async Task An_alias_that_maps_to_no_value_is_reported()
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", Grades, new Dictionary<string, string> { ["Alpha"] = "A", ["Delta"] = "D" })));

            Assert.Equal("The value set 'fixture.Grade' of the feature 'fixture' maps the alias 'Delta' to 'D', which is not one of its values.", problem.Problem);
        }

        [Theory]
        [InlineData("B")]
        [InlineData("b")]
        [InlineData("")]
        public async Task An_alias_that_is_itself_a_value_or_empty_is_reported(string alias)
        {
            CompositionProblem problem = Assert.Single(await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", Grades, new Dictionary<string, string> { [alias] = "A" })));

            Assert.Equal($"The value set 'fixture.Grade' of the feature 'fixture' lists '{alias}' as an alias, but it is empty or one of the set's values.", problem.Problem);
        }

        [Fact]
        public async Task An_alias_maps_to_its_value_without_regard_to_case()
        {
            Assert.Empty(await ContributedAsync(
                new ValueSetDescriptor("fixture.Grade", Grades, new Dictionary<string, string> { ["Alpha"] = "a" })));
        }

        [Fact]
        public async Task Every_problem_of_a_set_is_reported_at_once()
        {
            List<CompositionProblem> problems = await ContributedAsync(new ValueSetDescriptor(
                "grade",
                [new("A", null, null), new("A", "Grade_A", null)],
                new Dictionary<string, string> { ["A"] = "Z" }));

            // The name, the value without a label, the repeated value, and the alias twice over.
            Assert.Equal(5, problems.Count);
        }

        [Theory]
        [InlineData("fixture.Grade")]
        [InlineData("fixture.GRADE")]
        public async Task A_name_two_features_contribute_is_reported_without_regard_to_case(string second)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution.ValueSet(new ValueSetDescriptor("fixture.Grade", Grades))))
                .AddFeature(new TestFeature<Tag.B>("other", contribute: contribution => contribution.ValueSet(new ValueSetDescriptor(second, Grades)))));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal("The value set 'fixture.Grade' is contributed more than once, by the features 'fixture' and 'other'.", problem.Problem);
        }

        [Theory]
        [InlineData("core.Calendar")]
        [InlineData("core.Language")]
        [InlineData("core.TimeZone")]
        [InlineData("core.Region")]
        public async Task A_feature_cannot_contribute_one_of_the_platforms_sets(string name)
        {
            IReadOnlyList<CompositionProblem> problems = await ContributedAsync(new ValueSetDescriptor(name, Grades));

            Assert.Equal($"The value set '{name}' is contributed more than once, by the features 'core' and 'fixture'.", Assert.Single(problems).Problem);
        }

        [Theory]
        [InlineData("fixture.AccountKind")]
        [InlineData("fixture.ACCOUNTKIND")]
        public async Task A_set_named_like_an_enum_of_the_model_is_reported(string name)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution
                    .Entity<Account>()
                    .Model<AccountConfiguration>()
                    .ValueSet(new ValueSetDescriptor(name, Grades)))));

            List<CompositionProblem> valueSets = problems.From(Check);
            Assert.Contains(valueSets, problem => problem.Problem ==
                $"The value set '{name}' of the feature 'fixture' has the name of the enum Tellma.Core.Tests.Data.Metadata.AccountKind.");
        }

        [Fact]
        public async Task A_set_named_like_an_enum_under_another_schema_passes()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<Account>()
                    .Model<AccountConfiguration>()
                    .ValueSet(new ValueSetDescriptor("other.AccountKind", Grades)))));

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_property_that_draws_from_a_set_no_feature_contributes_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => Graded(tellma, set: null));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Equal(
                "The entity Tellma.Core.Tests.Composition.Checks.GradedItem draws Grade from the value set 'fixture.Grade', which no feature contributes.",
                problem.Problem);
            Assert.Contains("contribution.ValueSet(descriptor)", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_property_names_its_set_exactly()
        {
            // 'fixture.GRADE' is a well-formed name and another set; set names are case-sensitive.
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static tellma => Graded(tellma, new ValueSetDescriptor("fixture.GRADE", Grades)));

            Assert.Contains("draws Grade from the value set 'fixture.Grade'", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_hand_built_item_without_a_descriptor_fails_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution.Add(new ValueSetContributionItem(null!)))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("Feature 'fixture' contributes a value set without a descriptor.", problem.Problem);
            Assert.Contains("contribution.ValueSet(", problem.Fix, StringComparison.Ordinal);
        }

        /// <summary>The check's problems for one set a feature contributes on its own.</summary>
        /// <param name="set">The set.</param>
        /// <returns>The problems.</returns>
        private static async Task<List<CompositionProblem>> ContributedAsync(ValueSetDescriptor set)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution.ValueSet(set))));
            return problems.From(Check);
        }

        /// <summary>Composes the entity that draws from <c>fixture.Grade</c>, with or without a set beside it.</summary>
        /// <param name="tellma">The builder.</param>
        /// <param name="set">The set the feature contributes; none when null.</param>
        private static void Graded(TellmaBuilder tellma, ValueSetDescriptor? set)
        {
            _ = tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution =>
            {
                _ = contribution.Entity<GradedItem>();
                if (set is not null)
                {
                    _ = contribution.ValueSet(set);
                }
            }));
        }
    }

    /// <summary>An entity whose grade draws from a feature's value set and whose zone from the platform's.</summary>
    [Table("GradedItems", Schema = "fixture")]
    [TableType]
    public class GradedItem : TopLevelEntity
    {
        [MaxLength(20)]
        [ValueSet("fixture.Grade")]
        public string? Grade { get; set; }

        [MaxLength(64)]
        [ValueSet(CoreValueSets.TimeZone)]
        public string? Zone { get; set; }
    }
}
