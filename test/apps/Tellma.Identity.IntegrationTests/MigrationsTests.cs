// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests
{
    /// <summary>
    ///     The committed migration chain produces the full identity schema on a real SQL Server:
    ///     Identity tables including passkeys (schema version 3), OpenIddict's four tables, the
    ///     engine's own tables, and the prune-supporting filtered index.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class MigrationsTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Initial_migration_creates_the_full_schema()
        {
            string connectionString = await fixture.CreateDatabaseAsync("idmig");

            // Mirror the runtime model configuration (schema version 3) exactly.
            ServiceCollection services = new();
            services.Configure<IdentityOptions>(TellmaIdentityModelDefaults.ConfigureStoreOptions);
            await using ServiceProvider provider = services.BuildServiceProvider();

            DbContextOptionsBuilder<TellmaIdentityDbContext> builder = new();
            builder.UseSqlServer(connectionString, static sql => sql.MigrationsAssembly(TellmaIdentityConstants.MigrationsAssemblyName));
            builder.UseApplicationServiceProvider(provider);

            await using (TellmaIdentityDbContext context = new(builder.Options))
            {
                await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            }

            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);

            // Every table lives in the dedicated schema.
            HashSet<string> tables = [];
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'idsvr'";
                await using SqlDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);
                while (await reader.ReadAsync(TestContext.Current.CancellationToken))
                {
                    tables.Add(reader.GetString(0));
                }
            }

            foreach (string expected in (string[])
                ["AspNetUsers", "AspNetUserPasskeys", "AspNetUserLogins", "AspNetUserTokens",
                 "OpenIddictApplications", "OpenIddictAuthorizations", "OpenIddictScopes", "OpenIddictTokens",
                 "Sessions", "SessionClients", "SingleUseCodes", "TemporaryAccessPasses", "AuditEvents", "SsoTickets"])
            {
                Assert.Contains(expected, tables);
            }

            // The prune-supporting filtered index exists on the tokens table.
            await using (SqlCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT i.has_filter FROM sys.indexes i "
                    + "JOIN sys.tables t ON i.object_id = t.object_id "
                    + "JOIN sys.schemas s ON t.schema_id = s.schema_id "
                    + "WHERE s.name = 'idsvr' AND t.name = 'OpenIddictTokens' "
                    + "AND i.name = 'IX_OpenIddictTokens_Status_ExpirationDate'";
                object? hasFilter = await command.ExecuteScalarAsync(TestContext.Current.CancellationToken);
                Assert.NotNull(hasFilter);
                Assert.True((bool)hasFilter, "The prune index must be filtered.");
            }
        }
    }
}
