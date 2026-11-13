// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Composition.Checks;
using Tellma.Core.Localization;
using Tellma.Core.Tests.Access;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.securables</c>: the registry composed in the realised gate, its problems reported,
    ///     and the composed securables judged against the features, the owners and the model.
    /// </summary>
    public class SecurablesCheckTests
    {
        private const string Check = "core.securables";

        [Fact]
        public async Task The_core_securables_pass_and_compose()
        {
            Assert.Empty((await TestComposition.CheckAsync(static _ => { })).From(Check));

            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            ISecurableRegistry registry = provider.GetRequiredService<ISecurableRegistry>();
            Assert.Equal(
                [
                    new SecurableDescriptor("core.User", "Read", "core.User", false, "core", null),
                    new SecurableDescriptor("core.UserStamp", "Read", "core.UserStamp", false, "core", new SecurableOwner("core.User", "User")),
                ],
                registry.All);
            Assert.Same(registry, provider.GetRequiredService<ISecurableRegistry>());
        }

        [Fact]
        public async Task A_child_filter_root_without_an_owner_fails_the_gate_and_with_its_owner_passes()
        {
            string membership = await NameOf<RoleMembership>();

            IReadOnlyList<CompositionProblem> problems = await CheckAsync(builder => builder
                .Add(membership, "Read", membership)
                .Add(membership, "Save", membership, owner: new SecurableOwner(CoreResources.User, "User")));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Equal($"The securable '{membership}:Read' of the feature 'fixture' takes the filter root '{membership}', a child entity, without an owner.", problem.Problem);
        }

        [Fact]
        public async Task An_owner_navigation_that_is_no_path_or_leads_elsewhere_is_refused()
        {
            string membership = await NameOf<RoleMembership>();

            IReadOnlyList<CompositionProblem> problems = await CheckAsync(builder => builder
                .Add(membership, "Read", membership, owner: new SecurableOwner(CoreResources.User, "Role"))
                .Add(membership, "Save", membership, owner: new SecurableOwner(CoreResources.User, "User.Nothing")));

            Assert.Equal(
                [
                    $"The securable '{membership}:Read' of the feature 'fixture' reaches 'core.Role' through 'Role', not 'core.User', the filter root of its owner 'core.User'.",
                    $"The securable '{membership}:Save' of the feature 'fixture' declares the owner navigation 'User.Nothing', which is not a path of navigations from '{membership}'.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task A_filter_root_outside_the_query_schema_an_unregistered_owner_and_an_owner_without_a_root_are_refused()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static builder => builder
                .Add("fixture.Report", "Read", "fixture.Report")
                .Add("fixture.Line", "Read", null, owner: new SecurableOwner("fixture.Header", "Header")));

            Assert.Equal(
                [
                    "The securable 'fixture.Line:Read' of the feature 'fixture' declares the owner 'fixture.Header', which is not a registered resource.",
                    "The securable 'fixture.Line:Read' of the feature 'fixture' declares the owner 'fixture.Header' but takes no filter root, so no navigation leads to the owner.",
                    "The securable 'fixture.Report:Read' of the feature 'fixture' takes the filter root 'fixture.Report', which is not an entity of the query schema.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task Registry_problems_and_an_unknown_feature_fail_the_gate()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static builder => builder
                .Add("fixture.Report", "Read", null, feature: "nowhere")
                .Add("fixture.Report", "Save", null, feature: "nowhere")
                .Add("fixture.Export", "Read", null, feature: "elsewhere")
                .MarkSensitive("fixture.Missing", "Read"));

            Assert.Equal(
                [
                    "The feature 'fixture' marks 'fixture.Missing:Read' sensitive, which is not a registered securable.",
                    "The securable 'fixture.Export:Read' belongs to the feature 'elsewhere', which is not composed.",
                    "The securable 'fixture.Report:Read' and 1 more belong to the feature 'nowhere', which is not composed.",
                ],
                problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public void An_owner_cycle_is_refused_once_per_cycle()
        {
            // The chains: alpha → beta → alpha, gamma → gamma, and delta → alpha, which ends in a
            // cycle without being one; epsilon's owner pair is not registered, which ends its chain.
            SecurableRegistry registry = SecurableRegistryTests.Compose(("fixture", static builder => builder
                .Add("fixture.Alpha", "Read", "core.User", owner: new SecurableOwner("fixture.Beta", "User"))
                .Add("fixture.Beta", "Read", "core.User", owner: new SecurableOwner("fixture.Alpha", "User"))
                .Add("fixture.Gamma", "Read", "core.User", owner: new SecurableOwner("fixture.Gamma", "User"))
                .Add("fixture.Delta", "Read", "core.User", owner: new SecurableOwner("fixture.Alpha", "User"))
                .Add("fixture.Epsilon", "Save", "core.User", owner: new SecurableOwner("fixture.Alpha", "User"))));

            List<CompositionProblem> problems = [];
            new SecurablesCheck(Record()).CheckOwnerChains(registry, problems);

            Assert.Equal(
                [
                    "The owners of the securables form a cycle: 'fixture.Alpha:Read' → 'fixture.Beta:Read' → 'fixture.Alpha:Read'.",
                    "The owners of the securables form a cycle: 'fixture.Gamma:Read' → 'fixture.Gamma:Read'.",
                ],
                problems.Select(static problem => problem.Problem));
            Assert.All(problems, static problem => Assert.Equal(Check, problem.Source));
        }

        [Fact]
        public async Task An_owner_cycle_fails_the_gate()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAsync(static builder => builder
                .Add(CoreResources.User, "Save", CoreResources.User, owner: new SecurableOwner(CoreResources.User, "CreatedBy")));

            Assert.Equal(["The owners of the securables form a cycle: 'core.User:Save' → 'core.User:Save'."], problems.Select(static problem => problem.Problem));
        }

        [Fact]
        public async Task A_contributor_registered_by_type_is_attributed_to_its_feature()
        {
            await using ServiceProvider provider = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution
                    .Singleton<ISecurableContributor, FixtureSecurables>())))
                .BuildServiceProvider(validateScopes: true);

            ISecurableRegistry registry = provider.GetRequiredService<ISecurableRegistry>();
            Assert.Equal("fixture", registry.Find("fixture.Report", "Read")!.Feature);
            Assert.Equal("core", registry.Find("fixture.Report", "Save")!.Feature);
        }

        [Fact]
        public void Securables_without_a_configuration_are_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("fixture", contribute: static contribution => contribution.Add(new SecurablesContributionItem(null!)))));

            Assert.Equal(["Feature 'fixture' contributes securables without a configuration."], problems.Select(static problem => problem.Problem));
        }

        /// <summary>The query name of an entity of the composed model.</summary>
        /// <typeparam name="TEntity">The entity.</typeparam>
        /// <returns>Its name.</returns>
        private static async Task<string> NameOf<TEntity>()
        {
            await using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider(validateScopes: true);
            return provider.GetRequiredService<IEntityMetadataProvider>().Get(typeof(TEntity)).Name;
        }

        /// <summary>Runs every check over a composition in which one feature, <c>fixture</c>, registers securables.</summary>
        /// <param name="configure">The registrations.</param>
        /// <returns>The problems of this check.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> CheckAsync(Action<SecurableRegistryBuilder> configure)
        {
            return (await TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution
                .Securables(configure))))).From(Check);
        }

        /// <summary>A realised composition of the core feature alone.</summary>
        /// <returns>The record.</returns>
        private static TellmaCompositionRecord Record()
        {
            TellmaCompositionRecord record = new(TestComposition.Slug, TestComposition.Configuration(), new TestEnvironment());
            record.Settle([new CoreFeature()], new Dictionary<Type, Type>(), LanguageDeclaration.None);
            return record;
        }

        /// <summary>A contributor a feature registers by type: one securable of its own, and one it names <c>core</c>'s.</summary>
        private sealed class FixtureSecurables : ISecurableContributor
        {
            /// <inheritdoc />
            public void Contribute(SecurableRegistryBuilder builder)
            {
                _ = builder
                    .Add("fixture.Report", "Read", null)
                    .Add("fixture.Report", "Save", null, feature: "core");
            }
        }
    }
}
