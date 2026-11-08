// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.Options;
using Tellma.Identity.Services.Seeding;

namespace Tellma.Identity.Tests.Store
{
    /// <summary>
    ///     The store check's decisions that need no server: which collations pass, on SQL Server and
    ///     on Azure SQL Database, and what a failure says. The reads themselves are pinned by the
    ///     integration suite.
    /// </summary>
    public sealed class IdentityStoreCheckTests
    {
        private const int SqlServerEdition = 3;

        [Fact]
        public void A_database_in_the_platform_collation_passes_on_sql_server()
        {
            Assert.Null(IdentityStoreCheck.DescribeCollationFailure("ids", "Latin1_General_100_CI_AS", SqlServerEdition, null));
        }

        [Fact]
        public void A_database_in_the_server_default_collation_fails_naming_both_collations()
        {
            string? failure = IdentityStoreCheck.DescribeCollationFailure(
                "ids", "SQL_Latin1_General_CP1_CI_AS", SqlServerEdition, null);

            Assert.NotNull(failure);
            Assert.Contains("'ids'", failure, StringComparison.Ordinal);
            Assert.Contains("SQL_Latin1_General_CP1_CI_AS", failure, StringComparison.Ordinal);
            Assert.Contains("Latin1_General_100_CI_AS", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void A_collation_that_differs_only_in_a_suffix_fails()
        {
            // The supplementary-character variant sorts differently; only the exact name passes.
            Assert.NotNull(IdentityStoreCheck.DescribeCollationFailure(
                "ids", "Latin1_General_100_CI_AS_SC", SqlServerEdition, null));
        }

        [Fact]
        public void The_catalog_collation_is_ignored_off_azure()
        {
            Assert.Null(IdentityStoreCheck.DescribeCollationFailure(
                "ids", "Latin1_General_100_CI_AS", SqlServerEdition, "SQL_Latin1_General_CP1_CI_AS"));
        }

        [Fact]
        public void An_azure_database_with_the_database_default_catalog_collation_passes()
        {
            Assert.Null(IdentityStoreCheck.DescribeCollationFailure(
                "ids", "Latin1_General_100_CI_AS", IdentityStoreCheck.AzureSqlDatabaseEngineEdition, "DATABASE_DEFAULT"));
        }

        [Fact]
        public void An_azure_database_with_a_fixed_catalog_collation_fails_naming_both_values()
        {
            string? failure = IdentityStoreCheck.DescribeCollationFailure(
                "ids", "Latin1_General_100_CI_AS", IdentityStoreCheck.AzureSqlDatabaseEngineEdition, "SQL_Latin1_General_CP1_CI_AS");

            Assert.NotNull(failure);
            Assert.Contains("SQL_Latin1_General_CP1_CI_AS", failure, StringComparison.Ordinal);
            Assert.Contains("DATABASE_DEFAULT", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void The_database_collation_is_checked_before_the_catalog_collation_on_azure()
        {
            string? failure = IdentityStoreCheck.DescribeCollationFailure(
                "ids", "SQL_Latin1_General_CP1_CI_AS", IdentityStoreCheck.AzureSqlDatabaseEngineEdition, "DATABASE_DEFAULT");

            Assert.NotNull(failure);
            Assert.Contains("SQL_Latin1_General_CP1_CI_AS", failure, StringComparison.Ordinal);
        }

        [Fact]
        public void The_remedy_quotes_the_configured_migrate_command()
        {
            TellmaIdentitySeedOptions seed = new() { MigrateCommand = "dotnet run -- migrate" };

            Assert.Contains("dotnet run -- migrate", IdentityStoreCheck.Remedy(seed), StringComparison.Ordinal);
        }

        [Fact]
        public void The_remedy_without_a_command_names_the_migrations_assembly()
        {
            Assert.Contains(
                TellmaIdentityConstants.MigrationsAssemblyName,
                IdentityStoreCheck.Remedy(new TellmaIdentitySeedOptions()),
                StringComparison.Ordinal);
        }
    }
}
