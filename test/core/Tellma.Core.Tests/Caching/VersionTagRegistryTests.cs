// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Data.Metadata;
using Tellma.Core.Tests.Data.Metadata;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Caching
{
    /// <summary>
    ///     The version-tag registry over entity metadata: every name a tenant database carries, which
    ///     tags each written table bumps, the fixed rules of the preference tables, every declaration
    ///     it refuses, and the startup check that reports them.
    /// </summary>
    public sealed class VersionTagRegistryTests
    {
        private const string Check = "core.version-tags";

        private static readonly TableName Currencies = new("vt", "Currencies");

        private static readonly TableName Units = new("vt", "Units");

        private static readonly TableName Lists = new("vt", "Lists");

        private static readonly TableName Profiles = new("vt", "Profiles");

        private static readonly TableName Grants = new("vt", "Grants");

        [Fact]
        public void Every_name_is_the_platforms_three_with_the_declared_and_the_implied_ones_in_ordinal_order()
        {
            VersionTagRegistry registry = Sound();

            Assert.Empty(registry.Problems);
            Assert.Equal(["currencies", "entities", "entity:vt.VtList", "permissions", "settings", "units"], registry.Names);
        }

        [Fact]
        public void A_registry_over_no_entity_carries_the_platforms_three_names()
        {
            VersionTagRegistry registry = new([]);

            Assert.Equal([VersionTagNames.Entities, VersionTagNames.Permissions, VersionTagNames.Settings], registry.Names);
        }

        [Fact]
        public void A_written_table_resolves_to_its_declared_names_once_each_in_ordinal_order()
        {
            VersionTagRegistry registry = Sound();

            Assert.Equal(["currencies", "units"], registry.Resolve(new HashSet<TableName> { Units }).TenantNames);
            Assert.Equal(["currencies", "units"], registry.Resolve(new HashSet<TableName> { Units, Currencies }).TenantNames);
            Assert.Empty(registry.Resolve(new HashSet<TableName> { Units }).UserRules);
        }

        [Fact]
        public void A_cacheable_entitys_table_bumps_its_own_tag_and_the_entities_tag()
        {
            VersionTagEffects effects = Sound().Resolve(new HashSet<TableName> { Lists });

            Assert.Equal([VersionTagNames.Entities, VersionTagNames.ForEntity("vt.VtList")], effects.TenantNames);
        }

        [Fact]
        public void A_table_that_bumps_nothing_resolves_to_no_effect()
        {
            Assert.Same(VersionTagEffects.None, Sound().Resolve(new HashSet<TableName> { new("vt", "Elsewhere") }));
            Assert.Same(VersionTagEffects.None, Sound().Resolve(new HashSet<TableName>()));
        }

        [Fact]
        public void User_level_rules_name_the_column_of_the_declared_property_and_come_in_table_then_column_order()
        {
            VersionTagEffects effects = Sound().Resolve(new HashSet<TableName> { Profiles, Grants, VersionTagRegistry.UserPreferences });

            Assert.Empty(effects.TenantNames);
            Assert.Equal(
                [
                    new UserVersionTagRule(VersionTagRegistry.UserPreferences, UserVersionTagNames.Preferences, "UserId"),
                    new UserVersionTagRule(Grants, UserVersionTagNames.Permissions, "GranteeId"),
                    new UserVersionTagRule(Profiles, UserVersionTagNames.Preferences, "UserId"),
                ],
                effects.UserRules);
        }

        [Fact]
        public void The_two_preference_tables_bump_their_users_preferences_tag_with_no_entity_declaring_it()
        {
            VersionTagRegistry registry = new([]);

            foreach (TableName table in new[] { VersionTagRegistry.UserPreferences, VersionTagRegistry.NotificationPreferences })
            {
                Assert.Equal(
                    [new UserVersionTagRule(table, UserVersionTagNames.Preferences, "UserId")],
                    registry.Resolve(new HashSet<TableName> { table }).UserRules);
            }
        }

        [Theory]
        [InlineData(typeof(VtHandNamed), "tags named 'entity:…' belong to cacheable entities")]
        [InlineData(typeof(VtUpperNamed), "'Currencies', which is not a lower-case name")]
        [InlineData(typeof(VtDigitNamed), "'9lives', which is not a lower-case name")]
        [InlineData(typeof(VtOwnerless), "its property 'Owner' names, but it has no mapped int property of that name")]
        [InlineData(typeof(VtLabelled), "its property 'Label' names, but it has no mapped int property of that name")]
        [InlineData(typeof(VtUnknownUserTag), "a user-level version tag 7, which is not one of Permissions, Preferences")]
        [InlineData(typeof(VtAListWhoseNameIsSoLongThatItsImpliedVersionTagNameCannotFitTheColumnThatStoresTheTagsOfEveryTenantDatabaseAtAllEvenNow), "longer than 128 characters")]
        public void A_refused_declaration_is_one_problem_and_contributes_nothing(Type entity, string expected)
        {
            VersionTagRegistry registry = Over(entity);

            CompositionProblem problem = Assert.Single(registry.Problems);
            Assert.Equal(Check, problem.Source);
            Assert.Contains(expected, problem.Problem, StringComparison.Ordinal);
            Assert.Equal([VersionTagNames.Entities, VersionTagNames.Permissions, VersionTagNames.Settings], registry.Names);
            Assert.Same(VersionTagEffects.None, registry.Resolve(new HashSet<TableName> { Provider(entity).Get(entity).Table }));
        }

        [Fact]
        public void Two_names_that_differ_in_case_alone_are_refused_since_the_database_stores_them_in_one_row()
        {
            // The entity metadata refuses two such entities in one model, so the registry is handed two models' entities.
            VersionTagRegistry registry = new([.. Provider(typeof(VtList)).All, .. Provider(typeof(VTLIST)).All.Where(static entity => entity.ClrType == typeof(VTLIST))]);

            CompositionProblem problem = Assert.Single(registry.Problems);
            Assert.Contains("'entity:vt.VTLIST' and 'entity:vt.VtList' differ in case alone", problem.Problem, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("currencies", true)]
        [InlineData("a.b-c9", true)]
        [InlineData("x", true)]
        [InlineData("", false)]
        [InlineData("Currencies", false)]
        [InlineData("9lives", false)]
        [InlineData("-a", false)]
        [InlineData("a_b", false)]
        [InlineData("a b", false)]
        [InlineData("a\n", false)]
        [InlineData("entity:x", false)]
        public void A_plain_name_is_lower_case_ascii_starting_with_a_letter(string name, bool plain)
        {
            Assert.Equal(plain, VersionTagRegistry.IsPlainName(name));
        }

        [Fact]
        public void A_plain_name_holds_at_most_128_characters()
        {
            Assert.True(VersionTagRegistry.IsPlainName(new string('a', 128)));
            Assert.False(VersionTagRegistry.IsPlainName(new string('a', 129)));
        }

        [Fact]
        public async Task The_startup_check_reports_every_refused_declaration_of_a_composition()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<VtCurrency>()
                    .Entity<VtHandNamed>()
                    .Entity<VtOwnerless>())));

            Assert.Empty(problems.From("core.entity-metadata"));
            Assert.Equal(2, problems.From(Check).Count);
        }

        [Fact]
        public async Task The_startup_check_passes_a_sound_composition()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Entity<VtCurrency>()
                    .Entity<VtUnit>()
                    .Entity<VtList>()
                    .Entity<VtProfile>()
                    .Entity<VtGrant>())));

            Assert.Empty(problems.From(Check));
        }

        /// <summary>The registry over the sound entities.</summary>
        /// <returns>The registry.</returns>
        private static VersionTagRegistry Sound()
        {
            return Over(typeof(VtCurrency), typeof(VtUnit), typeof(VtList), typeof(VtProfile), typeof(VtGrant));
        }

        /// <summary>The registry over the metadata of the platform stand-ins and the given entities.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>The registry.</returns>
        private static VersionTagRegistry Over(params Type[] entities)
        {
            return new VersionTagRegistry(Provider(entities));
        }

        /// <summary>The metadata of the platform stand-ins and the given entities, which must be sound.</summary>
        /// <param name="entities">The entities.</param>
        /// <returns>The provider.</returns>
        private static EntityMetadataProvider Provider(params Type[] entities)
        {
            EntityMetadataBuild build = MetadataModels.Build(entities);
            Assert.Empty(build.Problems);
            return build.Provider!;
        }
    }
}
