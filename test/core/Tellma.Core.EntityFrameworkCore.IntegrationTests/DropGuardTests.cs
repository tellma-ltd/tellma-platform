// Copyright (c) 2026 Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Tellma.Core.EntityFrameworkCore.IntegrationTests.Infrastructure;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.EntityFrameworkCore.TableTypes.Operations;

namespace Tellma.Core.EntityFrameworkCore.IntegrationTests
{
    /// <summary>
    ///     The drop-time dependency guard (spec 0001 Rule 5 layer 1): dropping a type referenced by a
    ///     persisted module fails with error <see cref="TableTypeErrorNumbers.DroppedTypeHasDependents" />
    ///     naming the module; with no dependents the drop (and a recreate) succeeds.
    /// </summary>
    /// <param name="fixture">The shared SQL Server.</param>
    [Trait("Category", "Integration")]
    public class DropGuardTests(SqlServerFixture fixture)
    {
        /// <summary>Generates the SQL of one operation through the context's SQL generator.</summary>
        private static string GenerateSql(DbContext context, Microsoft.EntityFrameworkCore.Migrations.Operations.MigrationOperation operation)
        {
            IMigrationsSqlGenerator generator = context.GetService<IMigrationsSqlGenerator>();
            return Assert.Single(generator.Generate([operation])).CommandText;
        }

        [Fact]
        public async Task Dropping_a_type_referenced_by_a_persisted_module_fails_naming_the_module()
        {
            string connectionString = await fixture.CreateDatabaseAsync("guard");
            await using MigrationsHostContext context = IntegrationHelpers.CreateMigrationsHostContext(connectionString);
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);

            // Plant a persisted module that references a generated type (forbidden by the
            // architecture — exactly what the guard exists to catch).
            await IntegrationHelpers.ExecuteAsync(
                connectionString,
                "CREATE PROCEDURE [dbo].[UsesInvoicesList] @rows [gl].[InvoicesList] READONLY AS SELECT COUNT(*) FROM @rows");

            string dropSql = GenerateSql(context, new DropTableTypeOperation { Name = "InvoicesList", Schema = "gl" });

            SqlException exception = await Assert.ThrowsAsync<SqlException>(
                () => IntegrationHelpers.ExecuteAsync(connectionString, dropSql));

            Assert.Equal(TableTypeErrorNumbers.DroppedTypeHasDependents, exception.Number);
            Assert.Contains("[dbo].[UsesInvoicesList]", exception.Message, StringComparison.Ordinal);

            // The type survived the failed drop.
            int count = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT COUNT(*) FROM [sys].[table_types] WHERE [name] = N'InvoicesList'");
            Assert.Equal(1, count);

            // Remove the offender; the same drop now succeeds, and the type can be recreated
            // (drop + create is how every definitional change deploys).
            await IntegrationHelpers.ExecuteAsync(connectionString, "DROP PROCEDURE [dbo].[UsesInvoicesList]", dropSql);

            CreateTableTypeOperation recreate = new()
            {
                Name = "InvoicesList",
                Schema = "gl",
                PrimaryKey = ["Id"],
                Grants = ["public"],
            };
            recreate.Columns.Add(new TableTypeColumnDefinition { Name = "Id", StoreType = "int" });
            await IntegrationHelpers.ExecuteAsync(connectionString, GenerateSql(context, recreate));

            count = await IntegrationHelpers.ScalarAsync<int>(
                connectionString, "SELECT COUNT(*) FROM [sys].[table_types] WHERE [name] = N'InvoicesList'");
            Assert.Equal(1, count);
        }
    }
}
