// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Globalization;
using Tellma.Core.Composition;
using Tellma.Core.Migrator;
using Tellma.Distro.Acme.Migrator;
using Tellma.Identity.Migrations;

[assembly: AssemblyFixture(typeof(SqlServerFixture))]

namespace Tellma.Distro.Acme.IntegrationTests.Infrastructure
{
    /// <summary>What one migrator run printed and returned.</summary>
    /// <param name="ExitCode">The exit code.</param>
    /// <param name="Output">The report.</param>
    /// <param name="Error">Usage errors and composition problems.</param>
    internal sealed record MigratorResult(int ExitCode, string Output, string Error)
    {
        /// <summary>The report's lines.</summary>
        public IReadOnlyList<string> Lines => Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        /// <inheritdoc />
        public override string ToString()
        {
            return $"exit {ExitCode}\n{Output}\n{Error}";
        }
    }

    /// <summary>
    ///     Acme's deployment for one test: a catalog and a tenant database prefix of its own on the
    ///     suite's server, over Acme's own configuration files with only the catalog, the credential
    ///     and the prefix replaced. The migrator runs in process exactly as its <c>Program.cs</c> runs
    ///     it, with Acme's slug, composition and identity store.
    /// </summary>
    internal sealed class AcmeMigrator
    {
        /// <summary>The public origin every host of the deployment serves.</summary>
        public const string PublicOrigin = "https://localhost:4200";

        /// <summary>The tenant ids whose databases the fixture drops at the end: the two seeds and the sequence's first.</summary>
        private static readonly int[] TrackedTenants = [1, 2, 1000, 1001];

        private readonly SqlServerFixture _sql;

        /// <summary>Initializes a deployment whose catalog does not exist yet.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="prefix">The prefix identifying the test.</param>
        public AcmeMigrator(SqlServerFixture sql, string prefix)
        {
            _sql = sql;
            CatalogName = sql.NewDatabaseName(prefix);
            DatabasePrefix = sql.NewDatabaseName(prefix + "_t");
            foreach (int tenant in TrackedTenants)
            {
                sql.Track(TenantDatabaseName(tenant));
            }

            // The server's own credential is the profile: a catalog row names only server and database.
            SqlConnectionStringBuilder credential = new(sql.MasterConnectionString);
            credential.Remove("Data Source");
            credential.Remove("Initial Catalog");
            credential.MaxPoolSize = 10;

            Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = PublicOrigin,
                ["Tellma:Catalog:ConnectionString"] = CatalogConnectionString,
                ["Tellma:Sql:Profiles:default"] = credential.ConnectionString,
                ["Tellma:Sql:DatabasePrefix"] = DatabasePrefix,
            };
        }

        /// <summary>The catalog's name.</summary>
        public string CatalogName { get; }

        /// <summary>The catalog's connection string.</summary>
        public string CatalogConnectionString => _sql.ConnectionString(CatalogName);

        /// <summary>The prefix of the tenant databases' default names.</summary>
        public string DatabasePrefix { get; }

        /// <summary>The settings over Acme's configuration files that every run and every host reads; a test changes them between runs.</summary>
        public Dictionary<string, string?> Values { get; }

        /// <summary>A further composition every run adds to Acme's.</summary>
        public Action<TellmaBuilder>? Compose { get; set; }

        /// <summary>A tenant database's default name.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The name.</returns>
        public string TenantDatabaseName(int tenantId)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{DatabasePrefix}.{tenantId}");
        }

        /// <summary>A tenant database's connection string.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The connection string.</returns>
        public string TenantConnectionString(int tenantId)
        {
            return _sql.ConnectionString(TenantDatabaseName(tenantId));
        }

        /// <summary>Runs one command, in Development as a developer's checkout runs it.</summary>
        /// <param name="args">The command line.</param>
        /// <returns>What it printed and returned.</returns>
        public async Task<MigratorResult> RunAsync(params string[] args)
        {
            using StringWriter output = new(CultureInfo.InvariantCulture);
            using StringWriter error = new(CultureInfo.InvariantCulture);
            Dictionary<string, string?> values = new(Values, StringComparer.OrdinalIgnoreCase);
            Action<TellmaBuilder>? extra = Compose;

            int code = await TellmaMigrator.RunAsync(
                args,
                AcmeComposition.Slug,
                builder =>
                {
                    AcmeComposition.Compose(builder);
                    extra?.Invoke(builder);
                },
                TellmaIdentityStore.MigrateAsync,
                new MigratorRunSettings
                {
                    MigrationsAssembly = typeof(AcmeDesignTimeFactory).Assembly,
                    Out = output,
                    Error = error,
                    EnvironmentName = "Development",

                    // Acme's appsettings.json and appsettings.Development.json, which the migrator
                    // project links and copies beside the suite.
                    ContentRootPath = AppContext.BaseDirectory,
                    ConfigureConfiguration = configuration => configuration.AddInMemoryCollection(values),
                    ConfigureServices = static services => services.AddLogging(static logging => logging.ClearProviders()),
                },
                TestContext.Current.CancellationToken);

            return new MigratorResult(code, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
        }

        /// <summary>Runs <c>migrate</c> and asserts that it converged.</summary>
        /// <returns>What it printed.</returns>
        public async Task<MigratorResult> MigrateAsync()
        {
            MigratorResult result = await RunAsync("migrate");
            Assert.True(result.ExitCode == MigratorExitCodes.Converged, result.ToString());
            return result;
        }

        /// <summary>A tenant's state, as the catalog holds it.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>The state's name, or null for no row.</returns>
        public Task<string?> StateAsync(int tenantId)
        {
            return SqlServerFixture.ScalarAsync<string>(
                CatalogConnectionString, string.Create(CultureInfo.InvariantCulture, $"SELECT [State] FROM [catalog].[Tenants] WHERE [Id] = {tenantId};"));
        }
    }
}
