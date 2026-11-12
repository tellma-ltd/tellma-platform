// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Composition;
using Tellma.Core.Data;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator.Commands
{
    /// <summary>
    ///     <c>status</c>: the catalog's pending migrations; each tenant's state, recorded schema
    ///     fingerprints against the running model's, and pending migrations; then the composition's
    ///     problems (spec 0011 §6.1). It writes nothing.
    /// </summary>
    /// <param name="context">The run.</param>
    internal sealed class StatusCommand(MigratorContext context)
    {
        /// <summary>Runs the command.</summary>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>
        ///     <see cref="MigratorExitCodes.CompositionProblems" /> when the composition has problems,
        ///     <see cref="MigratorExitCodes.PartialFailure" /> when the catalog or a tenant could not be
        ///     read, else <see cref="MigratorExitCodes.Converged" />.
        /// </returns>
        public async Task<int> RunAsync(CancellationToken cancellationToken)
        {
            bool unreadable = false;
            string model = context.Services.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint;
            context.WriteLine($"model: fingerprint {model}");

            // --- The catalog ---
            CatalogConnection catalog = context.Services.GetRequiredService<CatalogConnection>();
            IReadOnlyList<TenantInfo> tenants = [];
            try
            {
                await using (var migrations = CatalogDbContext.Create(catalog.ConnectionString))
                {
                    WritePending("catalog", [.. await migrations.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)]);
                }

                tenants = await context.Services.GetRequiredService<ITenantCatalog>().ListAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                context.WriteLine($"catalog: unavailable ({TenantOutcome.Describe(exception)})");
                unreadable = true;
            }

            // --- Each tenant, by id ---
            TellmaModelFactory models = context.Services.GetRequiredService<TellmaModelFactory>();
            foreach (TenantInfo tenant in tenants.OrderBy(static tenant => tenant.Id))
            {
                string prefix = string.Create(CultureInfo.InvariantCulture, $"tenant {tenant.Id}");
                context.WriteLine($"{prefix}: {tenant.Descriptor.State}");
                try
                {
                    unreadable |= !await WriteTenantAsync(prefix, tenant, models, model, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    context.WriteLine($"{prefix}: unavailable ({TenantOutcome.Describe(exception)})");
                    unreadable = true;
                }
            }

            // --- The composition, validated without a host ---
            IReadOnlyList<CompositionProblem> problems = TellmaComposition.Validate(context.Slug, context.Configuration, context.Compose);
            foreach (CompositionProblem problem in problems)
            {
                context.WriteLine($"composition: {problem.Describe()}");
            }

            return problems.Count > 0 ? MigratorExitCodes.CompositionProblems
                : unreadable ? MigratorExitCodes.PartialFailure
                : MigratorExitCodes.Converged;
        }

        /// <summary>Writes one tenant's fingerprints and pending migrations.</summary>
        /// <param name="prefix">The tenant's line prefix.</param>
        /// <param name="tenant">The tenant.</param>
        /// <param name="models">The tenant model's factory.</param>
        /// <param name="model">The running model's fingerprint.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>False when the tenant's database could not be read.</returns>
        private async Task<bool> WriteTenantAsync(string prefix, TenantInfo tenant, TellmaModelFactory models, string model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(tenant.ConnectionString))
            {
                context.WriteLine($"{prefix}: unavailable (its catalog row names a location this deployment refuses)");
                return false;
            }

            TargetDatabase database = new(tenant.ConnectionString);
            SqlConnection connection;
            try
            {
                connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqlException) when (tenant.Descriptor.State == Abstractions.Tenancy.TenantState.Provisioning)
            {
                // A provisioning tenant's database may not have been created yet.
                context.WriteLine($"{prefix}: no database yet");
                return true;
            }

            // --- The recorded fingerprints, newest first, against the model's ---
            await using (connection.ConfigureAwait(false))
            {
                if (await TargetDatabase.IsFreshAsync(connection, ApplicationGrants.DefaultSchema, cancellationToken).ConfigureAwait(false))
                {
                    context.WriteLine($"{prefix}: fingerprint none (not migrated)");
                }
                else
                {
                    IReadOnlyList<SchemaFingerprintRow> rows = await SchemaFingerprints.ReadAsync(connection, cancellationToken).ConfigureAwait(false);
                    if (rows.Count == 0)
                    {
                        context.WriteLine($"{prefix}: fingerprint none");
                    }

                    foreach (SchemaFingerprintRow row in rows)
                    {
                        string match = string.Equals(row.Fingerprint, model, StringComparison.OrdinalIgnoreCase) ? "matches the model" : "differs from the model";
                        context.WriteLine(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{prefix}: fingerprint {row.Fingerprint} ({match}; {row.MigrationId}, platform {row.PlatformVersion}, {row.AppliedAt:yyyy-MM-dd HH:mm:ss zzz})"));
                    }
                }
            }

            // --- The pending migrations ---
            await using TellmaDbContext migrations = models.CreateMigrationContext(tenant.ConnectionString, context.MigrationsAssembly);
            WritePending(prefix, [.. await migrations.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)]);
            return true;
        }

        /// <summary>Writes a database's pending migrations: their count, then one line each.</summary>
        /// <param name="prefix">The database's line prefix.</param>
        /// <param name="pending">The pending migrations, in order.</param>
        private void WritePending(string prefix, IReadOnlyList<string> pending)
        {
            context.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{prefix}: {pending.Count} pending migration(s)"));
            foreach (string migration in pending)
            {
                context.WriteLine($"{prefix}: pending {migration}");
            }
        }
    }
}
