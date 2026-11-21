// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Migrator;
using Tellma.Core.Tenancy;
using Tellma.Core.Testing.Database;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     The catalog model and its platform-owned migrations: the pinned column set, the collation
    ///     pin, the latest-migration constant, and the membership table type.
    /// </summary>
    public class CatalogModelTests
    {
        [Fact]
        public void The_catalog_migrations_hold_the_collation_pin()
        {
            using CatalogDbContext context = new CatalogDesignTimeFactory().CreateDbContext([]);

            CollationPin.Verify(context);
        }

        [Fact]
        public void The_published_latest_migration_is_the_last_one_the_migrator_carries()
        {
            using CatalogDbContext context = new CatalogDesignTimeFactory().CreateDbContext([]);

            LatestMigrationPin.Verify(context, CatalogMigrations.Latest);
        }

        [Fact]
        public void The_tenants_table_holds_exactly_these_columns_and_no_secret()
        {
            // No password, key, token or connection-string column may ever be added: a change here
            // is a change to the secrets policy.
            ITable tenants = Table("Tenants");

            string[] expected =
            [
                "Id", "Name", "Name2", "Name3", "Language1", "Language2", "Language3", "Category", "LiveTenantId", "State",
                "StateReason", "StateChangedAt", "StateChangedBy", "Server", "Database", "CredentialProfile", "Properties",
                "Version", "CreatedAt", "ModifiedAt",
            ];
            Assert.Equal(expected.Order(StringComparer.Ordinal), tenants.Columns.Select(static column => column.Name).Order(StringComparer.Ordinal));
            Assert.Equal("nvarchar(16)", tenants.FindColumn("State")!.StoreType);
            Assert.Equal("varchar(35)", tenants.FindColumn("Language1")!.StoreType);
            Assert.Equal("datetimeoffset(3)", tenants.FindColumn("StateChangedAt")!.StoreType);
            Assert.Equal("uniqueidentifier", tenants.FindColumn("Version")!.StoreType);
        }

        [Fact]
        public void Every_catalog_table_lives_in_schema_catalog_with_named_keys_and_no_identity()
        {
            using var context = CatalogDbContext.Create(connectionString: null);
            IRelationalModel model = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();

            Assert.Equal(["CatalogState", "Sessions", "TenantMemberships", "Tenants"], model.Tables.Select(static table => table.Name).Order(StringComparer.Ordinal));
            Assert.All(model.Tables, static table =>
            {
                Assert.Equal(CatalogDbContext.Schema, table.Schema);
                Assert.Equal($"PK_{table.Name}", table.PrimaryKey!.Name);
                Assert.All(table.Columns, static column => Assert.Equal(
                    SqlServerValueGenerationStrategy.None,
                    column.PropertyMappings[0].Property.GetValueGenerationStrategy()));
            });

            ISequence sequence = Assert.Single(model.Model.GetSequences());
            Assert.Equal((CatalogDbContext.Schema, CatalogDbContext.TenantSequence, 1000L), (sequence.Schema, sequence.Name, sequence.StartValue));
        }

        [Fact]
        public void Identifiers_compare_byte_for_byte()
        {
            Assert.Equal(DatabaseCollations.Binary, Table("TenantMemberships").FindColumn("Subject")!.Collation);
            Assert.Equal(DatabaseCollations.Binary, Table("Sessions").FindColumn("Key")!.Collation);
            Assert.Equal(DatabaseCollations.Binary, Table("Sessions").FindColumn("Handle")!.Collation);
            Assert.Equal(DatabaseCollations.Binary, Table("Sessions").FindColumn("Subject")!.Collation);
            Assert.Null(Table("Tenants").FindColumn("Name")!.Collation);
        }

        [Fact]
        public void The_membership_table_type_is_the_catalogs_own_granted_to_the_application_role()
        {
            using var context = CatalogDbContext.Create(connectionString: null);

            TableTypeDefinition type = Assert.Single(context.Model.GetTableTypes());

            Assert.Equal(("catalog", "TenantMembershipList"), (type.Schema, type.Name));
            Assert.StartsWith("TenantMembershipList_", type.PhysicalName, StringComparison.Ordinal);
            Assert.Equal(["TenantId", "Subject", "IsActive"], type.Columns.Select(static column => column.Name));
            TableTypeColumnDefinition subject = type.Columns[1];
            Assert.Equal(("varchar(255)", DatabaseCollations.Binary), (subject.StoreType, subject.Collation));
            Assert.Equal([DatabaseRoles.Application], type.Grants);
            Assert.Equal(TableTypeSweepScopes.Catalog, context.GetService<IDbContextOptions>().FindExtension<TableTypesOptionsExtension>()!.SweepScope);
        }

        [Fact]
        public void The_migrations_live_in_the_migrator_with_their_history_in_schema_catalog()
        {
            using var context = CatalogDbContext.Create("Data Source=(local);Initial Catalog=x;Integrated Security=true");

            string script = context.Database.GenerateCreateScript();

            Assert.Equal(CatalogDbContext.MigrationsAssembly, typeof(CatalogDesignTimeFactory).Assembly.GetName().Name);
            Assert.Contains(context.Database.GetMigrations(), static id => id == CatalogMigrations.Latest);
            Assert.Contains("CREATE SEQUENCE [catalog].[sq_Tenants] AS int START WITH 1000", script, StringComparison.Ordinal);
            Assert.Contains("CONSTRAINT [DF_Tenants_CredentialProfile] DEFAULT N'default'", script, StringComparison.Ordinal);
        }

        /// <summary>A table of the catalog's design-time relational model.</summary>
        /// <param name="name">The table's name.</param>
        /// <returns>The table.</returns>
        private static ITable Table(string name)
        {
            using var context = CatalogDbContext.Create(connectionString: null);
            return context.GetService<IDesignTimeModel>().Model.GetRelationalModel().FindTable(name, CatalogDbContext.Schema)!;
        }
    }
}
