// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Testing;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Tenancy;

namespace Tellma.Core.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     A migrated catalog on the suite's server and the tenancy runtime composed over it, as a
    ///     distribution composes it: the registry, the connection factory, the write side, the
    ///     membership directory and the tenant scope factory, without a host's hosted services.
    /// </summary>
    internal sealed class CatalogHost : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;

        /// <summary>Creates the host over a catalog.</summary>
        /// <param name="connectionString">The catalog's connection string.</param>
        /// <param name="provider">The composed services.</param>
        private CatalogHost(string connectionString, ServiceProvider provider)
        {
            ConnectionString = connectionString;
            _provider = provider;
        }

        /// <summary>The catalog's connection string.</summary>
        public string ConnectionString { get; }

        /// <summary>The composed services.</summary>
        public IServiceProvider Services => _provider;

        /// <summary>The registry.</summary>
        public TenantRegistry Registry => _provider.GetRequiredService<TenantRegistry>();

        /// <summary>The write side.</summary>
        public ITenantCatalog Catalog => _provider.GetRequiredService<ITenantCatalog>();

        /// <summary>The membership directory.</summary>
        public TenantMembershipDirectory Directory => _provider.GetRequiredService<TenantMembershipDirectory>();

        /// <summary>The connection factory.</summary>
        public ITenantConnectionFactory Connections => _provider.GetRequiredService<ITenantConnectionFactory>();

        /// <summary>The tenant scope factory.</summary>
        public ITenantScopeFactory Scopes => _provider.GetRequiredService<ITenantScopeFactory>();

        /// <summary>Every log record of the runtime.</summary>
        public FakeLogCollector Logs => _provider.GetFakeLogCollector();

        /// <summary>Creates and migrates a catalog, and composes the runtime over it.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="prefix">The prefix identifying the test.</param>
        /// <param name="configure">Changes the configuration before composing.</param>
        /// <param name="environmentName">The environment; Development, where every server is allowed unless listed.</param>
        /// <param name="configureServices">Adds services after the composition, such as request-context initializers.</param>
        /// <returns>The host; the registry has loaded its first snapshot.</returns>
        public static async Task<CatalogHost> CreateAsync(
            SqlServerFixture sql,
            string prefix,
            Action<Dictionary<string, string?>>? configure = null,
            string environmentName = "Development",
            Action<IServiceCollection>? configureServices = null)
        {
            string catalog = await sql.CreateEmptyCatalogAsync(prefix);
            await MigrateAsync(catalog);
            return await ComposeAsync(sql, catalog, configure, environmentName, configureServices);
        }

        /// <summary>Composes the runtime over an existing catalog.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="catalog">The catalog's connection string.</param>
        /// <param name="configure">Changes the configuration before composing.</param>
        /// <param name="environmentName">The environment.</param>
        /// <param name="configureServices">Adds services after the composition, such as request-context initializers.</param>
        /// <returns>The host; the registry has loaded its first snapshot.</returns>
        public static async Task<CatalogHost> ComposeAsync(
            SqlServerFixture sql,
            string catalog,
            Action<Dictionary<string, string?>>? configure = null,
            string environmentName = "Development",
            Action<IServiceCollection>? configureServices = null)
        {
            // The server's own credential is the profile: a catalog row names only server and database.
            SqlConnectionStringBuilder credential = new(sql.MasterConnectionString);
            credential.Remove("Data Source");
            credential.Remove("Initial Catalog");
            credential.MaxPoolSize = 5;

            Dictionary<string, string?> values = new(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = "https://localhost:4200",
                ["Tellma:Catalog:ConnectionString"] = catalog,
                ["Tellma:Sql:Profiles:default"] = credential.ConnectionString,
                ["Tellma:Sql:DatabasePrefix"] = "Tellma.it",
            };
            configure?.Invoke(values);
            IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

            ServiceCollection services = new();
            _ = services.AddFakeLogging();
            _ = services.AddTellma("acme", configuration, new SuiteEnvironment(environmentName), static _ => { });

            // The first-load bound is pinned by the unit suite on a fake clock; here a server busy
            // with the suite's parallel CREATE DATABASEs must not turn a verdict into a timeout.
            _ = services.Configure<TenantRegistryStartupOptions>(static startup => startup.FirstLoadTimeout = TimeSpan.FromMinutes(1));
            configureServices?.Invoke(services);

            ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
            CatalogHost host = new(catalog, provider);
            await host.Registry.InitializeAsync(TestContext.Current.CancellationToken);
            return host;
        }

        /// <summary>Applies the catalog migrations to a database, as the migrator does.</summary>
        /// <param name="connectionString">The database's connection string.</param>
        /// <returns>A task that completes when the catalog is migrated.</returns>
        public static async Task MigrateAsync(string connectionString)
        {
            await using var context = CatalogDbContext.Create(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>The catalog's current version.</summary>
        /// <returns>The version.</returns>
        public Task<Guid> VersionAsync()
        {
            return SqlServerFixture.ScalarAsync<Guid>(ConnectionString, "SELECT [Version] FROM [catalog].[CatalogState] WHERE [Id] = 1;");
        }

        /// <summary>A tenant's stored state.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The stored state name.</returns>
        public Task<string?> StateAsync(int tenantId)
        {
            return SqlServerFixture.ScalarAsync<string>(ConnectionString, $"SELECT [State] FROM [catalog].[Tenants] WHERE [Id] = {tenantId};");
        }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _provider.DisposeAsync();
        }

        /// <summary>The host environment the suite composes under.</summary>
        /// <param name="environmentName">The environment name.</param>
        private sealed class SuiteEnvironment(string environmentName) : IHostEnvironment
        {
            /// <inheritdoc />
            public string EnvironmentName { get; set; } = environmentName;

            /// <inheritdoc />
            public string ApplicationName { get; set; } = "Tellma.Core.IntegrationTests";

            /// <inheritdoc />
            public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

            /// <inheritdoc />
            public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        }
    }
}
