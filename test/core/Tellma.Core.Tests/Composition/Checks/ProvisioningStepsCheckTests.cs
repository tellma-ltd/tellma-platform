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

            List<ServiceDescriptor> steps = [.. services.Where(static descriptor => descriptor.ServiceType == typeof(ITenantProvisioningStep))];
            Assert.Equal([typeof(SampleStep), typeof(OtherStep)], steps.Select(static descriptor => descriptor.ImplementationType));
            Assert.All(steps, static descriptor => Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime));

            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            Assert.Empty((await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: false, TestContext.Current.CancellationToken)).From(Check));
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
