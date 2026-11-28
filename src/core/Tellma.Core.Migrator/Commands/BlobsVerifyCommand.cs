// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Blobs;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Provisioning;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     <c>blobs verify</c>: compares a tenant's stored objects with its blob rows, as the migrator,
    ///     and reports the objects no row references and the rows whose objects are gone. It deletes
    ///     the unreferenced objects only when asked, and never writes a row.
    /// </summary>
    /// <param name="context">The run.</param>
    internal sealed class BlobsVerifyCommand(MigratorContext context)
    {
        /// <summary>Runs the command.</summary>
        /// <param name="line">The parsed command line.</param>
        /// <param name="cancellationToken">Cancels the walk.</param>
        /// <returns>
        ///     <see cref="MigratorExitCodes.Converged" /> when the walk reached the end, findings or
        ///     not; <see cref="MigratorExitCodes.PartialFailure" /> when the tenant could not be served
        ///     or the store failed.
        /// </returns>
        public async Task<int> RunAsync(BlobsVerifyCommandLine line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            string prefix = string.Create(CultureInfo.InvariantCulture, $"tenant {line.Tenant}");
            TenantOutcome outcome;
            try
            {
                // --- The tenant, as the catalog holds it now, in a system scope even while frozen ---
                await context.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
                TenantScope scope = await context.Services.GetRequiredService<ITenantScopeFactory>()
                    .CreateScopeAsync(ProvisioningStepRunner.SystemSnapshot(line.Tenant), allowNonActive: true, cancellationToken)
                    .ConfigureAwait(false);

                await using (scope.ConfigureAwait(false))
                {
                    // --- The walk, each finding printed as it is found ---
                    IServiceProvider services = scope.Services;
                    BlobVerifier verifier = new(
                        services.GetRequiredService<IBlobStore>(),
                        services.GetRequiredService<IBlobKindRegistry>(),
                        services.GetRequiredService<ITenantDatabase>(),
                        services.GetService<TimeProvider>() ?? TimeProvider.System);
                    BlobVerification totals = await verifier
                        .VerifyAsync(line.Tenant, line.DeleteOrphans, finding => context.WriteLine($"{prefix}: {Describe(finding)}"), cancellationToken)
                        .ConfigureAwait(false);

                    outcome = new TenantOutcome(line.Tenant, TenantOutcome.Verified, Summary(totals, line.DeleteOrphans), []);
                }
            }
            catch (Exception exception) when (exception is TenantNotFoundException or TenantUnavailableException or BlobStoreException)
            {
                outcome = TenantOutcome.Failure(line.Tenant, TenantOutcome.Describe(exception));
            }

            foreach (string reportLine in outcome.Lines())
            {
                context.WriteLine(reportLine);
            }

            return outcome.IsFailure ? MigratorExitCodes.PartialFailure : MigratorExitCodes.Converged;
        }

        /// <summary>The text of one finding.</summary>
        /// <param name="finding">The finding.</param>
        /// <returns><c>orphan {name}</c>, or <c>missing {name} (blob {id}, {state})</c>.</returns>
        internal static string Describe(BlobFinding finding)
        {
            ArgumentNullException.ThrowIfNull(finding);
            return finding.Kind == BlobFindingKind.Orphan
                ? $"orphan {finding.Name}"
                : string.Create(CultureInfo.InvariantCulture, $"missing {finding.Name} (blob {finding.BlobId}, {finding.State})");
        }

        /// <summary>The text of the walk's totals.</summary>
        /// <param name="totals">The totals.</param>
        /// <param name="deleteOrphans">Whether the unreferenced objects were deleted.</param>
        /// <returns>The text.</returns>
        internal static string Summary(BlobVerification totals, bool deleteOrphans)
        {
            string orphans = deleteOrphans
                ? string.Create(CultureInfo.InvariantCulture, $"{totals.Orphans} orphans, {totals.Deleted} deleted")
                : string.Create(CultureInfo.InvariantCulture, $"{totals.Orphans} orphans");
            return string.Create(CultureInfo.InvariantCulture, $"{totals.Objects} objects, {totals.Rows} rows, {orphans}, {totals.Missing} missing");
        }
    }
}
