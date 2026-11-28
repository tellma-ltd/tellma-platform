// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Tellma.Core.Abstractions.Client;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Dashboards;
using Tellma.Core.Composition;
using Tellma.Core.Dashboards;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Dashboards
{
    /// <summary>
    ///     The dashboard widget types: the registry as composed, the <c>core.widget-types</c> startup
    ///     check, and the <c>widgetTypes</c> section of the client metadata.
    /// </summary>
    public class WidgetTypesTests
    {
        private const string Check = "core.widget-types";

        // --- The registry, as composed ---

        [Fact]
        public async Task The_core_types_come_first_and_a_features_types_follow_in_its_order()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .WidgetType<CashPositionSettings>("fixture.cash-position")
                    .WidgetType("fixture.overdue"))))
                .BuildServiceProvider(validateScopes: true);

            IWidgetTypeRegistry registry = provider.GetRequiredService<IWidgetTypeRegistry>();

            Assert.Equal(
                [
                    new WidgetTypeDescriptor("core.assignments-to-me", "core", null, WidgetSubject.None),
                    new WidgetTypeDescriptor("core.assignments-by-me", "core", null, WidgetSubject.None),
                    new WidgetTypeDescriptor("core.recents", "core", null, WidgetSubject.None),
                    new WidgetTypeDescriptor("fixture.cash-position", "fixture", typeof(CashPositionSettings), WidgetSubject.None),
                    new WidgetTypeDescriptor("fixture.overdue", "fixture", null, WidgetSubject.None),
                ],
                registry.Types);
        }

        [Fact]
        public async Task A_type_is_found_by_its_exact_key()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            IWidgetTypeRegistry registry = provider.GetRequiredService<IWidgetTypeRegistry>();

            Assert.Equal(new WidgetTypeDescriptor("core.recents", "core", null, WidgetSubject.None), registry.Find("core.recents"));
            Assert.Equal(new WidgetTypeDescriptor("core.assignments-by-me", "core", null, WidgetSubject.None), registry.Find("core.assignments-by-me"));
            Assert.Null(registry.Find("Core.recents"));
            Assert.Null(registry.Find("core.report"));
            Assert.Null(registry.Find(null!));
        }

        [Fact]
        public void Only_the_report_type_has_a_report_subject_and_a_repeated_key_is_kept_once()
        {
            // Built over a record by hand: the gate refuses core.report from any feature but Core's.
            TestFeature<Tag.A> core = new("core");
            TestFeature<Tag.B> fixture = new("fixture");
            TellmaCompositionRecord record = new(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment());
            record.WidgetTypes.AddRange(
            [
                new("core.report", typeof(CashPositionSettings), core),
                new("fixture.report", null, fixture),
                new("core.report", null, fixture),
            ]);

            WidgetTypeRegistry registry = new(record);

            Assert.Equal(
                [
                    new WidgetTypeDescriptor("core.report", "core", typeof(CashPositionSettings), WidgetSubject.Report),
                    new WidgetTypeDescriptor("fixture.report", "fixture", null, WidgetSubject.None),
                ],
                registry.Types);
        }

        // --- The sugar and hand-built items ---

        [Fact]
        public void The_sugar_refuses_a_null_key()
        {
            FeatureContribution contribution = new();

            _ = Assert.Throws<ArgumentNullException>(() => contribution.WidgetType(null!));
            _ = Assert.Throws<ArgumentNullException>(() => contribution.WidgetType<CashPositionSettings>(null!));
            Assert.Empty(contribution.Items);
        }

        [Fact]
        public void An_item_without_its_key_is_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Add(new WidgetTypeContributionItem(null!, null)))));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal("Feature 'fixture' contributes a widget type without a key.", problem.Problem);
        }

        // --- core.widget-types ---

        [Fact]
        public async Task The_core_type_and_a_features_own_pass()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static contribution => contribution
                .WidgetType<CashPositionSettings>("fixture.cash-position")
                .WidgetType("fixture.top10-customers")
                .WidgetType("fixture.a"));

            Assert.Empty(problems.From(Check));
        }

        [Theory]
        [InlineData("fixture")]
        [InlineData("fixture.")]
        [InlineData(".cash-position")]
        [InlineData("fixture.Cash-position")]
        [InlineData("Fixture.cash-position")]
        [InlineData("fixture.cash_position")]
        [InlineData("fixture.cash--position")]
        [InlineData("fixture.cash-position-")]
        [InlineData("fixture.-cash")]
        [InlineData("fixture.9cash")]
        [InlineData("fixture.cash.position")]
        [InlineData("fixture-x.cash")]
        [InlineData("fixture.cash-position\n")]
        [InlineData("")]
        public async Task A_malformed_key_is_refused(string key)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(contribution => contribution.WidgetType(key))).From(Check));

            Assert.Equal($"The widget type '{key}' of the feature 'fixture' does not have a well-formed key.", problem.Problem);
        }

        [Fact]
        public async Task A_key_under_another_features_name_is_refused()
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(static contribution => contribution.WidgetType("acme.cash-position"))).From(Check));

            Assert.Equal("The widget type 'acme.cash-position' of the feature 'fixture' is not keyed under the feature's name.", problem.Problem);
            Assert.Equal("Start the key with the registering feature's name, such as 'fixture.cash-position', or register it in the feature 'acme'.", problem.Fix);
        }

        [Fact]
        public async Task A_key_registered_twice_names_both_features()
        {
            // The second registration also breaks the first-segment rule, which is reported apart.
            IReadOnlyList<CompositionProblem> problems = (await CheckAsync(static contribution => contribution.WidgetType("core.recents"))).From(Check);

            Assert.Equal(
                [
                    "The widget type 'core.recents' of the feature 'fixture' is not keyed under the feature's name.",
                    "The widget type 'core.recents' is registered more than once, by the features 'core' and 'fixture'.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task A_key_registered_twice_by_one_feature_is_refused()
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(static contribution => contribution
                .WidgetType("fixture.overdue")
                .WidgetType<CashPositionSettings>("fixture.overdue"))).From(Check));

            Assert.Equal("The widget type 'fixture.overdue' is registered more than once, by the features 'fixture' and 'fixture'.", problem.Problem);
        }

        [Theory]
        [InlineData(typeof(int))]
        [InlineData(typeof(IDisposable))]
        [InlineData(typeof(AbstractSettings))]
        [InlineData(typeof(List<>))]
        public async Task A_settings_type_that_is_not_a_concrete_closed_class_is_refused(Type settings)
        {
            CompositionProblem problem = Assert.Single((await CheckAsync(contribution => contribution
                .Add(new WidgetTypeContributionItem("fixture.overdue", settings)))).From(Check));

            Assert.StartsWith("The widget type 'fixture.overdue' of the feature 'fixture' binds its settings to ", problem.Problem, StringComparison.Ordinal);
            Assert.EndsWith(", which is not a concrete, closed class.", problem.Problem, StringComparison.Ordinal);
        }

        // --- The client metadata ---

        [Fact]
        public async Task The_contributor_writes_every_key_in_registry_order()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .WidgetType<CashPositionSettings>("fixture.cash-position"))))
                .BuildServiceProvider(validateScopes: true);

            // The widget types' contributor alone: the core chunk also carries the other sections.
            ClientAssetContext context = new(provider);
            provider.GetServices<IClientAssetContributor>().OfType<WidgetTypeClientAssetContributor>().Single().Contribute(context);

            ClientMetadataSection section = Assert.Single(context.Sections);
            Assert.Equal(
                (ClientMetadataChunk.Core, "widgetTypes", /*lang=json,strict*/ """["core.assignments-to-me","core.assignments-by-me","core.recents","fixture.cash-position"]"""),
                (section.Chunk, section.Name, Encoding.UTF8.GetString(section.Json.Span)));
        }

        /// <summary>Runs every check over a composition in which one feature, <c>fixture</c>, registers the types.</summary>
        /// <param name="contribute">Registers the types.</param>
        /// <returns>The problems.</returns>
        private static Task<IReadOnlyList<CompositionProblem>> CheckAsync(Action<FeatureContribution> contribute)
        {
            return TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribute)));
        }

        /// <summary>A widget type's settings.</summary>
        /// <param name="Account">The account the widget shows.</param>
        private sealed record CashPositionSettings(string Account);

        /// <summary>Settings no deserializer can create.</summary>
        private abstract class AbstractSettings;
    }
}
