// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Support.Tenancy;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary>
    ///     <c>core.deployment-identity</c>: the identity names the slug, and the sandbox context the
    ///     composition registers, or one a host registered after it, resolves.
    /// </summary>
    public class DeploymentIdentityCheckTests
    {
        private const string Check = "core.deployment-identity";

        [Fact]
        public async Task The_identity_and_the_sandbox_context_AddTellma_registers_pass()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: new ServiceCollection());

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_sandbox_context_a_host_registered_after_composing_passes_too()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAfterAsync(
                static services => services.AddScoped<ISandboxContext>(static _ => new StubSandboxContext()));

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_removed_sandbox_context_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAfterAsync(static services => services.RemoveAll<ISandboxContext>());

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Contains("No ISandboxContext is registered", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Leave the ISandboxContext registration to AddTellma", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_sandbox_context_that_cannot_be_created_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await CheckAfterAsync(
                static services => services.AddScoped<ISandboxContext, UnresolvableSandboxContext>());

            Assert.Contains("cannot be created", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_identity_the_host_registered_for_another_application_is_reported()
        {
            ServiceCollection services = [];
            services.AddSingleton(new DeploymentIdentity("identity", "Production"));

            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: services);

            Assert.Contains("names the application 'identity'", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        /// <summary>Composes, lets the host change the services afterwards, and runs every startup check.</summary>
        /// <param name="afterComposing">What the host does after <c>AddTellma</c>.</param>
        /// <returns>The problems the checks reported.</returns>
        private static async Task<IReadOnlyList<CompositionProblem>> CheckAfterAsync(Action<IServiceCollection> afterComposing)
        {
            IServiceCollection services = TestComposition.Compose(static _ => { });
            afterComposing(services);
            await using ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            return await TellmaStartupGate.RunChecksAsync(provider, includeInfrastructure: true, TestContext.Current.CancellationToken);
        }

        /// <summary>A sandbox context whose dependency nobody registers.</summary>
        /// <param name="dependency">The unregistered dependency.</param>
        private sealed class UnresolvableSandboxContext(IFormatProvider dependency) : ISandboxContext
        {
            /// <inheritdoc />
            public bool IsSandbox => dependency is null;
        }
    }
}
