// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Hosting;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.IntegrationTests.Infrastructure;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Catalog
{
    /// <summary>
    ///     The registry over a real catalog: what a second instance sees after one instance writes,
    ///     a reload only when the version moved, the startup verdicts on a behind or missing catalog,
    ///     and connections to the tenant the snapshot names, never another.
    /// </summary>
    [Trait("Category", "Integration")]
    public class RegistryCatalogTests(SqlServerFixture sql)
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_second_instance_converges_on_its_next_refresh_and_an_unchanged_catalog_is_not_reloaded()
        {
            await using CatalogHost writer = await CatalogHost.CreateAsync(sql, "registry_converge");
            await using CatalogHost reader = await CatalogHost.ComposeAsync(sql, writer.ConnectionString);
            Guid loaded = reader.Registry.SnapshotVersion;

            TenantDescriptor tenant = await writer.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token);
            Assert.Null(reader.Registry.Find(tenant.Id));

            await reader.Registry.RefreshAsync(force: false, Token);
            Assert.NotNull(reader.Registry.Find(tenant.Id));
            Assert.NotEqual(loaded, reader.Registry.SnapshotVersion);

            // An unchanged catalog: the same snapshot object survives the probe.
            TenantInfo before = reader.Registry.Get(tenant.Id);
            await reader.Registry.RefreshAsync(force: false, Token);
            Assert.Same(before, reader.Registry.Get(tenant.Id));
        }

        [Fact]
        public async Task A_connection_opened_for_a_tenant_reaches_that_tenants_database_alone()
        {
            await using CatalogHost host = await CatalogHost.CreateAsync(sql, "registry_connect");
            string first = sql.NewDatabaseName("tenant_a");
            string second = sql.NewDatabaseName("tenant_b");
            await SqlServerFixture.ExecuteAsync(
                sql.MasterConnectionString,
                $"CREATE DATABASE [{first}] COLLATE {Abstractions.Hosting.DatabaseCollations.Platform};",
                $"CREATE DATABASE [{second}] COLLATE {Abstractions.Hosting.DatabaseCollations.Platform};");

            TenantDescriptor a = await host.Catalog.RegisterAsync(new TenantRegistration("A", TenantCategory.Live, null, new TenantLocation(sql.Server, first, "default")), "migrator", cancellationToken: Token);
            TenantDescriptor b = await host.Catalog.RegisterAsync(new TenantRegistration("B", TenantCategory.Live, null, new TenantLocation(sql.Server, second, "default")), "migrator", cancellationToken: Token);

            Assert.Equal(first, await DatabaseOfAsync(host, a.Id));
            Assert.Equal(second, await DatabaseOfAsync(host, b.Id));
            Assert.Equal("acme-development", new SqlConnectionStringBuilder(host.Connections.GetConnectionString(a.Id)).ApplicationName);
            _ = await Assert.ThrowsAsync<TenantNotFoundException>(() => host.Connections.OpenAsync(4242, Token));

            await using SqlConnection catalog = await host.Connections.OpenCatalogAsync(Token);
            Assert.Equal(new SqlConnectionStringBuilder(host.ConnectionString).InitialCatalog, catalog.Database);
        }

        [Fact]
        public async Task A_web_host_refuses_to_start_over_a_catalog_behind_the_platform()
        {
            string catalog = await sql.CreateEmptyCatalogAsync("registry_behind");
            await CatalogHost.MigrateAsync(catalog);
            await SqlServerFixture.ExecuteAsync(catalog, "DELETE FROM [catalog].[__EFMigrationsHistory];");

            TellmaCompositionException failure = await Assert.ThrowsAsync<TellmaCompositionException>(
                () => CatalogHost.ComposeAsync(sql, catalog, environmentName: Environments.Production));

            Assert.Contains("-- migrate", Assert.Single(failure.Problems).Fix, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_missing_catalog_fails_startup_in_Development_and_starts_not_ready_elsewhere()
        {
            string absent = sql.ConnectionString(sql.NewDatabaseName("registry_absent"));

            _ = await Assert.ThrowsAsync<TellmaCompositionException>(() => CatalogHost.ComposeAsync(sql, absent));
            await using CatalogHost production = await CatalogHost.ComposeAsync(sql, absent, environmentName: Environments.Production);

            Assert.False(production.Registry.IsAvailable);
            Assert.Equal(TenantProblemCodes.CatalogUnavailable, Assert.Throws<TenantUnavailableException>(() => production.Registry.Find(1)).Code);
        }

        /// <summary>The database a connection opened for a tenant lands in.</summary>
        /// <param name="host">The host.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The database's name, as the server reports it.</returns>
        private static async Task<string> DatabaseOfAsync(CatalogHost host, int tenantId)
        {
            await using SqlConnection connection = await host.Connections.OpenAsync(tenantId, Token);
            await using SqlCommand command = connection.CreateCommand();
            command.CommandText = "SELECT DB_NAME();";
            return await command.ExecuteScalarAsync(Token) as string ?? throw new InvalidOperationException("The server named no database.");
        }
    }
}
