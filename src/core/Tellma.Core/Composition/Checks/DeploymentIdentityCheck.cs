// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Composition.Checks
{
    /// <summary>
    ///     <c>core.deployment-identity</c>: the registered <see cref="DeploymentIdentity" /> names this
    ///     distribution's slug, and an <see cref="ISandboxContext" /> resolves, so every connector that
    ///     routes on the sandbox flag finds one.
    /// </summary>
    /// <param name="record">The realised composition.</param>
    internal sealed class DeploymentIdentityCheck(TellmaCompositionRecord record) : IStartupCheck
    {
        /// <inheritdoc />
        public string Name => CoreCheckNames.DeploymentIdentity;

        /// <inheritdoc />
        public bool RequiresInfrastructure => false;

        /// <inheritdoc />
        public Task<IReadOnlyList<CompositionProblem>> CheckAsync(IServiceProvider services, CancellationToken cancellationToken)
        {
            List<CompositionProblem> problems = [];

            // --- The deployment identity ---
            DeploymentIdentity? identity = services.GetService<DeploymentIdentity>();
            if (identity is null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    "No DeploymentIdentity is registered.",
                    "Leave the DeploymentIdentity registration to AddTellma, which registers it from the slug and the host environment."));
            }
            else if (!string.Equals(identity.Application, record.Slug, StringComparison.Ordinal))
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The registered DeploymentIdentity names the application '{identity.Application}', but the distribution slug is '{record.Slug}'.",
                    $"Remove the DeploymentIdentity the host registers itself; AddTellma registers DeploymentIdentity(\"{record.Slug}\", <environment>)."));
            }

            // --- The sandbox context ---
            ISandboxContext? sandbox;
            try
            {
                sandbox = services.GetService<ISandboxContext>();
            }
            catch (InvalidOperationException exception)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    $"The registered ISandboxContext cannot be created: {exception.Message}",
                    "Register every service the ISandboxContext implementation depends on."));
                return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
            }

            if (sandbox is null)
            {
                problems.Add(new CompositionProblem(
                    Name,
                    "No ISandboxContext is registered, so no connector can tell a sandbox tenant's work from a live tenant's.",
                    "Register the ISandboxContext of the host's tenancy; a host without tenants registers services.AddSingleton(SandboxContext.Never)."));
            }

            return Task.FromResult<IReadOnlyList<CompositionProblem>>(problems);
        }
    }
}
