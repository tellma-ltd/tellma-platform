// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Migrator.CommandLine;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     <c>migrate</c>: brings the catalog, the in-proc identity store and every tenant up to this
    ///     migrator, registers and provisions the seed tenants, and prints one line per tenant
    ///     (spec 0011 §6.1).
    /// </summary>
    /// <param name="context">The run.</param>
    internal sealed class MigrateCommand(MigratorContext context)
    {
        /// <summary>Runs the command.</summary>
        /// <param name="line">The parsed command line.</param>
        /// <param name="cancellationToken">Stops the run between tenants.</param>
        /// <returns><see cref="MigratorExitCodes.Converged" />, or <see cref="MigratorExitCodes.PartialFailure" /> when the catalog or any tenant failed.</returns>
        public async Task<int> RunAsync(MigrateCommandLine line, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(line);
            ConcurrentDictionary<int, TenantOutcome> outcomes = new();

            // --- The catalog, the identity store and the seed registrations, under the catalog lock ---
            IReadOnlyList<int> seeded;
            try
            {
                (int applied, seeded) = await MigrateCatalogAsync(registerSeeds: !line.CatalogOnly, outcomes, cancellationToken).ConfigureAwait(false);
                context.WriteLine(string.Create(CultureInfo.InvariantCulture, $"catalog: migrated ({applied} migration(s))"));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                MigratorLog.CatalogFailed(context.Logger, exception);
                context.WriteLine($"catalog: failed ({TenantOutcome.Describe(exception)})");
                return MigratorExitCodes.PartialFailure;
            }

            if (line.CatalogOnly)
            {
                return MigratorExitCodes.Converged;
            }

            // --- The seed tenants still provisioning, under their own locks ---
            TenantMigration migration = new(context);
            IReadOnlyList<TenantInfo> tenants = await context.Services.GetRequiredService<ITenantCatalog>().ListAsync(cancellationToken).ConfigureAwait(false);
            ProvisioningAdministrator administrator = context.Administrator(email: null, subject: null);
            List<TenantInfo> seeds = [.. tenants.Where(tenant => seeded.Contains(tenant.Id) && tenant.Descriptor.State == TenantState.Provisioning)];
            await Parallel.ForEachAsync(
                seeds,
                new ParallelOptions { MaxDegreeOfParallelism = line.Parallelism, CancellationToken = cancellationToken },
                async (tenant, token) => outcomes[tenant.Id] = await migration.ProvisionAsync(tenant, administrator, token).ConfigureAwait(false))
                .ConfigureAwait(false);

            // --- Every other tenant: migrated, skipped or reported by its state ---
            List<TenantInfo> targets = [];
            foreach (TenantInfo tenant in Targets(tenants, line.Tenants, outcomes))
            {
                switch (tenant.Descriptor.State)
                {
                    case TenantState.Active or TenantState.ReadOnly or TenantState.Suspended:
                        targets.Add(tenant);
                        break;

                    case TenantState.Frozen:
                        outcomes[tenant.Id] = TenantOutcome.Of(tenant.Id, TenantOutcome.Frozen);
                        break;

                    case TenantState.Provisioning:
                        outcomes[tenant.Id] = TenantOutcome.Of(tenant.Id, TenantOutcome.Provisioning);
                        break;

                    case TenantState.Retired:
                    default:
                        outcomes[tenant.Id] = TenantOutcome.Of(tenant.Id, TenantOutcome.Retired);
                        break;
                }
            }

            await Parallel.ForEachAsync(
                targets,
                new ParallelOptions { MaxDegreeOfParallelism = line.Parallelism, CancellationToken = cancellationToken },
                async (tenant, token) => outcomes[tenant.Id] = await migration.MigrateAsync(tenant, token).ConfigureAwait(false))
                .ConfigureAwait(false);

            // --- A named tenant the catalog does not hold ---
            foreach (int missing in line.Tenants.Where(id => !tenants.Any(tenant => tenant.Id == id)))
            {
                outcomes[missing] = TenantOutcome.Failure(missing, "the catalog holds no such tenant");
            }

            // --- The report, by tenant id ---
            foreach (TenantOutcome outcome in outcomes.Values.OrderBy(static outcome => outcome.TenantId))
            {
                foreach (string reportLine in outcome.Lines())
                {
                    context.WriteLine(reportLine);
                }
            }

            return outcomes.Values.Any(static outcome => outcome.IsFailure) ? MigratorExitCodes.PartialFailure : MigratorExitCodes.Converged;
        }

        /// <summary>
        ///     The tenants the fan-out considers: every row, or the named ones, less those this run
        ///     already settled (the seed tenants it provisioned and the registrations that failed).
        /// </summary>
        /// <param name="tenants">Every catalog row.</param>
        /// <param name="named">The named tenants; empty for every one.</param>
        /// <param name="settled">The outcomes recorded so far.</param>
        /// <returns>The tenants.</returns>
        private static IEnumerable<TenantInfo> Targets(IReadOnlyList<TenantInfo> tenants, IReadOnlyList<int> named, ConcurrentDictionary<int, TenantOutcome> settled)
        {
            IEnumerable<TenantInfo> selected = named.Count == 0 ? tenants : tenants.Where(tenant => named.Contains(tenant.Id));
            return selected.Where(tenant => !settled.ContainsKey(tenant.Id) && (named.Count > 0 || tenant.Descriptor.State != TenantState.Retired));
        }

        /// <summary>
        ///     The catalog's part: the database, created when missing; the catalog lock; the role and,
        ///     in a fresh catalog, the application principal; the catalog's migrations and the identity
        ///     store's; the grants; the registry's reload; and the seed tenants' registrations.
        /// </summary>
        /// <param name="registerSeeds">Whether the seed tenants are registered.</param>
        /// <param name="outcomes">Where a seed whose registration failed is reported.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>How many catalog migrations were applied, and the ids of the configured seed tenants.</returns>
        private async Task<(int Applied, IReadOnlyList<int> Seeded)> MigrateCatalogAsync(
            bool registerSeeds, ConcurrentDictionary<int, TenantOutcome> outcomes, CancellationToken cancellationToken)
        {
            CatalogConnection catalog = context.Services.GetRequiredService<CatalogConnection>();
            TellmaSqlOptions sql = context.Services.GetRequiredService<IOptionsMonitor<TellmaSqlOptions>>().CurrentValue;

            // --- The database, created when missing; read-committed snapshot in a fresh one, before the lock ---
            TargetDatabase database = new(catalog.ConnectionString);
            if (await database.EnsureExistsAsync(create: true, sql.CreateDatabaseOptions, cancellationToken).ConfigureAwait(false))
            {
                MigratorLog.DatabaseCreated(context.Logger, database.Name, database.Server);
            }

            bool fresh;
            SqlConnection connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await database.CheckCollationAsync(connection, cancellationToken).ConfigureAwait(false);
                fresh = await TargetDatabase.IsFreshAsync(connection, CatalogDbContext.Schema, cancellationToken).ConfigureAwait(false);
                if (fresh)
                {
                    await TargetDatabase.EnableReadCommittedSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
                }
            }

            await using MigrationLock held = await MigrationLock.AcquireAsync(catalog.ConnectionString, MigrationLock.CatalogResource, cancellationToken).ConfigureAwait(false);

            // --- The role, and the application's user in a fresh catalog ---
            connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await TargetDatabase.ExecuteAsync(connection, ApplicationGrants.EnsureRole, cancellationToken).ConfigureAwait(false);
                if (fresh && sql.ApplicationPrincipal is { Length: > 0 } principal)
                {
                    await TargetDatabase.ExecuteAsync(connection, ApplicationGrants.ForPrincipal(principal, database.EngineEdition), cancellationToken).ConfigureAwait(false);
                }
            }

            // --- The catalog's migrations, then the identity store's ---
            int applied;
            IReadOnlyList<string> grants;
            await using (var migrations = CatalogDbContext.Create(catalog.ConnectionString))
            {
                applied = (await migrations.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();
                await migrations.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                grants = ApplicationGrants.ForCatalog(migrations.Model, context.InProcIdentity);
            }

            if (applied > 0)
            {
                MigratorLog.MigrationsApplied(context.Logger, database.Name, applied);
            }

            if (context.InProcIdentity)
            {
                // The usage check refused in-proc mode without the delegate before the host started.
                await context.MigrateIdentityStore!(catalog.ConnectionString).ConfigureAwait(false);
            }

            // --- The grants, recomputed over everything the catalog now holds ---
            connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await TargetDatabase.ExecuteAsync(connection, grants, cancellationToken).ConfigureAwait(false);
            }

            // --- The registry, then the seed tenants the catalog lacks ---
            await context.Services.GetRequiredService<ITenantRegistry>().RefreshAsync(force: true, cancellationToken).ConfigureAwait(false);
            if (!registerSeeds)
            {
                return (applied, []);
            }

            List<SeedTenant> seeds = context.Configuration.GetSection(MigratorContext.SeedTenantsKey).Get<List<SeedTenant>>() ?? [];
            ITenantCatalog tenants = context.Services.GetRequiredService<ITenantCatalog>();
            HashSet<int> existing = [.. (await tenants.ListAsync(cancellationToken).ConfigureAwait(false)).Select(static tenant => tenant.Id)];
            foreach (SeedTenant seed in seeds.Where(seed => !existing.Contains(seed.Id)))
            {
                try
                {
                    string name = !string.IsNullOrWhiteSpace(seed.Name)
                        ? seed.Name
                        : throw new MigratorFailureException(string.Create(CultureInfo.InvariantCulture, $"The seed tenant {seed.Id} has no name; set {MigratorContext.SeedTenantsKey}'s Name."));

                    _ = await tenants.RegisterAsync(new TenantRegistration(name, seed.Category, seed.LiveTenantId, Location: null), MigratorContext.Actor, seed.Id, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is MigratorFailureException or TenantStateException or ArgumentException)
                {
                    outcomes[seed.Id] = TenantOutcome.Failure(seed.Id, TenantOutcome.Describe(exception));
                }
            }

            return (applied, [.. seeds.Select(static seed => seed.Id)]);
        }
    }
}
