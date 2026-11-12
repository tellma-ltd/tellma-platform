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
using Tellma.Core.Migrator.TestDistribution;

namespace Tellma.Core.IntegrationTests.Migrator
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
    ///     Runs the test distribution's migrator in process against the suite's server: a catalog and
    ///     a database prefix unique to the test, configuration in memory, the report captured.
    /// </summary>
    internal sealed class MigratorHarness
    {
        /// <summary>The tenant ids whose databases the fixture drops at the end: the seed band's first and the sequence's first.</summary>
        private static readonly int[] TrackedTenants = [1, 2, 3, 5, 1000, 1001, 1002];

        private readonly SqlServerFixture _sql;

        /// <summary>Initializes a harness over a catalog that does not exist yet.</summary>
        /// <param name="sql">The suite's server.</param>
        /// <param name="prefix">The prefix identifying the test.</param>
        public MigratorHarness(SqlServerFixture sql, string prefix)
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
            credential.MaxPoolSize = 5;

            Values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Tellma:PublicOrigin"] = "https://localhost:4200",
                ["Tellma:Catalog:ConnectionString"] = CatalogConnectionString,
                ["Tellma:Sql:Profiles:default"] = credential.ConnectionString,
                ["Tellma:Sql:DatabasePrefix"] = DatabasePrefix,
                ["Tellma:DisplayName"] = "Probe",
            };
        }

        /// <summary>The catalog's name.</summary>
        public string CatalogName { get; }

        /// <summary>The catalog's connection string.</summary>
        public string CatalogConnectionString => _sql.ConnectionString(CatalogName);

        /// <summary>The prefix of the tenant databases' default names.</summary>
        public string DatabasePrefix { get; }

        /// <summary>The configuration every run reads; a test changes it between runs.</summary>
        public Dictionary<string, string?> Values { get; }

        /// <summary>A further composition every run adds to the test distribution's.</summary>
        public Action<TellmaBuilder>? Compose { get; set; }

        /// <summary>The environment every run is hosted in.</summary>
        public string EnvironmentName { get; set; } = "Development";

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

        /// <summary>Configures the seed tenants <c>migrate</c> registers.</summary>
        /// <param name="ids">The seed tenants' ids.</param>
        public void Seed(params int[] ids)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                Values[string.Create(CultureInfo.InvariantCulture, $"Tellma:Seed:Tenants:{i}:Id")] = ids[i].ToString(CultureInfo.InvariantCulture);
                Values[string.Create(CultureInfo.InvariantCulture, $"Tellma:Seed:Tenants:{i}:Name")] = string.Create(CultureInfo.InvariantCulture, $"Seed {ids[i]}");
            }
        }

        /// <summary>Runs one command.</summary>
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
                TestDistribution.Slug,
                builder =>
                {
                    TestDistribution.Compose(builder);
                    extra?.Invoke(builder);
                },
                migrateIdentityStore: null,
                new MigratorRunSettings
                {
                    MigrationsAssembly = typeof(TestDistribution).Assembly,
                    Out = output,
                    Error = error,
                    EnvironmentName = EnvironmentName,
                    ContentRootPath = AppContext.BaseDirectory,
                    ConfigureConfiguration = configuration => configuration.AddInMemoryCollection(values),
                    ConfigureServices = static services => services.AddLogging(static logging => logging.ClearProviders()),
                },
                TestContext.Current.CancellationToken);

            return new MigratorResult(code, output.ToString().ReplaceLineEndings("\n"), error.ToString().ReplaceLineEndings("\n"));
        }
    }
}
