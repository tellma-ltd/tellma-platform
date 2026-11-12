// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Tests.Data.Model;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition
{
    /// <summary>Entity contributions: recorded for the tenant model in feature order, leaves substituted, malformed items reported.</summary>
    public class StackContributionTests
    {
        [Fact]
        public void Contributed_entities_reach_the_model_definition_with_their_leaves()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("first", contribute: static c => c.Entity<TestUser>().Entity<Center, object>()))
                .AddFeature(new TestFeature<Tag.B>("second", contribute: static c => c.Entity<TestUser>().Entity<TestJob>()))
                .UseEntity<Center, MyCenter>());

            using ServiceProvider provider = services.BuildServiceProvider();
            TellmaModelDefinition definition = provider.GetRequiredService<TellmaModelDefinition>();
            TellmaCompositionRecord record = provider.GetRequiredService<TellmaCompositionRecord>();

            // Core's own entities come first, each followed by the children its collections carry.
            Assert.Equal(
                [typeof(User), typeof(RoleMembership), typeof(Role), typeof(Permission), typeof(TestUser), typeof(MyCenter), typeof(TestJob)],
                definition.EntityTypes);
            Assert.Equal(typeof(object), record.Entities.Single(static entity => entity.EntityType == typeof(Center)).ServiceType);
            Assert.Equal(["core", "core", "first", "first", "second", "second"], record.Entities.Select(static entity => entity.Owner.Name));
        }

        [Fact]
        public void The_same_entity_from_the_same_feature_is_recorded_once()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("first", contribute: static c => c.Entity<TestUser>().Entity<TestUser>())));

            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.Single(provider.GetRequiredService<TellmaCompositionRecord>().Entities, static entity => entity.Owner.Name == "first");
        }

        [Theory]
        [InlineData(null)]
        [InlineData(typeof(IComparable))]
        [InlineData(typeof(List<>))]
        public void An_entity_that_is_not_a_closed_class_is_reported(Type? entityType)
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("first", contribute: contribution => contribution.Add(new StackContributionItem(entityType!, null, null)))));

            Assert.Contains("Feature 'first' contributes the entity", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_sugar_records_the_entity_and_the_service()
        {
            FeatureContribution contribution = new FeatureContribution().Entity<TestUser>().Entity<TestJob, object>();

            Assert.Equal(
                [new StackContributionItem(typeof(TestUser), null, null), new StackContributionItem(typeof(TestJob), typeof(object), null)],
                contribution.Items);
        }
    }
}
