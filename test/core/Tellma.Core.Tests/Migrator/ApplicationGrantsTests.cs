// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Migrator.Sql;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Migrator
{
    /// <summary>
    ///     The statements that keep the application role's grants equal to the model: every schema
    ///     but <c>dbo</c>, every sequence, read-only access to the fingerprint table, no write to the
    ///     catalog's history, and the application principal's user.
    /// </summary>
    public class ApplicationGrantsTests
    {
        [Fact]
        public void A_tenant_grants_every_schema_but_dbo_every_sequence_and_select_alone_on_the_fingerprint_table()
        {
            IModel model = TenantModel();

            IReadOnlyList<string> statements = ApplicationGrants.ForTenant(model);

            // --- The role first, then one grant per schema, ordinal ---
            Assert.Equal(ApplicationGrants.EnsureRole, statements[0]);
            List<string> schemas = [.. model.GetRelationalModel().Tables.Select(static table => table.Schema ?? "dbo").Where(static schema => schema != "dbo").Distinct().Order(StringComparer.Ordinal)];
            Assert.NotEmpty(schemas);
            Assert.Equal(
                schemas.Select(static schema => $"GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[{schema}] TO [tellma_app];"),
                statements.Where(static statement => statement.Contains("ON SCHEMA::", StringComparison.Ordinal)));
            Assert.DoesNotContain(statements, static statement => statement.Contains("SCHEMA::[dbo]", StringComparison.Ordinal));

            // --- Every sequence's next value ---
            Assert.NotEmpty(model.GetSequences());
            foreach (ISequence sequence in model.GetSequences())
            {
                Assert.Contains($"GRANT UPDATE ON OBJECT::[{sequence.Schema}].[{sequence.Name}] TO [tellma_app];", statements);
            }

            // --- The fingerprint table, read-only, and nothing else in dbo ---
            Assert.Equal("GRANT SELECT ON OBJECT::[dbo].[__TellmaSchema] TO [tellma_app];", statements[^1]);
            Assert.Single(statements, static statement => statement.Contains("[dbo]", StringComparison.Ordinal));
        }

        [Fact]
        public void The_catalog_denies_writes_to_its_history_and_adds_the_identity_store_in_process()
        {
            using var context = CatalogDbContext.Create("Data Source=sql01;Initial Catalog=catalog;Integrated Security=True");

            IReadOnlyList<string> standalone = ApplicationGrants.ForCatalog(context.Model, inProcIdentity: false);
            IReadOnlyList<string> inProc = ApplicationGrants.ForCatalog(context.Model, inProcIdentity: true);

            Assert.Equal(
            [
                ApplicationGrants.EnsureRole,
                "GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[catalog] TO [tellma_app];",
                "GRANT UPDATE ON OBJECT::[catalog].[sq_Tenants] TO [tellma_app];",
                "DENY INSERT, UPDATE, DELETE ON OBJECT::[catalog].[__EFMigrationsHistory] TO [tellma_app];",
            ], standalone);
            Assert.Equal(
            [
                ApplicationGrants.EnsureRole,
                "GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[catalog] TO [tellma_app];",
                "GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::[idsvr] TO [tellma_app];",
                "GRANT UPDATE ON OBJECT::[catalog].[sq_Tenants] TO [tellma_app];",
                "DENY INSERT, UPDATE, DELETE ON OBJECT::[catalog].[__EFMigrationsHistory] TO [tellma_app];",
                "GRANT SELECT ON OBJECT::[dbo].[__EFMigrationsHistory] TO [tellma_app];",
            ], inProc);
        }

        [Fact]
        public void The_role_is_created_only_when_missing()
        {
            Assert.Equal("IF DATABASE_PRINCIPAL_ID(N'tellma_app') IS NULL CREATE ROLE [tellma_app];", ApplicationGrants.EnsureRole);
        }

        [Fact]
        public void The_principal_is_an_external_user_on_Azure_SQL_Database_and_a_login_user_elsewhere()
        {
            Assert.Equal(
            [
                "IF DATABASE_PRINCIPAL_ID(N'acme-web') IS NULL CREATE USER [acme-web] FROM EXTERNAL PROVIDER;",
                "ALTER ROLE [tellma_app] ADD MEMBER [acme-web];",
            ], ApplicationGrants.ForPrincipal("acme-web", engineEdition: 5));
            Assert.Equal(
            [
                "IF DATABASE_PRINCIPAL_ID(N'acme_web') IS NULL CREATE USER [acme_web] FOR LOGIN [acme_web];",
                "ALTER ROLE [tellma_app] ADD MEMBER [acme_web];",
            ], ApplicationGrants.ForPrincipal("acme_web", engineEdition: 3));
        }

        [Fact]
        public void A_principal_is_quoted_as_an_identifier_and_as_a_literal()
        {
            IReadOnlyList<string> statements = ApplicationGrants.ForPrincipal("o'brien]x", engineEdition: 2);

            Assert.Equal("IF DATABASE_PRINCIPAL_ID(N'o''brien]x') IS NULL CREATE USER [o'brien]]x] FOR LOGIN [o'brien]]x];", statements[0]);
        }

        [Fact]
        public void The_role_itself_is_refused_as_the_principal()
        {
            _ = Assert.Throws<ArgumentException>(() => ApplicationGrants.ForPrincipal("TELLMA_APP", engineEdition: 2));
        }

        /// <summary>The tenant model of a composition of the core feature alone.</summary>
        /// <returns>The model.</returns>
        private static IModel TenantModel()
        {
            using ServiceProvider provider = TestComposition.Compose(static _ => { }).BuildServiceProvider();
            return provider.GetRequiredService<IModel>();
        }
    }
}
