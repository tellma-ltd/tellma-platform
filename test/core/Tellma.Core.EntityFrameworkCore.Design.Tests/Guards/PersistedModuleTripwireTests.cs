// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using System.Reflection;
using System.Text.RegularExpressions;
using Tellma.Core.EntityFrameworkCore.MigrationsHost;
using Tellma.Core.EntityFrameworkCore.TableTypes;

namespace Tellma.Core.EntityFrameworkCore.Design.Tests.Guards
{
    /// <summary>
    ///     The fast static tripwire of spec 0001 Rule 5 (layer 3): reflect over the migrations assembly,
    ///     enumerate every <see cref="SqlOperation" /> across all migrations' <c>UpOperations</c>,
    ///     and flag any generated table-type name appearing inside a
    ///     <c>CREATE/ALTER PROCEDURE|FUNCTION</c> batch. (Layer 1 is the drop-time guard; layer 2
    ///     is the integration test asserting zero <c>sys.sql_expression_dependencies</c> rows.)
    /// </summary>
    public partial class PersistedModuleTripwireTests
    {
        [GeneratedRegex(
            @"(CREATE|ALTER)\s+(OR\s+ALTER\s+)?(PROCEDURE|PROC|FUNCTION)\b",
            RegexOptions.IgnoreCase | RegexOptions.Singleline)]
        private static partial Regex PersistedModuleRegex();

        [Fact]
        public void No_migration_creates_a_persisted_module_referencing_a_generated_type()
        {
            using MigrationsHostContext context = new MigrationsHostContextFactory().CreateDbContext([]);
            string[] typeNames = [.. context.GetService<IDesignTimeModel>().Model.GetTableTypes().Select(t => t.Name)];
            Assert.NotEmpty(typeNames);

            Assembly migrationsAssembly = typeof(MigrationsHostContext).Assembly;
            List<Type> migrationTypes = [.. migrationsAssembly.GetTypes()
                .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)];

            foreach (Type migrationType in migrationTypes)
            {
                var migration = (Migration)Activator.CreateInstance(migrationType)!;
                migration.ActiveProvider = "Microsoft.EntityFrameworkCore.SqlServer";

                foreach (SqlOperation sqlOperation in migration.UpOperations.OfType<SqlOperation>())
                {
                    if (!PersistedModuleRegex().IsMatch(sqlOperation.Sql))
                    {
                        continue;
                    }

                    foreach (string typeName in typeNames)
                    {
                        Assert.False(
                            sqlOperation.Sql.Contains(typeName, StringComparison.OrdinalIgnoreCase),
                            $"Migration '{migrationType.Name}' contains a CREATE/ALTER PROCEDURE|FUNCTION batch that "
                                + $"references generated table type '{typeName}'. Per the Tellma architecture, no "
                                + "persisted SQL module may reference a generated table type; use dynamic SQL.");
                    }
                }
            }
        }

        [Fact]
        public void Tripwire_detects_a_planted_offender()
        {
            // Self-test of the tripwire's matching logic.
            const string Offender = "CREATE OR ALTER PROCEDURE [dbo].[SaveInvoices] @rows [gl].[InvoicesList] READONLY AS BEGIN SELECT 1 END";

            Assert.Matches(PersistedModuleRegex(), Offender);
            Assert.Contains("InvoicesList", Offender, StringComparison.OrdinalIgnoreCase);
        }
    }
}
