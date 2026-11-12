// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.provisioning-steps</c> and the contribution it checks: each step registered once,
    ///     scoped; a name, order and version in range; one step per name; and a step that cannot be
    ///     created reported rather than thrown.
    /// </summary>
    public class ProvisioningStepsCheckTests
    {
        private const string Check = "core.provisioning-steps";

        [Fact]
        public async Task Sound_steps_pass_and_are_registered_once_each_as_scoped_steps()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c
                .ProvisioningStep<SampleStep>()
                .ProvisioningStep<OtherStep>())));

            // --- Each class scoped by itself, and recorded once with its owner, in contribution order ---
            Assert.Equal([typeof(SampleStep), typeof(OtherStep)], Record(services).ProvisioningSteps.Select(static step => step.StepType));
            Assert.All(Record(services).ProvisioningSteps, static step => Assert.Equal("acme", step.Owner.Name));
            foreach (Type stepType in new[] { typeof(SampleStep), typeof(OtherStep) })
            {
                ServiceDescriptor descriptor = Assert.Single(services, descriptor => descriptor.ServiceType == stepType);
                Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
                Assert.Equal(stepType, descriptor.ImplementationType);
            }

            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Assert.Empty((await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: false, TestContext.Current.CancellationToken)).From(Check));
        }

        [Fact]
        public async Task A_step_generic_over_a_substituted_entity_is_closed_over_the_registered_leaf()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c.ProvisioningStep<EntityStep<SampleEntity>>()))
                .UseEntity<SampleEntity, TrackedLeaf>());

            Assert.Equal(typeof(EntityStep<TrackedLeaf>), Assert.Single(Record(services).ProvisioningSteps).StepType);
            Assert.Contains(services, static descriptor => descriptor.ServiceType == typeof(EntityStep<TrackedLeaf>) && descriptor.Lifetime == ServiceLifetime.Scoped);
            Assert.DoesNotContain(services, static descriptor => descriptor.ServiceType == typeof(EntityStep<SampleEntity>));

            // The runner and the gate create the step their registration names, over the leaf.
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            Assert.IsType<EntityStep<TrackedLeaf>>(Record(services).ProvisioningSteps[0].Create(scope.ServiceProvider));
        }

        [Fact]
        public async Task One_class_may_run_as_several_steps_each_keyed_by_its_own_name()
        {
            IServiceCollection services = TestComposition.Compose(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme")));
            TellmaCompositionRecord record = Record(services);
            ITellmaFeature owner = record.Features.Single(static feature => feature.Name == "acme");

            // --- Two instances of one class under two names, as a realiser adds one per seed: sound ---
            record.ProvisioningSteps.Add(new ProvisioningStepRegistration(typeof(NamedStep), owner, static _ => new NamedStep("acme.seed-a")));
            record.ProvisioningSteps.Add(new ProvisioningStepRegistration(typeof(NamedStep), owner, static _ => new NamedStep("acme.seed-b")));
            await using (ServiceProvider provider = services.BuildServiceProvider(validateScopes: true))
            {
                Assert.Empty((await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: false, TestContext.Current.CancellationToken)).From(Check));
            }

            // --- A third under a name already taken: reported ---
            record.ProvisioningSteps.Add(new ProvisioningStepRegistration(typeof(NamedStep), owner, static _ => new NamedStep("ACME.SEED-A")));
            await using (ServiceProvider provider = services.BuildServiceProvider(validateScopes: true))
            {
                IReadOnlyList<CompositionProblem> problems = (await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: false, TestContext.Current.CancellationToken)).From(Check);
                Assert.Contains("share the name 'acme.seed-a'", Assert.Single(problems).Problem, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_same_step_class_contributed_twice_by_one_feature_is_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c
                .ProvisioningStep<SampleStep>()
                .ProvisioningStep<SampleStep>())));

            Assert.Contains("already, once", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_step_class_contributed_by_two_features_is_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma
                .AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c.ProvisioningStep<SampleStep>()))
                .AddFeature(new TestFeature<Tag.B>("beta", contribute: static c => c.ProvisioningStep<SampleStep>())));

            CompositionProblem problem = Assert.Single(problems);
            Assert.Contains("Feature 'beta'", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("after feature 'acme' did", problem.Problem, StringComparison.Ordinal);
        }

        [Fact]
        public void A_contributed_type_that_is_not_a_concrete_step_is_a_composition_problem()
        {
            IReadOnlyList<CompositionProblem> problems = TestComposition.Problems(static tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: static c => c
                .Add(new ProvisioningStepContributionItem(typeof(AbstractStep)))
                .Add(new ProvisioningStepContributionItem(typeof(string))))));

            Assert.Equal(2, problems.Count);
            Assert.All(problems, static problem => Assert.Contains("not a concrete, closed class implementing ITenantProvisioningStep", problem.Problem, StringComparison.Ordinal));
        }

        [Fact]
        public async Task Two_steps_with_one_name_in_any_case_are_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await StepProblemsAsync(static c => c.ProvisioningStep<SampleStep>().ProvisioningStep<ShoutingStep>());

            Assert.Contains("share the name", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_name_order_or_version_out_of_range_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await StepProblemsAsync(static c => c.ProvisioningStep<MalformedStep>());

            Assert.Equal(3, problems.Count);
            Assert.Contains(problems, static problem => problem.Problem.Contains("has the name ' acme.padded'", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("has the order -1", StringComparison.Ordinal));
            Assert.Contains(problems, static problem => problem.Problem.Contains("has the version 0", StringComparison.Ordinal));
        }

        [Fact]
        public async Task A_step_that_cannot_be_created_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await StepProblemsAsync(static c => c.ProvisioningStep<UnconstructibleStep>());

            Assert.Contains("could not be created", Assert.Single(problems).Problem, StringComparison.Ordinal);
        }

        /// <summary>The realised composition's record.</summary>
        /// <param name="services">The composed services.</param>
        /// <returns>The record.</returns>
        private static TellmaCompositionRecord Record(IServiceCollection services)
        {
            return (TellmaCompositionRecord)services.Single(static descriptor => descriptor.ServiceType == typeof(TellmaCompositionRecord)).ImplementationInstance!;
        }

        /// <summary>Runs the check over a composition of the given steps.</summary>
        /// <param name="contribute">Contributes the steps.</param>
        /// <returns>The check's problems.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> StepProblemsAsync(Action<FeatureContribution> contribute)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(tellma => tellma.AddFeature(new TestFeature<Tag.A>("acme", contribute: contribute)));
            return problems.From(Check);
        }

        /// <summary>A sound step.</summary>
        private sealed class SampleStep : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public string Name => "acme.sample";

            /// <inheritdoc />
            public int Order => 200;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A step written against a pack's entity, which a distribution may substitute.</summary>
        /// <typeparam name="TEntity">The entity, or the leaf that replaces it.</typeparam>
        private sealed class EntityStep<TEntity> : ITenantProvisioningStep
            where TEntity : SampleEntity
        {
            /// <inheritdoc />
            public string Name => "acme.entity";

            /// <inheritdoc />
            public int Order => 200;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>One class that runs under the name each instance is given.</summary>
        /// <param name="name">The instance's name.</param>
        private sealed class NamedStep(string name) : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public string Name => name;

            /// <inheritdoc />
            public int Order => 900;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>Another sound step.</summary>
        private sealed class OtherStep : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public string Name => "acme.other";

            /// <inheritdoc />
            public int Order => 210;

            /// <inheritdoc />
            public int Version => 3;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A step whose name differs from <see cref="SampleStep" />'s in case alone.</summary>
        private sealed class ShoutingStep : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public string Name => "ACME.SAMPLE";

            /// <inheritdoc />
            public int Order => 220;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A step with a padded name, a negative order and a zero version.</summary>
        private sealed class MalformedStep : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public string Name => " acme.padded";

            /// <inheritdoc />
            public int Order => -1;

            /// <inheritdoc />
            public int Version => 0;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>A step whose constructor throws.</summary>
        private sealed class UnconstructibleStep : ITenantProvisioningStep
        {
            /// <summary>Throws.</summary>
            public UnconstructibleStep()
            {
                throw new InvalidOperationException("The step reached for a tenant in its constructor.");
            }

            /// <inheritdoc />
            public string Name => "acme.unconstructible";

            /// <inheritdoc />
            public int Order => 200;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>An abstract step, which cannot be created.</summary>
        private abstract class AbstractStep : ITenantProvisioningStep
        {
            /// <inheritdoc />
            public abstract string Name { get; }

            /// <inheritdoc />
            public int Order => 200;

            /// <inheritdoc />
            public int Version => 1;

            /// <inheritdoc />
            public Task RunAsync(TenantProvisioningContext context)
            {
                return Task.CompletedTask;
            }
        }
    }
}
