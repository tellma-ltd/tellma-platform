// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Caching;
using Tellma.Core.Migrator;
using Tellma.Core.Provisioning;

namespace Tellma.Core.IntegrationTests.Migrator
{
    /// <summary>
    ///     The migrator's version-tag seed against a real server: a row for every registered name on
    ///     every run, every tag moved after a run that applied a migration or a step and never after
    ///     one that changed nothing, a name registered later seeded on the next run, and a lost row
    ///     restored.
    /// </summary>
    [Trait("Category", "Integration")]
    public class VersionTagSeedTests(SqlServerFixture sql)
    {
        /// <summary>The names the test distribution registers: the platform's three.</summary>
        private static readonly string[] PlatformNames = [VersionTagNames.Entities, VersionTagNames.Permissions, VersionTagNames.Settings];

        [Fact]
        public async Task A_provisioned_tenant_has_a_row_for_every_registered_name()
        {
            MigratorHarness migrator = new(sql, "vt_seed");
            migrator.Seed(1);

            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            Dictionary<string, Guid> tags = await TagsAsync(migrator.TenantConnectionString(1));
            Assert.Equal(PlatformNames, tags.Keys.Order(StringComparer.Ordinal));
            Assert.DoesNotContain(Guid.Empty, tags.Values);
        }

        [Fact]
        public async Task A_run_that_changes_nothing_moves_no_tag_and_a_run_that_runs_a_step_moves_every_tag()
        {
            MigratorHarness migrator = new(sql, "vt_bump")
            {
                Compose = static builder => builder.AddFeature(new ProbeStepFeature()),
            };
            migrator.Values[ProbeStep.RunKeyKey] = migrator.CatalogName;
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            string tenant = migrator.TenantConnectionString(1);
            Dictionary<string, Guid> first = await TagsAsync(tenant);

            // --- Nothing applied, nothing run: every tag stays ---
            MigratorResult same = await migrator.RunAsync("migrate");
            Assert.Contains("tenant 1: migrated (0 migration(s), 0 step(s))", same.Lines);
            Assert.Equal(first, await TagsAsync(tenant));

            // --- A step ran again: every tag moved, to one new value ---
            migrator.Values[ProbeStep.VersionKey] = "3";
            MigratorResult stepped = await migrator.RunAsync("migrate");
            Assert.Contains("tenant 1: migrated (0 migration(s), 1 step(s))", stepped.Lines);
            Dictionary<string, Guid> moved = await TagsAsync(tenant);
            Assert.Equal(first.Keys.Order(StringComparer.Ordinal), moved.Keys.Order(StringComparer.Ordinal));
            Assert.All(moved, pair => Assert.NotEqual(first[pair.Key], pair.Value));
        }

        [Fact]
        public async Task A_name_registered_later_is_seeded_on_the_next_run_and_a_lost_row_is_restored_without_moving_the_others()
        {
            MigratorHarness migrator = new(sql, "vt_new_name");
            migrator.Seed(1);
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            string tenant = migrator.TenantConnectionString(1);
            await SqlServerFixture.ExecuteAsync(tenant, $"DELETE FROM [core].[VersionTags] WHERE [Name] = N'{VersionTagNames.Entities}';");
            Dictionary<string, Guid> before = await TagsAsync(tenant);

            // A distribution that now registers one more name, as a newly cacheable entity would.
            migrator.Services = static services => services.AddSingleton<IVersionTagRegistry>(new NamesRegistry([.. PlatformNames, "probe.gadgets"]));
            MigratorResult result = await migrator.RunAsync("migrate");

            Assert.Contains("tenant 1: migrated (0 migration(s), 0 step(s))", result.Lines);
            Dictionary<string, Guid> after = await TagsAsync(tenant);
            Assert.Equal([VersionTagNames.Entities, VersionTagNames.Permissions, "probe.gadgets", VersionTagNames.Settings], after.Keys.Order(StringComparer.Ordinal));
            Assert.All(before, pair => Assert.Equal(pair.Value, after[pair.Key]));
        }

        [Fact]
        public async Task A_run_that_applies_a_migration_and_runs_no_step_moves_every_tag_after_the_seed()
        {
            MigratorHarness migrator = new(sql, "vt_applied");
            RecordingSeeder seeder = new(migrator.TenantConnectionString(1));
            migrator.Services = services => services.AddSingleton<ITenantVersionTagSeeder>(provider => seeder.Over(provider));
            migrator.Seed(1);

            // --- The first run applies the initial migration and runs no step: seeded, then every tag moved ---
            MigratorResult first = await migrator.RunAsync("migrate");
            Assert.True(first.ExitCode == MigratorExitCodes.Converged, first.ToString());
            Assert.Contains(first.Lines, static line => line.Contains("(1 migration(s), 0 step(s))", StringComparison.Ordinal));
            Assert.Equal(["seed", "bump"], seeder.Calls);
            Dictionary<string, Guid> moved = await TagsAsync(migrator.TenantConnectionString(1));
            Assert.All(seeder.Seeded!, pair => Assert.NotEqual(pair.Value, moved[pair.Key]));

            // --- A run that applies nothing seeds alone ---
            seeder.Calls.Clear();
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            Assert.Equal(["seed"], seeder.Calls);
        }

        [Fact]
        public async Task A_run_that_fails_after_applying_a_migration_still_moves_every_tag_and_one_that_changed_nothing_does_not()
        {
            MigratorHarness migrator = new(sql, "vt_failed");
            RecordingSeeder seeder = new(migrator.TenantConnectionString(1)) { FailSeed = true };
            migrator.Services = services => services.AddSingleton<ITenantVersionTagSeeder>(provider => seeder.Over(provider));
            migrator.Seed(1);

            // --- The migration applied, then the seed failed: the tags move on the way out ---
            MigratorResult failed = await migrator.RunAsync("migrate");
            Assert.NotEqual(MigratorExitCodes.Converged, failed.ExitCode);
            Assert.Equal(["seed", "bump"], seeder.Calls);

            // --- Provisioned on the next run, which applies nothing; a later failure moves nothing ---
            seeder.FailSeed = false;
            Assert.Equal(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            seeder.Calls.Clear();
            seeder.FailSeed = true;
            Assert.NotEqual(MigratorExitCodes.Converged, (await migrator.RunAsync("migrate")).ExitCode);
            Assert.Equal(["seed"], seeder.Calls);
        }

        /// <summary>Reads a tenant database's version tags.</summary>
        /// <param name="database">The tenant database.</param>
        /// <returns>Each name's tag.</returns>
        private static async Task<Dictionary<string, Guid>> TagsAsync(string database)
        {
            Dictionary<string, Guid> tags = new(StringComparer.Ordinal);
            await using SqlConnection connection = new(database);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT [Name], [Tag] FROM [core].[VersionTags];";
            await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
            while (await reader.ReadAsync(TestContext.Current.CancellationToken))
            {
                tags[reader.GetString(0)] = reader.GetGuid(1);
            }

            return tags;
        }

        /// <summary>
        ///     The platform's seeder, recording each call and the tags right after each seed, and
        ///     failing the seed on demand.
        /// </summary>
        /// <param name="database">The tenant database whose tags it reads after a seed.</param>
        private sealed class RecordingSeeder(string database) : ITenantVersionTagSeeder
        {
            /// <summary>The seeder it forwards to, once the host built it.</summary>
            private TenantVersionTagSeeder? _inner;

            /// <summary>The calls, in order: <c>seed</c> and <c>bump</c>.</summary>
            public List<string> Calls { get; } = [];

            /// <summary>The tags right after the last seed that succeeded.</summary>
            public Dictionary<string, Guid>? Seeded { get; private set; }

            /// <summary>Whether a seed throws, after it is recorded, instead of seeding.</summary>
            public bool FailSeed { get; set; }

            /// <summary>Wraps the platform's seeder in the host the migrator builds.</summary>
            /// <param name="services">The host's services.</param>
            /// <returns>This seeder.</returns>
            public RecordingSeeder Over(IServiceProvider services)
            {
                _inner = ActivatorUtilities.CreateInstance<TenantVersionTagSeeder>(services);
                return this;
            }

            /// <inheritdoc />
            public async Task SeedAsync(int tenantId, CancellationToken cancellationToken)
            {
                Calls.Add("seed");
                if (FailSeed)
                {
                    throw new InvalidOperationException("The seed failed, as the test asked.");
                }

                await _inner!.SeedAsync(tenantId, cancellationToken);
                Seeded = await TagsAsync(database);
            }

            /// <inheritdoc />
            public Task BumpAllAsync(int tenantId, CancellationToken cancellationToken)
            {
                Calls.Add("bump");
                return _inner!.BumpAllAsync(tenantId, cancellationToken);
            }
        }

        /// <summary>A registry of fixed names, none of which a written table bumps.</summary>
        /// <param name="names">The names.</param>
        private sealed class NamesRegistry(IReadOnlyList<string> names) : IVersionTagRegistry
        {
            /// <inheritdoc />
            public IReadOnlyList<string> Names => names;

            /// <inheritdoc />
            public VersionTagEffects Resolve(IReadOnlySet<TableName> writtenTables)
            {
                return VersionTagEffects.None;
            }
        }
    }
}
