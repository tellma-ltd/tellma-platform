// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary><c>set-state</c>: moves a tenant to another state, as the migrator (spec 0011 §6.1).</summary>
    /// <param name="context">The run.</param>
    internal sealed class SetStateCommand(MigratorContext context)
    {
        /// <summary>Runs the command.</summary>
        /// <param name="line">The parsed command line.</param>
        /// <param name="cancellationToken">Cancels the write.</param>
        /// <returns>
        ///     <see cref="MigratorExitCodes.Converged" /> when the tenant is in the state,
        ///     <see cref="MigratorExitCodes.PartialFailure" /> when the catalog refused the transition.
        /// </returns>
        /// <exception cref="MigratorUsageException">The reason is malformed.</exception>
        public async Task<int> RunAsync(SetStateCommandLine line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            TenantOutcome outcome;
            try
            {
                TenantDescriptor tenant = await context.Services.GetRequiredService<ITenantCatalog>()
                    .SetStateAsync(line.Tenant, line.State, line.Reason, MigratorContext.Actor, cancellationToken)
                    .ConfigureAwait(false);

                outcome = new TenantOutcome(tenant.Id, tenant.State.ToString(), Detail: null, []);
            }
            catch (ArgumentException exception)
            {
                // The only argument the operator controls that the catalog checks is the reason.
                throw new MigratorUsageException(string.Create(CultureInfo.InvariantCulture, $"--reason is refused: {exception.Message}"));
            }
            catch (Exception exception) when (exception is TenantNotFoundException or TenantStateException)
            {
                outcome = TenantOutcome.Failure(line.Tenant, TenantOutcome.Describe(exception));
            }

            foreach (string reportLine in outcome.Lines())
            {
                context.WriteLine(reportLine);
            }

            return outcome.IsFailure ? MigratorExitCodes.PartialFailure : MigratorExitCodes.Converged;
        }
    }
}
