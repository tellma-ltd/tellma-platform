// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
#pragma warning disable EF1001 // The compatibility level is read back from the provider's own options extension.
using Microsoft.EntityFrameworkCore.SqlServer.Infrastructure.Internal;
#pragma warning restore EF1001
using Tellma.Core.Abstractions.Hosting;
using Tellma.Core.Data;
using Tellma.Core.EntityFrameworkCore.TableTypes;
using Tellma.Core.Tests.Data.Model;

namespace Tellma.Core.Tests.Data
{
    /// <summary>The one place provider options are applied: the provider, its pins, the table types and the conventions.</summary>
    public class UseTellmaSqlServerTests
    {
        [Fact]
        public void The_connection_string_carries_the_deployment_id_as_its_application_name()
        {
            DbContextOptions options = Options("Server=(local);Database=Acme;Application Name=other;Integrated Security=true", new DeploymentIdentity("acme", "Staging"));

            SqlConnectionStringBuilder connection = new(RelationalOptionsExtension.Extract(options).ConnectionString);
            Assert.Equal("acme-staging", connection.ApplicationName);
            Assert.Equal("Acme", connection.InitialCatalog);
        }

        [Fact]
        public void A_value_with_a_separator_cannot_inject_a_keyword()
        {
            string composed = TellmaSqlServerDbContextOptionsExtensions.WithApplicationName(
                "Server=(local);Database='x;Pooling=false'", new DeploymentIdentity("acme", "Production"));

            SqlConnectionStringBuilder connection = new(composed);
            Assert.Equal(("x;Pooling=false", "acme", true), (connection.InitialCatalog, connection.ApplicationName, connection.Pooling));
        }

        [Fact]
        public void A_model_only_context_has_no_connection_string()
        {
            DbContextOptions options = Options(null, TestModels.Deployment);

            Assert.Null(RelationalOptionsExtension.Extract(options).ConnectionString);
        }

        [Fact]
        public void The_compatibility_level_is_pinned_to_sql_server_2022()
        {
            DbContextOptions options = Options(null, TestModels.Deployment);

#pragma warning disable EF1001 // The provider exposes the pinned level only on its internal options extension.
            Assert.Equal(160, options.FindExtension<SqlServerOptionsExtension>()!.SqlServerCompatibilityLevel);
#pragma warning restore EF1001
        }

        [Fact]
        public void The_table_types_and_the_conventions_are_installed_with_the_tenant_scope_by_default()
        {
            DbContextOptions tenant = Options(null, TestModels.Deployment);
            DbContextOptionsBuilder fixture = new();
            _ = fixture.UseTellmaSqlServer(null, TestModels.Deployment, TableTypeSweepScopes.Fixture);

            Assert.Equal("tellma.tenant", tenant.FindExtension<TableTypesOptionsExtension>()!.SweepScope);
            Assert.Equal("tellma.fixture", fixture.Options.FindExtension<TableTypesOptionsExtension>()!.SweepScope);
            Assert.NotNull(tenant.FindExtension<TellmaSqlServerOptionsExtension>());
        }

        [Fact]
        public void The_hierarchyid_mapping_is_installed()
        {
            using TellmaDbContext context = new TellmaModelFactory(TestModels.Definition([typeof(TestUser), typeof(Node)]), TestModels.Deployment).CreateContext(null);

            Assert.Equal("hierarchyid", context.Model.FindEntityType(typeof(Node))!.FindProperty("Node")!.GetColumnType());
            Assert.Contains(context.GetService<IDbContextOptions>().Extensions, static extension => extension.GetType().Name.Contains("HierarchyId", StringComparison.Ordinal));
        }

        [Fact]
        public void The_sweep_scopes_are_distinct_and_stable()
        {
            Assert.Equal(
                ["tellma.catalog", "tellma.fixture", "tellma.tenant"],
                new[] { TableTypeSweepScopes.Tenant, TableTypeSweepScopes.Catalog, TableTypeSweepScopes.Fixture }.Order(StringComparer.Ordinal));
        }

        private static DbContextOptions Options(string? connectionString, DeploymentIdentity deployment)
        {
            DbContextOptionsBuilder options = new();
            _ = options.UseTellmaSqlServer(connectionString, deployment);
            return options.Options;
        }
    }
}
