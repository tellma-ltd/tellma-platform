// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tests.Infrastructure;
using Tellma.Testing.Support.Tenancy;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.deployment-identity</c>: the identity names the slug and a sandbox context resolves.</summary>
    public class DeploymentIdentityCheckTests
    {
        private const string Check = "core.deployment-identity";

        [Fact]
        public async Task The_identity_AddTellma_registers_and_a_registered_sandbox_context_pass()
        {
            ServiceCollection services = new();
            services.AddScoped<ISandboxContext>(static _ => new StubSandboxContext());

            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: services);

            Assert.Empty(problems.From(Check));
        }

        [Fact]
        public async Task A_missing_sandbox_context_is_reported()
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: new ServiceCollection());

            CompositionProblem problem = Assert.Single(problems.From(Check));
            Assert.Contains("No ISandboxContext is registered", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("SandboxContext.Never", problem.Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_sandbox_context_that_cannot_be_created_is_reported()
        {
            ServiceCollection services = new();
            services.AddScoped<ISandboxContext, UnresolvableSandboxContext>();

            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: services);

            Assert.Contains("cannot be created", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_identity_the_host_registered_for_another_application_is_reported()
        {
            ServiceCollection services = new();
            services.AddSingleton(SandboxContext.Never);
            services.AddSingleton(new DeploymentIdentity("identity", "Production"));

            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(static _ => { }, services: services);

            Assert.Contains("names the application 'identity'", Assert.Single(problems.From(Check)).Problem, StringComparison.Ordinal);
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
