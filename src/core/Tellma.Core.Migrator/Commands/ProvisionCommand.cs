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
    ///     then brings it to active under its tenant lock.
    /// </summary>
    /// <remarks>
    ///     A rerun of a line that requests an id converges: when the catalog already holds that id
    ///     with the same name, category, live tenant and location, the row is resumed instead of
    ///     registered again, and a tenant an earlier run turned active is reported provisioned
    ///     already. A line without <c>--tenant</c> draws a new id from the sequence each time, so the
    ///     command prints the id it registered at once and names <c>provision --tenant &lt;id&gt;</c>
    ///     as the resume when the run fails.
    /// </remarks>
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
                bool registered;
                try
                {
                    (tenantId, registered) = await RegisterAsync(catalog, line, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is TenantStateException or ArgumentException or MigratorFailureException)
                {
                    context.WriteLine($"provision: failed ({TenantOutcome.Describe(exception)})");
                    return MigratorExitCodes.PartialFailure;
                }

                // The id is printed before anything else can fail, so the operator always knows the resume.
                if (registered)
                {
                    context.WriteLine(string.Create(CultureInfo.InvariantCulture, $"provision: registered tenant {tenantId}"));
                }
            }
            else
            {
                tenantId = line.Tenant!.Value;
            }

            // The registry's first load waits for the commands; the steps' tenant scopes read it.
            await context.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);

            // The tenant's row is read under its lock, where a run that finished it first is told apart.
            TenantOutcome outcome = await new TenantMigration(context)
                .ProvisionAsync(tenantId, context.Administrator(line.AdminEmail, line.AdminSubject), cancellationToken)
                .ConfigureAwait(false);

            // --- The report; a failed run of a sequence-allocated registration names its resume ---
            foreach (string reportLine in outcome.Lines())
            {
                context.WriteLine(reportLine);
            }

            if (outcome.IsFailure && line.Name is not null && line.Tenant is null)
            {
                context.WriteLine(string.Create(CultureInfo.InvariantCulture, $"provision: resume it with provision --tenant {tenantId}; rerunning this line registers another tenant"));
            }

            return outcome.IsFailure ? MigratorExitCodes.PartialFailure : MigratorExitCodes.Converged;
        }

        /// <summary>
        ///     Registers the tenant the command line describes, under the catalog lock; or, when the
        ///     line requests an id the catalog already holds with the same registration, the id of
        ///     that row, which an earlier run of the same line registered.
        /// </summary>
        /// <param name="catalog">The catalog.</param>
        /// <param name="line">The command line, with a name and a category.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>The tenant's id, and whether this run registered it rather than finding it.</returns>
        /// <exception cref="MigratorFailureException">The requested id is held by a row registered otherwise.</exception>
        private async Task<(int TenantId, bool Registered)> RegisterAsync(ITenantCatalog catalog, ProvisionCommandLine line, CancellationToken cancellationToken)
        {
            CatalogConnection catalogConnection = context.Services.GetRequiredService<CatalogConnection>();

            // A database named alone lives on the catalog's server, under the default profile; the
            // server is spelled as the catalog stores it, so a rerun compares like with like.
            TenantLocation? location = line.Database is null
                ? null
                : new TenantLocation(TenantLocations.Canonicalize(line.Server ?? catalogConnection.Server), line.Database, TellmaSqlOptions.DefaultProfile);

            await using MigrationLock held = await MigrationLock
                .AcquireAsync(catalogConnection.ConnectionString, MigrationLock.CatalogResource, cancellationToken)
                .ConfigureAwait(false);

            // --- A rerun of the same line: the row it registered before ---
            if (line.Tenant is int requested
                && (await catalog.ListAsync(cancellationToken).ConfigureAwait(false)).FirstOrDefault(row => row.Id == requested) is { } existing)
            {
                return Matches(existing, line, location)
                    ? (requested, false)
                    : throw new MigratorFailureException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"Tenant {requested} is already registered as '{existing.Descriptor.Name}' ({existing.Descriptor.Category}, database {existing.Location.Database}), which this command line does not describe; run provision --tenant {requested} alone to resume it, or request another id."));
            }

            TenantDescriptor registered = await catalog
                .RegisterAsync(new TenantRegistration(line.Name!, line.Category!.Value, line.LiveTenant, location), MigratorContext.Actor, line.Tenant, cancellationToken)
                .ConfigureAwait(false);

            return (registered.Id, true);
        }

        /// <summary>
        ///     Whether an existing row is the registration a command line describes: the same name,
        ///     category and live tenant, and, when the line names a database, the same server (both
        ///     canonical: no <c>tcp:</c> prefix, no default port) and database. The credential profile
        ///     is configuration, not part of the registration.
        /// </summary>
        /// <param name="existing">The row.</param>
        /// <param name="line">The command line.</param>
        /// <param name="location">The location the line names, or null when it names none.</param>
        /// <returns>True when they match.</returns>
        private static bool Matches(TenantInfo existing, ProvisionCommandLine line, TenantLocation? location)
        {
            return string.Equals(existing.Descriptor.Name, line.Name, StringComparison.Ordinal)
                && existing.Descriptor.Category == line.Category
                && existing.Descriptor.LiveTenantId == line.LiveTenant
                && (location is null
                    || (string.Equals(existing.Location.Server, location.Server, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(existing.Location.Database, location.Database, StringComparison.OrdinalIgnoreCase)));
        }
    }
}
