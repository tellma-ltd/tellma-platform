// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Data;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Provisioning;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Migrator
{
    /// <summary>
    ///     One tenant's part of a command, under the tenant's migration lock: <see cref="ProvisionAsync" />
    ///     brings a provisioning tenant to active, <see cref="MigrateAsync" /> brings an existing one up
    ///     to this migrator's model. Both converge: a re-run does only what is left.
    /// </summary>
    /// <param name="context">The run.</param>
    internal sealed class TenantMigration(MigratorContext context)
    {
        /// <summary>The longest a migration's statements may run; a large table's rebuild takes a while.</summary>
        private static readonly TimeSpan MigrationCommandTimeout = TimeSpan.FromHours(1);

        private IServiceProvider Services => context.Services;

        /// <summary>
        ///     Provisions a tenant whose row is <see cref="TenantState.Provisioning" />: the database,
        ///     created when missing and checked when it exists; on a fresh one read-committed
        ///     snapshot, the query store and the application principal; the role, the migrations, the
        ///     fingerprint and the grants; the version-tag seed; every due step; the administrator's
        ///     membership hint and invitation; then the state <see cref="TenantState.Active" />.
        /// </summary>
        /// <remarks>
        ///     The row is read again once the tenant's lock is held, because an overlapping run may
        ///     have provisioned the tenant, or an operator moved it, since the caller listed it: an
        ///     active or read-only tenant is reported provisioned already, and any other state but
        ///     provisioning is a failure.
        /// </remarks>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="administrator">The first administrator the run names.</param>
        /// <param name="cancellationToken">Cancels between stages.</param>
        /// <returns>The outcome; a failure is reported, never thrown.</returns>
        public async Task<TenantOutcome> ProvisionAsync(int tenantId, ProvisioningAdministrator administrator, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(administrator);

            return await GuardAsync(tenantId, async () =>
            {
                await using MigrationLock held = await LockAsync(tenantId, cancellationToken).ConfigureAwait(false);

                // --- The row as the catalog holds it under the lock: another run may have finished it ---
                TenantInfo? tenant = await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false);
                if (tenant is not { Descriptor.State: TenantState.Provisioning })
                {
                    return tenant switch
                    {
                        null => TenantOutcome.Failure(tenantId, "the catalog holds no such tenant"),
                        { Descriptor.State: TenantState.Active or TenantState.ReadOnly } => TenantOutcome.AlreadyProvisioned(tenantId),
                        _ => TenantOutcome.Failure(tenantId, string.Create(CultureInfo.InvariantCulture, $"the tenant is {tenant.Descriptor.State}; only a provisioning tenant is provisioned")),
                    };
                }

                // --- The database and what a fresh one gets before its first migration ---
                TargetDatabase database = Target(tenant);
                bool created = await database.EnsureExistsAsync(create: true, SqlOptions.CreateDatabaseOptions, cancellationToken).ConfigureAwait(false);
                if (created)
                {
                    MigratorLog.DatabaseCreated(context.Logger, database.Name, database.Server);
                }

                await PrepareAsync(database, provisioning: true, cancellationToken).ConfigureAwait(false);

                // --- Migrations, fingerprint, grants, version tags and steps; a failure after a change still moves the tags ---
                ITenantVersionTagSeeder? tags = Services.GetService<ITenantVersionTagSeeder>();
                TagMove move = new();
                (MigrationResult migration, IReadOnlyList<string> steps) = await MovingTagsOnFailureAsync(tenant.Id, tags, move, async () =>
                {
                    MigrationResult migration = await MigrateDatabaseAsync(tenant, held, move, cancellationToken).ConfigureAwait(false);
                    if (!migration.Current)
                    {
                        return (migration, (IReadOnlyList<string>)[]);
                    }

                    if (tags is not null)
                    {
                        await tags.SeedAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                    }

                    IReadOnlyList<string> steps = await RunStepsAsync(move, tenant.Descriptor, isNew: true, administrator.Subject, administrator.Email, held, cancellationToken).ConfigureAwait(false);
                    await MoveTagsAsync(tenant.Id, tags, move, cancellationToken).ConfigureAwait(false);
                    return (migration, steps);
                }).ConfigureAwait(false);

                if (!migration.Current)
                {
                    return TenantOutcome.Failure(tenantId, $"its database is ahead of this migrator, whose last migration is {migration.LastMigration}; provision it with the migrator that migrated it");
                }

                // --- The administrator: the membership hint when the subject is known, then the invitation ---
                if (administrator.Subject is not null)
                {
                    await Services.GetRequiredService<TenantMembershipDirectory>().RecordAsync(
                        [new TenantMembershipRecord { TenantId = tenant.Id, Subject = administrator.Subject, IsActive = true }],
                        TenantMembershipWriteSource.Provision,
                        cancellationToken).ConfigureAwait(false);
                }

                if (administrator.Email is not null && IsRegistered<ITenantAdministratorInviter>())
                {
                    await held.EnsureHeldAsync(cancellationToken).ConfigureAwait(false);
                    await InSystemScopeAsync(tenant.Id, async scope =>
                    {
                        ITenantAdministratorInviter inviter = scope.GetRequiredService<ITenantAdministratorInviter>();
                        await inviter.InviteAsync(new TenantProvisioningContext(tenant.Descriptor, scope, IsNew: true, administrator.Subject, administrator.Email), cancellationToken).ConfigureAwait(false);
                    }, cancellationToken).ConfigureAwait(false);
                }

                // --- Active, only while this run still holds the lock ---
                await held.EnsureHeldAsync(cancellationToken).ConfigureAwait(false);
                _ = await Services.GetRequiredService<ITenantCatalog>()
                    .SetStateAsync(tenantId, TenantState.Active, reason: null, MigratorContext.Actor, cancellationToken)
                    .ConfigureAwait(false);

                return new TenantOutcome(tenantId, TenantOutcome.Provisioned, Applied(migration.Applied, steps.Count), []);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        ///     Migrates an existing tenant: the database's collation check, the role, the migrations,
        ///     the fingerprint and the grants; then, unless the database is ahead, the version-tag seed;
        ///     then, unless the tenant is suspended, every due step and the drift scan.
        /// </summary>
        /// <remarks>
        ///     The row is read again once the tenant's lock is held, because an operator may have
        ///     frozen the tenant, or another run changed it, since the caller listed it: a tenant no
        ///     longer active, read-only or suspended is reported by its state and left alone.
        /// </remarks>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels between stages.</param>
        /// <returns>The outcome; a failure is reported, never thrown.</returns>
        public async Task<TenantOutcome> MigrateAsync(int tenantId, CancellationToken cancellationToken)
        {
            return await GuardAsync(tenantId, async () =>
            {
                await using MigrationLock held = await LockAsync(tenantId, cancellationToken).ConfigureAwait(false);

                // --- The row as the catalog holds it under the lock: a tenant frozen meanwhile is skipped ---
                TenantInfo? tenant = await ReadAsync(tenantId, cancellationToken).ConfigureAwait(false);
                if (tenant is null)
                {
                    return TenantOutcome.Failure(tenantId, "the catalog holds no such tenant");
                }

                if (TenantOutcome.Unmigrated(tenantId, tenant.Descriptor.State) is { } unmigrated)
                {
                    return unmigrated;
                }

                // --- The database must exist: only provisioning creates one ---
                TargetDatabase database = Target(tenant);
                _ = await database.EnsureExistsAsync(create: false, SqlOptions.CreateDatabaseOptions, cancellationToken).ConfigureAwait(false);
                await PrepareAsync(database, provisioning: false, cancellationToken).ConfigureAwait(false);

                // --- Migrations, fingerprint, grants, the seed, then (unless suspended) the due steps ---
                // A failure after any of them changed the database still moves the tags on the way out:
                // the next run may apply nothing and run nothing, and would never move them.
                ITenantVersionTagSeeder? tags = Services.GetService<ITenantVersionTagSeeder>();
                TagMove move = new();
                (MigrationResult migration, IReadOnlyList<string> steps) = await MovingTagsOnFailureAsync(tenant.Id, tags, move, async () =>
                {
                    MigrationResult migration = await MigrateDatabaseAsync(tenant, held, move, cancellationToken).ConfigureAwait(false);
                    if (!migration.Current)
                    {
                        return (migration, (IReadOnlyList<string>)[]);
                    }

                    if (tags is not null)
                    {
                        await tags.SeedAsync(tenant.Id, cancellationToken).ConfigureAwait(false);
                    }

                    IReadOnlyList<string> steps = tenant.Descriptor.State == TenantState.Suspended
                        ? []
                        : await RunStepsAsync(move, tenant.Descriptor, isNew: false, adminSubject: null, adminEmail: null, held, cancellationToken).ConfigureAwait(false);
                    await MoveTagsAsync(tenant.Id, tags, move, cancellationToken).ConfigureAwait(false);
                    return (migration, steps);
                }).ConfigureAwait(false);

                // --- An ahead database stops here; a suspended tenant stops before its steps ---
                if (!migration.Current)
                {
                    MigratorLog.TenantAhead(context.Logger, tenantId, migration.LastMigration);
                    return new TenantOutcome(tenantId, TenantOutcome.Ahead, $"this migrator's last migration is {migration.LastMigration}", []);
                }

                if (tenant.Descriptor.State == TenantState.Suspended)
                {
                    return new TenantOutcome(tenant.Id, TenantOutcome.SuspendedStepsDeferred, Applied(migration.Applied, steps: 0), []);
                }

                // --- The drift scan ---

                IReadOnlyList<string> drift = [];
                if (IsRegistered<ITenantDriftReport>())
                {
                    await InSystemScopeAsync(tenant.Id, async scope =>
                    {
                        drift = await scope.GetRequiredService<ITenantDriftReport>().ScanAsync(cancellationToken).ConfigureAwait(false);
                    }, cancellationToken).ConfigureAwait(false);
                }

                return new TenantOutcome(tenantId, TenantOutcome.Migrated, Applied(migration.Applied, steps.Count), drift);
            }, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The SQL options in effect.</summary>
        private TellmaSqlOptions SqlOptions => Services.GetRequiredService<IOptionsMonitor<TellmaSqlOptions>>().CurrentValue;

        /// <summary>The report's detail of what a run applied.</summary>
        /// <param name="migrations">How many migrations were applied.</param>
        /// <param name="steps">How many steps ran.</param>
        /// <returns>The detail.</returns>
        private static string Applied(int migrations, int steps)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{migrations} migration(s), {steps} step(s)");
        }

        /// <summary>
        ///     Runs a tenant's work and turns any failure into a reported outcome, so one tenant's
        ///     failure never stops the others. Only the run's own cancellation escapes: a cancellation
        ///     raised inside the work for another reason, such as an HTTP client's timeout, is the
        ///     tenant's failure.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="work">The work.</param>
        /// <param name="cancellationToken">The run's cancellation.</param>
        /// <returns>The work's outcome, or the failure.</returns>
        private async Task<TenantOutcome> GuardAsync(int tenantId, Func<Task<TenantOutcome>> work, CancellationToken cancellationToken)
        {
            try
            {
                return await work().ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                MigratorLog.TenantFailed(context.Logger, tenantId, exception);
                return TenantOutcome.Failure(tenantId, TenantOutcome.Describe(exception));
            }
        }

        /// <summary>Takes the tenant's migration lock in the catalog.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the wait.</param>
        /// <returns>The held lock.</returns>
        private Task<MigrationLock> LockAsync(int tenantId, CancellationToken cancellationToken)
        {
            return MigrationLock.AcquireAsync(Services.GetRequiredService<CatalogConnection>().ConnectionString, MigrationLock.TenantResource(tenantId), cancellationToken);
        }

        /// <summary>The tenant's row as the catalog holds it now, read from the store rather than the snapshot.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row, or null when the catalog holds none.</returns>
        private async Task<TenantInfo?> ReadAsync(int tenantId, CancellationToken cancellationToken)
        {
            IReadOnlyList<TenantInfo> tenants = await Services.GetRequiredService<ITenantCatalog>().ListAsync(cancellationToken).ConfigureAwait(false);
            return tenants.FirstOrDefault(tenant => tenant.Id == tenantId);
        }

        /// <summary>The tenant's database, over the connection string the registry composed for it.</summary>
        /// <param name="tenant">The tenant.</param>
        /// <returns>The database.</returns>
        /// <exception cref="MigratorFailureException">The registry could not compose the tenant's connection string.</exception>
        private static TargetDatabase Target(TenantInfo tenant)
        {
            return string.IsNullOrEmpty(tenant.ConnectionString)
                ? throw new MigratorFailureException($"Tenant {tenant.Id}'s catalog row names a location this deployment refuses ({tenant.Location.Server}, {tenant.Location.Database}, profile '{tenant.Location.CredentialProfile}').")
                : new TargetDatabase(tenant.ConnectionString);
        }

        /// <summary>
        ///     Checks the database's collation; on a fresh database a provisioning turns on
        ///     read-committed snapshot and the query store; then the role, which every table type's
        ///     migration grants, and on a fresh provisioned database the application principal.
        /// </summary>
        /// <param name="database">The database.</param>
        /// <param name="provisioning">Whether the tenant is being provisioned, so a fresh database gets the provisioning statements.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>A task that completes when the database is ready to migrate.</returns>
        private async Task PrepareAsync(TargetDatabase database, bool provisioning, CancellationToken cancellationToken)
        {
            SqlConnection connection = await database.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                await database.CheckCollationAsync(connection, cancellationToken).ConfigureAwait(false);
                bool fresh = provisioning && await TargetDatabase.IsFreshAsync(connection, ApplicationGrants.DefaultSchema, cancellationToken).ConfigureAwait(false);

                // --- A fresh database: its isolation and its query store, before anything else connects ---
                if (fresh)
                {
                    await TargetDatabase.EnableReadCommittedSnapshotAsync(connection, cancellationToken).ConfigureAwait(false);
                    await TargetDatabase.EnableQueryStoreAsync(connection, cancellationToken).ConfigureAwait(false);
                }

                // --- The role, then the application's user in it ---
                await TargetDatabase.ExecuteAsync(connection, ApplicationGrants.EnsureRole, cancellationToken).ConfigureAwait(false);
                if (fresh && SqlOptions.ApplicationPrincipal is { Length: > 0 } principal)
                {
                    await TargetDatabase.ExecuteAsync(connection, ApplicationGrants.ForPrincipal(principal, database.EngineEdition), cancellationToken).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        ///     Runs the tenant's due steps. A step that completed changed the database, so a failure
        ///     part-way marks the tags due: the steps that completed are recorded and will not run again.
        /// </summary>
        /// <param name="move">Whether the run changed the database.</param>
        /// <param name="tenant">The tenant.</param>
        /// <param name="isNew">Whether the tenant is being provisioned.</param>
        /// <param name="adminSubject">The first administrator's subject, or null.</param>
        /// <param name="adminEmail">The first administrator's email address, or null.</param>
        /// <param name="held">The tenant's lock, confirmed before each step.</param>
        /// <param name="cancellationToken">Cancels between steps.</param>
        /// <returns>The names of the steps that ran.</returns>
        private async Task<IReadOnlyList<string>> RunStepsAsync(TagMove move, TenantDescriptor tenant, bool isNew, string? adminSubject, string? adminEmail, MigrationLock held, CancellationToken cancellationToken)
        {
            try
            {
                IReadOnlyList<string> steps = await Services.GetRequiredService<ProvisioningStepRunner>()
                    .RunAsync(tenant, isNew, adminSubject, adminEmail, held.EnsureHeldAsync, cancellationToken)
                    .ConfigureAwait(false);
                move.Due |= steps.Count > 0;
                return steps;
            }
            catch
            {
                // Which steps completed before the failure is not known here; moving the tags when
                // none did costs the tenant's caches one reload.
                move.Due = true;
                throw;
            }
        }

        /// <summary>Moves every version tag when the run changed the database, once.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="tags">The seeder, or null when none is registered.</param>
        /// <param name="move">Whether the run changed the database; cleared once the tags moved.</param>
        /// <param name="cancellationToken">Cancels the bump.</param>
        /// <returns>A task that completes when the tags moved, or at once when none were due.</returns>
        private static async Task MoveTagsAsync(int tenantId, ITenantVersionTagSeeder? tags, TagMove move, CancellationToken cancellationToken)
        {
            if (tags is not null && move.Due)
            {
                await tags.BumpAllAsync(tenantId, cancellationToken).ConfigureAwait(false);
                move.Due = false;
            }
        }

        /// <summary>
        ///     Runs a tenant's migrating work; when it fails after changing the database and before
        ///     moving the version tags, moves them on the way out, then lets the failure surface. A
        ///     failed move is logged and never replaces the failure.
        /// </summary>
        /// <typeparam name="T">The work's result.</typeparam>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="tags">The seeder, or null when none is registered.</param>
        /// <param name="move">Whether the run changed the database.</param>
        /// <param name="work">The work.</param>
        /// <returns>The work's result.</returns>
        private async Task<T> MovingTagsOnFailureAsync<T>(int tenantId, ITenantVersionTagSeeder? tags, TagMove move, Func<Task<T>> work)
        {
            try
            {
                return await work().ConfigureAwait(false);
            }
            catch when (tags is not null && move.Due)
            {
                try
                {
                    // Not the run's token: a cancelled run still leaves the changes it committed, and
                    // one update moves the tags.
                    await tags.BumpAllAsync(tenantId, CancellationToken.None).ConfigureAwait(false);
                }
                catch (Exception bumpFailure)
                {
                    MigratorLog.VersionTagBumpFailed(context.Logger, tenantId, bumpFailure);
                }

                throw;
            }
        }

        /// <summary>
        ///     Applies the tenant model's pending migrations, records the fingerprint when the history
        ///     ends at this migrator's last migration, and recomputes the grants.
        /// </summary>
        /// <param name="tenant">The tenant.</param>
        /// <param name="held">The tenant's lock, confirmed before the fingerprint is written.</param>
        /// <param name="move">Marked due before any pending migration is applied.</param>
        /// <param name="cancellationToken">Cancels the work.</param>
        /// <returns>How many migrations were applied, and whether the database is current or ahead.</returns>
        private async Task<MigrationResult> MigrateDatabaseAsync(TenantInfo tenant, MigrationLock held, TagMove move, CancellationToken cancellationToken)
        {
            TellmaModelFactory models = Services.GetRequiredService<TellmaModelFactory>();
            string last;
            int applied;

            // --- The migrations ---
            await using (TellmaDbContext migrations = models.CreateMigrationContext(tenant.ConnectionString, context.MigrationsAssembly))
            {
                migrations.Database.SetCommandTimeout(MigrationCommandTimeout);
                last = migrations.Database.GetMigrations().LastOrDefault()
                    ?? throw new MigratorFailureException($"The assembly {context.MigrationsAssembly.GetName().Name} carries no migration of the tenant model; scaffold one with dotnet ef migrations add.");

                applied = (await migrations.Database.GetPendingMigrationsAsync(cancellationToken).ConfigureAwait(false)).Count();

                // Marked before migrating: a failure part-way may leave some of them applied.
                move.Due |= applied > 0;
                await migrations.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
            }

            if (applied > 0)
            {
                string database = new SqlConnectionStringBuilder(tenant.ConnectionString).InitialCatalog;
                MigratorLog.MigrationsApplied(context.Logger, database, applied);
            }

            // --- The fingerprint, then the grants; a migration that outlived the lock's session writes neither ---
            await held.EnsureHeldAsync(cancellationToken).ConfigureAwait(false);
            SqlConnection connection = await new TargetDatabase(tenant.ConnectionString).OpenAsync(cancellationToken).ConfigureAwait(false);
            await using (connection.ConfigureAwait(false))
            {
                bool current = await SchemaFingerprints.RecordAsync(
                    connection,
                    Services.GetRequiredService<IEntityMetadataProvider>().StorageFingerprint,
                    last,
                    Services.GetRequiredService<DeploymentVersions>().PlatformVersion,
                    cancellationToken).ConfigureAwait(false);

                await TargetDatabase.ExecuteAsync(connection, ApplicationGrants.ForTenant(models.BuildModel()), cancellationToken).ConfigureAwait(false);
                return new MigrationResult(applied, current, last);
            }
        }

        /// <summary>Whether a service is registered, so a seam with no implementation costs no scope.</summary>
        /// <typeparam name="T">The service.</typeparam>
        /// <returns>True when registered.</returns>
        private bool IsRegistered<T>()
        {
            return Services.GetService<IServiceProviderIsService>()?.IsService(typeof(T)) ?? (Services.GetService<T>() is not null);
        }

        /// <summary>Runs work in a system scope of the tenant, which may still be provisioning.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="work">The work, given the scope's services.</param>
        /// <param name="cancellationToken">Cancels the scope's creation.</param>
        /// <returns>A task that completes when the work did.</returns>
        private async Task InSystemScopeAsync(int tenantId, Func<IServiceProvider, Task> work, CancellationToken cancellationToken)
        {
            TenantScope scope = await Services.GetRequiredService<ITenantScopeFactory>()
                .CreateScopeAsync(ProvisioningStepRunner.SystemSnapshot(tenantId), allowNonActive: true, cancellationToken)
                .ConfigureAwait(false);

            await using (scope.ConfigureAwait(false))
            {
                await work(scope.Services).ConfigureAwait(false);
            }
        }

        /// <summary>What migrating a tenant database did.</summary>
        /// <param name="Applied">How many migrations were applied.</param>
        /// <param name="Current">True when the history ends at this migrator's last migration; false when the database is ahead.</param>
        /// <param name="LastMigration">This migrator's last migration.</param>
        private sealed record MigrationResult(int Applied, bool Current, string LastMigration);

        /// <summary>Whether a run changed the tenant database since it last moved the version tags.</summary>
        private sealed class TagMove
        {
            /// <summary>True once a migration or step may have changed the database and the tags have not moved since.</summary>
            public bool Due { get; set; }
        }
    }
}
