// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Immutable;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Tests.Infrastructure;
using TypedSettings = Tellma.Core.Abstractions.Settings.Settings;

namespace Tellma.Core.Tests.Settings
{
    /// <summary>
    ///     <c>core.setting-keys</c> and the contribution it checks: each key's name, visibility, type,
    ///     default, secrecy, category, group and reference, and the names and category spellings
    ///     across keys; the registry built from the same contribution.
    /// </summary>
    public class SettingKeysCheckTests
    {
        private const string Check = "core.setting-keys";

        // --- Sound keys ---

        [Fact]
        public async Task Sound_keys_pass_and_the_registry_lists_them_in_declaration_order()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma.AddFeature(Feature(typeof(SoundKeys))));

            Assert.Empty(problems.From(Check));

            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma.AddFeature(Feature(typeof(SoundKeys)))).BuildServiceProvider(validateScopes: true);
            ISettingKeyRegistry registry = provider.GetRequiredService<ISettingKeyRegistry>();
            Assert.Equal(
                ["acme.posting.autoNumber", "acme.posting.width", "acme.tax.code", "acme.period", "acme.centers", "acme.defaultRole", "acme.erp.apiKey", "acme.erp.hidden"],
                registry.Keys.Select(static key => key.Name));
            Assert.Same(SoundKeys.Width, registry.Find("acme.posting.width"));
            Assert.Null(registry.Find("Acme.Posting.Width"));
        }

        [Fact]
        public async Task A_holder_contributed_twice_adds_each_key_once()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c.SettingKeys(typeof(SoundKeys)).SettingKeys(typeof(SoundKeys)))));

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task The_tabs_open_with_general_then_follow_each_category_as_its_first_key_appears()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(Feature(typeof(CategorizedKeys))))
                .BuildServiceProvider(validateScopes: true);
            IReadOnlyList<SettingCategoryDescriptor> categories = provider.GetRequiredService<ISettingKeyRegistry>().Categories;

            Assert.Equal(["general", "sales", "gl"], categories.Select(static category => category.Name));
            Assert.Equal([0, 1, 2], categories.Select(static category => category.Order));
            Assert.Equal([CoreResources.SettingsGeneral, "core.Settings.sales", "core.Settings.gl"], categories.Select(static category => category.Resource));
            Assert.Equal(["SettingCategory_general", "SettingCategory_sales", "SettingCategory_gl"], categories.Select(static category => category.LabelKey));
        }

        [Fact]
        public async Task A_composition_without_keys_has_the_general_tab_alone()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            ISettingKeyRegistry registry = provider.GetRequiredService<ISettingKeyRegistry>();

            Assert.Empty(registry.Keys);
            Assert.Equal("general", Assert.Single(registry.Categories).Name);
            Assert.Empty(registry.Describe());
        }

        // --- One key's rules ---

        [Fact]
        public async Task A_field_that_holds_no_key_is_reported()
        {
            CompositionProblem problem = Assert.Single(await Problems(typeof(EmptyField)));

            Assert.Contains("EmptyField.Missing", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("holds no key", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(typeof(SingleSegment), "'acme'")]
        [InlineData(typeof(UpperSegment), "'acme.Width'")]
        [InlineData(typeof(UnderscoredName), "'acme.posting_width'")]
        [InlineData(typeof(EmptySegment), "'acme..width'")]
        [InlineData(typeof(LongName), "at most 128")]
        public async Task A_name_that_is_not_dotted_camel_case_of_two_segments_is_reported(Type holder, string expected)
        {
            CompositionProblem problem = Assert.Single(await Problems(holder), static problem => problem.Problem.Contains("is named", StringComparison.Ordinal));

            Assert.Contains(expected, problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_undefined_visibility_is_reported()
        {
            CompositionProblem problem = Assert.Single(await Problems(typeof(UndefinedVisibility)));

            Assert.Contains("not a defined visibility", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(typeof(DateTimeKey), "DateTime")]
        [InlineData(typeof(NullableElements), "ImmutableArray`1")]
        [InlineData(typeof(ListKey), "List`1")]
        public async Task A_type_a_key_holds_only_with_its_own_contract_is_reported(Type holder, string typeName)
        {
            CompositionProblem problem = Assert.Single(await Problems(holder));

            Assert.Contains($"holds values of {typeName}", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_nullable_key_with_a_default_and_a_non_nullable_key_without_one_are_reported()
        {
            List<CompositionProblem> problems = await Problems(typeof(Defaults));

            Assert.Equal(4, problems.Count);
            Assert.Single(problems, static problem => problem.Problem.Contains("Defaults.NullableInt", StringComparison.Ordinal) && problem.Problem.Contains("admits null but has a default", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("Defaults.NullableText", StringComparison.Ordinal) && problem.Problem.Contains("admits null but has a default", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("Defaults.Text", StringComparison.Ordinal) && problem.Problem.Contains("admits no null", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("Defaults.Codes", StringComparison.Ordinal) && problem.Problem.Contains("admits null but has a default", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_secret_with_a_default_or_a_reference_is_reported()
        {
            List<CompositionProblem> problems = await Problems(typeof(Secrets));

            Assert.Single(problems, static problem => problem.Problem.Contains("Secrets.Port", StringComparison.Ordinal) && problem.Problem.Contains("secret with a default", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("Secrets.Role", StringComparison.Ordinal) && problem.Problem.Contains("secret that references", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_category_or_group_that_is_not_camel_case_is_reported()
        {
            List<CompositionProblem> problems = await Problems(typeof(Placement));

            Assert.Equal(3, problems.Count);
            Assert.Single(problems, static problem => problem.Problem.Contains("category 'Gl'", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("group 'posting.'", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("Placement.LongGroup", StringComparison.Ordinal) && problem.Problem.Contains("at most 64", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_reference_to_an_int_keyed_stack_passes_and_any_other_is_reported()
        {
            List<CompositionProblem> problems = await Problems(typeof(References));

            Assert.Equal(3, problems.Count);
            Assert.Single(problems, static problem => problem.Problem.Contains("References.ByName", StringComparison.Ordinal) && problem.Problem.Contains("holds values of String", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("References.Settings", StringComparison.Ordinal) && problem.Problem.Contains("not an entity with an int-keyed stack", StringComparison.Ordinal));
            Assert.Single(problems, static problem => problem.Problem.Contains("References.NotAnEntity", StringComparison.Ordinal) && problem.Problem.Contains("not an entity with an int-keyed stack", StringComparison.Ordinal));
        }

        // --- Across keys ---

        [Fact]
        public async Task A_name_declared_twice_is_reported_once_with_both_fields()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c.SettingKeys(typeof(SoundKeys)).SettingKeys(typeof(Duplicate)))));

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Contains("'acme.posting.width' is declared more than once", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("SoundKeys.Width and ", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Duplicate.Width", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_category_spelled_two_ways_is_reported()
        {
            CompositionProblem problem = Assert.Single(await Problems(typeof(Spellings)));

            Assert.Contains("'sales' and 'saleS'", problem.Problem, StringComparison.Ordinal);
        }

        // --- The contribution ---

        [Fact]
        public void A_holder_that_is_an_interface_or_open_generic_fails_the_composition()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c.SettingKeys(typeof(IHolder)).SettingKeys(typeof(OpenHolder<>)))));

            Assert.Equal(2, problems.Count(static problem => problem.Problem.Contains("contributes the setting keys of", StringComparison.Ordinal)));
        }

        /// <summary>A feature that contributes one holder's keys.</summary>
        /// <param name="holder">The holder.</param>
        /// <returns>The feature.</returns>
        internal static TestFeature<Tag.A> Feature(Type holder)
        {
            return new TestFeature<Tag.A>("acme", contribute: c => c.SettingKeys(holder));
        }

        /// <summary>The check's problems for one holder's keys.</summary>
        /// <param name="holder">The holder.</param>
        /// <returns>The problems.</returns>
        private static async Task<List<CompositionProblem>> Problems(Type holder)
        {
            return (await TestComposition.CheckAsync(tellma => tellma.AddFeature(Feature(holder)))).From(Check);
        }

        /// <summary>A period an enum key holds.</summary>
        internal enum Period
        {
            /// <summary>Monthly.</summary>
            Monthly,

            /// <summary>Quarterly.</summary>
            Quarterly,
        }

        /// <summary>Keys of every admitted shape, each sound.</summary>
        internal static class SoundKeys
        {
            /// <summary>A flag.</summary>
            public static readonly SettingKey<bool> AutoNumber = new("acme.posting.autoNumber", true, visibility: SettingVisibility.Client);

            /// <summary>A validated width.</summary>
            public static readonly SettingKey<int> Width = new("acme.posting.width", 6, validate: static width => width is >= 1 and <= 12 ? null : "Acme.WidthOutOfRange");

            /// <summary>Optional text.</summary>
            public static readonly SettingKey<string?> TaxCode = new("acme.tax.code", null, group: "tax");

            /// <summary>An enum.</summary>
            public static readonly SettingKey<Period> Cadence = new("acme.period", Period.Monthly, visibility: SettingVisibility.Client);

            /// <summary>Ids of an int-keyed stack.</summary>
            public static readonly SettingKey<ImmutableArray<int>> Centers = new("acme.centers", [], references: typeof(Role));

            /// <summary>An optional id of an int-keyed stack.</summary>
            public static readonly SettingKey<int?> DefaultRole = new("acme.defaultRole", null, references: typeof(Role));

            /// <summary>A secret.</summary>
            public static readonly SettingKey<string?> ApiKey = new("acme.erp.apiKey", null, visibility: SettingVisibility.Secret);

            /// <summary>A server key of a value type that admits null.</summary>
            public static readonly SettingKey<long?> Hidden = new("acme.erp.hidden", null);

            /// <summary>Not a key: an instance field.</summary>
            public static readonly int NotAKey = 3;
        }

        /// <summary>Keys of two categories after the general one.</summary>
        internal static class CategorizedKeys
        {
            /// <summary>A sales key.</summary>
            public static readonly SettingKey<bool> Discounts = new("sales.discounts", false);

            /// <summary>A general key, stated so.</summary>
            public static readonly SettingKey<bool> Tour = new("acme.tour", false, category: "general");

            /// <summary>A GL key.</summary>
            public static readonly SettingKey<bool> Posting = new("gl.posting.auto", false);

            /// <summary>Another sales key.</summary>
            public static readonly SettingKey<bool> Returns = new("sales.returns", false);
        }

        /// <summary>A field left null.</summary>
        internal static class EmptyField
        {
            /// <summary>Holds nothing.</summary>
            public static readonly SettingKey<bool> Missing = null!;
        }

        /// <summary>A name of one segment.</summary>
        internal static class SingleSegment
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme", false);
        }

        /// <summary>A segment that opens upper case.</summary>
        internal static class UpperSegment
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme.Width", false);
        }

        /// <summary>A segment with an underscore.</summary>
        internal static class UnderscoredName
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme.posting_width", false);
        }

        /// <summary>An empty segment.</summary>
        internal static class EmptySegment
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme..width", false, category: "acme");
        }

        /// <summary>A name longer than a key is.</summary>
        internal static class LongName
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme." + new string('a', 124), false);
        }

        /// <summary>A visibility outside the enum.</summary>
        internal static class UndefinedVisibility
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme.flag", false, visibility: (SettingVisibility)9);
        }

        /// <summary>A date and time, which no key holds without its own contract.</summary>
        internal static class DateTimeKey
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<DateTime> Key = new("acme.when", DateTime.UnixEpoch);
        }

        /// <summary>An array of nullable elements.</summary>
        internal static class NullableElements
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<ImmutableArray<int?>> Key = new("acme.ids", []);
        }

        /// <summary>A mutable list.</summary>
        internal static class ListKey
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<List<int>> Key = new("acme.ids", []);
        }

        /// <summary>Defaults that contradict their keys' nullability.</summary>
        internal static class Defaults
        {
            /// <summary>Admits null, defaults to a number.</summary>
            public static readonly SettingKey<int?> NullableInt = new("acme.count", 5);

            /// <summary>Admits null, defaults to text.</summary>
            public static readonly SettingKey<string?> NullableText = new("acme.label", "x");

            /// <summary>Admits no null, defaults to null.</summary>
            public static readonly SettingKey<string> Text = new("acme.title", null!);

            /// <summary>An array that admits null, defaults to an empty one.</summary>
            public static readonly SettingKey<ImmutableArray<int>?> Codes = new("acme.codes", ImmutableArray<int>.Empty);
        }

        /// <summary>Secrets that break the secret rules.</summary>
        internal static class Secrets
        {
            /// <summary>A secret with a default.</summary>
            public static readonly SettingKey<int> Port = new("acme.erp.port", 1234, visibility: SettingVisibility.Secret);

            /// <summary>A secret that references an entity.</summary>
            public static readonly SettingKey<int?> Role = new("acme.erp.role", null, visibility: SettingVisibility.Secret, references: typeof(Role));
        }

        /// <summary>Categories and groups outside the grammar.</summary>
        internal static class Placement
        {
            /// <summary>A category that opens upper case.</summary>
            public static readonly SettingKey<bool> Category = new("acme.flag", false, category: "Gl");

            /// <summary>A group with an empty segment.</summary>
            public static readonly SettingKey<bool> Group = new("acme.other", false, group: "posting.");

            /// <summary>A group longer than a group is.</summary>
            public static readonly SettingKey<bool> LongGroup = new("acme.third", false, group: new string('g', 65));
        }

        /// <summary>References that do not hold ids of an int-keyed stack.</summary>
        internal static class References
        {
            /// <summary>Sound.</summary>
            public static readonly SettingKey<int> Role = new("acme.role", 1, references: typeof(Role));

            /// <summary>Text cannot hold an id.</summary>
            public static readonly SettingKey<string?> ByName = new("acme.roleName", null, references: typeof(Role));

            /// <summary>An entity without a stack.</summary>
            public static readonly SettingKey<int?> Settings = new("acme.settings", null, references: typeof(TypedSettings));

            /// <summary>Not an entity.</summary>
            public static readonly SettingKey<int?> NotAnEntity = new("acme.other", null, references: typeof(Period));
        }

        /// <summary>A key the sound holder declares as well.</summary>
        internal static class Duplicate
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<int> Width = new("acme.posting.width", 8);
        }

        /// <summary>One category spelled two ways.</summary>
        internal static class Spellings
        {
            /// <summary>The first spelling.</summary>
            public static readonly SettingKey<bool> First = new("sales.discounts", false);

            /// <summary>The second spelling.</summary>
            public static readonly SettingKey<bool> Second = new("sales.returns", false, category: "saleS");
        }

        /// <summary>An interface, whose fields cannot hold keys.</summary>
        internal interface IHolder;

        /// <summary>An open generic holder.</summary>
        /// <typeparam name="T">Anything.</typeparam>
        internal static class OpenHolder<T>
        {
            /// <summary>The key.</summary>
            public static readonly SettingKey<bool> Key = new("acme.open", false);
        }
    }
}
