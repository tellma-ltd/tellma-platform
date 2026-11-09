// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Data.Model;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.model-contributors</c>: every contributed model configuration is one the model can apply.</summary>
    public class ModelContributorsCheckTests
    {
        private const string Check = "core.model-contributors";

        [Fact]
        public async Task Cores_own_configurations_and_a_sound_open_one_pass()
        {
            Assert.Empty(await CheckAsync(typeof(TestUserConfiguration<>)));
        }

        [Theory]
        [InlineData(typeof(NotAConfiguration), "does not implement IEntityTypeConfiguration<TEntity> exactly once")]
        [InlineData(typeof(AbstractConfiguration), "is not a concrete class")]
        [InlineData(typeof(UnconstrainedConfiguration<>), "constrains its type parameter to no entity class")]
        [InlineData(typeof(TwoParameterConfiguration<,>), "is open over more than one type parameter")]
        [InlineData(typeof(OtherParameterConfiguration<>), "is open over a type parameter other than the entity it configures")]
        [InlineData(typeof(NoParameterlessConstructor), "cannot be created over the registered leaf")]
        public async Task A_configuration_the_model_cannot_apply_is_reported_with_its_owner_and_reason(Type configuration, string reason)
        {
            CompositionProblem problem = Assert.Single(await CheckAsync(configuration));

            Assert.Contains("The feature 'fixture' contributes the model configuration", problem.Problem, StringComparison.Ordinal);
            Assert.Contains(reason, problem.Problem, StringComparison.Ordinal);
            Assert.Contains("IEntityTypeConfiguration<TEntity>", problem.Fix, StringComparison.Ordinal);
        }

        private static async Task<List<CompositionProblem>> CheckAsync(Type configuration)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma =>
                tellma.AddFeature(new TestFeature<Tag.A>("fixture", contribute: contribution => contribution.Model(configuration))));

            return problems.From(Check);
        }

        /// <summary>Not a configuration at all.</summary>
        public sealed class NotAConfiguration;

        /// <summary>An abstract configuration.</summary>
        public abstract class AbstractConfiguration : IEntityTypeConfiguration<TestJob>
        {
            public void Configure(EntityTypeBuilder<TestJob> builder)
            {
            }
        }

        /// <summary>Open, with no class constraint to close it by.</summary>
        /// <typeparam name="T">The entity.</typeparam>
        public sealed class UnconstrainedConfiguration<T> : IEntityTypeConfiguration<T>
            where T : class
        {
            public void Configure(EntityTypeBuilder<T> builder)
            {
            }
        }

        /// <summary>Open over two parameters.</summary>
        /// <typeparam name="T">The entity.</typeparam>
        /// <typeparam name="TOther">Another parameter.</typeparam>
        public sealed class TwoParameterConfiguration<T, TOther> : IEntityTypeConfiguration<T>
            where T : TestUser
        {
            public void Configure(EntityTypeBuilder<T> builder)
            {
            }
        }

        /// <summary>Open over a parameter it does not configure.</summary>
        /// <typeparam name="T">A parameter other than the entity.</typeparam>
        public sealed class OtherParameterConfiguration<T> : IEntityTypeConfiguration<TestJob>
            where T : TestUser
        {
            public void Configure(EntityTypeBuilder<TestJob> builder)
            {
            }
        }

        /// <summary>A closed configuration the model cannot create.</summary>
        /// <param name="unused">A constructor argument nothing supplies.</param>
        public sealed class NoParameterlessConstructor(int unused) : IEntityTypeConfiguration<TestJob>
        {
            public void Configure(EntityTypeBuilder<TestJob> builder)
            {
                _ = unused;
            }
        }
    }
}
