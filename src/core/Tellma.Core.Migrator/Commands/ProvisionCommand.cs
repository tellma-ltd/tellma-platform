// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     <c>provision</c>: registers a tenant under the catalog lock, or resumes a provisioning one,
    ///     then brings it to active under its tenant lock (spec 0011 §6.1).
    /// </summary>
    /// <param name="context">The run.</param>
    internal sealed class ProvisionCommand(MigratorContext context)
    {
        /// <summary>Runs the command.</summary>
        /// <param name="line">The parsed command line.</param>
        /// <param name="cancellationToken">Cancels the run.</param>
        /// <returns>
        ///     <see cref="MigratorExitCodes.Converged" /> when the tenant is active,
        ///     <see cref="MigratorExitCodes.Usage" /> for an administrator subject outside Development,
        ///     else <see cref="MigratorExitCodes.PartialFailure" />.
        /// </returns>
        /// <exception cref="MigratorUsageException"><c>--admin-subject</c> was given outside Development.</exception>
        public async Task<int> RunAsync(ProvisionCommandLine line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);

            // A deployed administrator obtains a subject through the invitation, never from the operator.
            if (line.AdminSubject is not null && !context.Environment.IsDevelopment())
            {
                throw new MigratorUsageException("--admin-subject is accepted in Development only; a deployed administrator is invited by --admin-email.");
            }

            ITenantCatalog catalog = context.Services.GetRequiredService<ITenantCatalog>();
            int tenantId;

            // --- The registration, under the catalog lock; or the row to resume ---
            if (line.Name is not null)
            {
                try
                {
                    tenantId = await RegisterAsync(catalog, line, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TenantStateException or ArgumentException or MigratorFailureException)
                {
                    context.WriteLine($"provision: failed ({TenantOutcome.Describe(exception)})");
                    return MigratorExitCodes.PartialFailure;
                }
            }
            else
            {
                tenantId = line.Tenant!.Value;
            }

            // The registry's first load waits for the commands; the steps' tenant scopes read it.
            await context.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
            TenantInfo? tenant = (await catalog.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(row => row.Id == tenantId);
            TenantOutcome outcome = tenant switch
            {
                null => TenantOutcome.Failure(tenantId, "the catalog holds no such tenant"),
                { Descriptor.State: TenantState.Provisioning } => await new TenantMigration(context)
                    .ProvisionAsync(tenant, context.Administrator(line.AdminEmail, line.AdminSubject), cancellationToken)
                    .ConfigureAwait(false),

                // A rerun after the run that turned it active has nothing left to do.
                { Descriptor.State: TenantState.Active or TenantState.ReadOnly } => new TenantOutcome(tenantId, TenantOutcome.Provisioned, "already", []),
                _ => TenantOutcome.Failure(tenantId, string.Create(CultureInfo.InvariantCulture, $"the tenant is {tenant.Descriptor.State}; only a provisioning tenant is provisioned")),
            };

            // --- The report ---
            foreach (string reportLine in outcome.Lines())
            {
                context.WriteLine(reportLine);
            }

            return outcome.IsFailure ? MigratorExitCodes.PartialFailure : MigratorExitCodes.Converged;
        }

        /// <summary>Registers the tenant the command line describes, under the catalog lock.</summary>
        /// <param name="catalog">The catalog.</param>
        /// <param name="line">The command line, with a name and a category.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>The registered tenant's id.</returns>
        private async Task<int> RegisterAsync(ITenantCatalog catalog, ProvisionCommandLine line, CancellationToken cancellationToken)
        {
            CatalogConnection catalogConnection = context.Services.GetRequiredService<CatalogConnection>();

            // A database named alone lives on the catalog's server, under the default profile.
            TenantLocation? location = line.Database is null
                ? null
                : new TenantLocation(line.Server ?? catalogConnection.Server, line.Database, TellmaSqlOptions.DefaultProfile);

            await using MigrationLock held = await MigrationLock
                .AcquireAsync(catalogConnection.ConnectionString, MigrationLock.CatalogResource, cancellationToken)
                .ConfigureAwait(false);

            TenantDescriptor registered = await catalog
                .RegisterAsync(new TenantRegistration(line.Name!, line.Category!.Value, line.LiveTenant, location), MigratorContext.Actor, line.Tenant, cancellationToken)
                .ConfigureAwait(false);

            return registered.Id;
        }
    }
}
