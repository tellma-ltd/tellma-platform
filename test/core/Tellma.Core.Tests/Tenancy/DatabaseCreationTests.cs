// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>The database-creation statement on SQL Server and on Azure SQL Database.</summary>
    public class DatabaseCreationTests
    {
        [Theory]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(8)]
        public void Every_engine_but_Azure_SQL_Database_gets_the_collation_alone(int engineEdition)
        {
            Assert.Equal(
                "CREATE DATABASE [Tellma.dev.acme.catalog] COLLATE Latin1_General_100_CI_AS;",
                DatabaseCreation.CreateStatement("Tellma.dev.acme.catalog", engineEdition, "(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))"));
        }

        [Fact]
        public void Azure_SQL_Database_appends_the_options_and_then_the_catalog_collation()
        {
            Assert.Equal(
                "CREATE DATABASE [tellma-acme-catalog] COLLATE Latin1_General_100_CI_AS\n"
                    + "    (SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))\n"
                    + "    WITH CATALOG_COLLATION = DATABASE_DEFAULT;",
                DatabaseCreation.CreateStatement("tellma-acme-catalog", DatabaseCreation.AzureSqlDatabaseEngineEdition, "(SERVICE_OBJECTIVE = ELASTIC_POOL(name = pool1))"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Azure_SQL_Database_without_options_pins_the_catalog_collation_alone(string? options)
        {
            Assert.Equal(
                "CREATE DATABASE [tellma-acme.1000] COLLATE Latin1_General_100_CI_AS\n    WITH CATALOG_COLLATION = DATABASE_DEFAULT;",
                DatabaseCreation.CreateStatement("tellma-acme.1000", DatabaseCreation.AzureSqlDatabaseEngineEdition, options));
        }

        [Theory]
        [InlineData("x]; DROP DATABASE [y")]
        [InlineData("x;y")]
        [InlineData("")]
        [InlineData("a\tb")]
        public void A_malformed_name_is_refused(string name)
        {
            _ = Assert.Throws<ArgumentException>(() => DatabaseCreation.CreateStatement(name, 2, null));
        }

        [Theory]
        [InlineData("(SERVICE_OBJECTIVE = 'S0'); DROP DATABASE [x]")]
        [InlineData("(SERVICE_OBJECTIVE = 'S0') -- comment")]
        [InlineData("(SERVICE_OBJECTIVE = 'S0') /* comment */")]
        public void Options_that_could_end_the_statement_are_refused(string options)
        {
            _ = Assert.Throws<ArgumentException>(() => DatabaseCreation.CreateStatement("tellma-acme.1", DatabaseCreation.AzureSqlDatabaseEngineEdition, options));
        }
    }
}
