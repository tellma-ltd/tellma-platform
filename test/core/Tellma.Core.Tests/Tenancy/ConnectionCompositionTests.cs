// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     How a tenant's connection string is composed: the profile's keywords, the row's server and
    ///     database set as values, the deployment's application name, and nothing a row holds able to
    ///     add a keyword.
    /// </summary>
    public class ConnectionCompositionTests
    {
        [Fact]
        public void The_row_sets_server_and_database_the_deployment_the_application_name_and_the_profile_everything_else()
        {
            // The profile's own server, database and application name are overridden; nothing else is.
            TellmaSqlOptions sql = Sql("Data Source=elsewhere;Initial Catalog=other;Application Name=spoof;Authentication=Active Directory Managed Identity;User Id=client-id;Encrypt=True;Max Pool Size=20;Min Pool Size=2;Connect Timeout=7");
            TenantRowComposer composer = new(sql, isDevelopment: false, "acme-staging");

            Assert.True(composer.TryCompose(FakeCatalogReader.Row(7, server: "tcp:sql01.database.windows.net,1433", database: "tellma-acme.7"), out TenantInfo tenant, out _));

            SqlConnectionStringBuilder composed = new(tenant.ConnectionString);
            Assert.Equal("tcp:sql01.database.windows.net,1433", composed.DataSource);
            Assert.Equal("tellma-acme.7", composed.InitialCatalog);
            Assert.Equal("acme-staging", composed.ApplicationName);
            Assert.Equal(SqlAuthenticationMethod.ActiveDirectoryManagedIdentity, composed.Authentication);
            Assert.Equal("client-id", composed.UserID);
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, composed.Encrypt);
            Assert.Equal(20, composed.MaxPoolSize);
            Assert.Equal(2, composed.MinPoolSize);
            Assert.Equal(7, composed.ConnectTimeout);
            Assert.Equal(new TenantLocation("tcp:sql01.database.windows.net,1433", "tellma-acme.7", "default"), tenant.Location);
        }

        [Fact]
        public void Each_row_takes_the_profile_it_names()
        {
            TellmaSqlOptions sql = Sql("Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20");
            sql.Profiles["warm"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=10;Min Pool Size=4";
            TenantRowComposer composer = new(sql, isDevelopment: false, "acme");

            Assert.True(composer.TryCompose(FakeCatalogReader.Row(1), out TenantInfo cold, out _));
            Assert.True(composer.TryCompose(FakeCatalogReader.Row(2, profile: "WARM"), out TenantInfo warm, out _));

            Assert.Equal(0, new SqlConnectionStringBuilder(cold.ConnectionString).MinPoolSize);
            Assert.Equal(4, new SqlConnectionStringBuilder(warm.ConnectionString).MinPoolSize);
            Assert.False(composer.IsWarm("default"));
            Assert.True(composer.IsWarm("warm"));
        }

        [Fact]
        public void A_value_holding_separators_is_set_as_a_value_and_never_adds_a_keyword()
        {
            // Defence in depth: the grammar refuses such rows before composition, and the builder
            // would quote the value even if one got through.
            SqlConnectionStringBuilder profile = new("Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20");

            string composed = TenantConnectionStrings.Compose(profile, "sql01.database.windows.net", "x;Password=stolen;Encrypt=False", "acme");

            SqlConnectionStringBuilder parsed = new(composed);
            Assert.Equal("x;Password=stolen;Encrypt=False", parsed.InitialCatalog);
            Assert.False(parsed.ShouldSerialize("Password"));
            Assert.Equal(SqlConnectionEncryptOption.Mandatory, parsed.Encrypt);
        }

        [Theory]
        [InlineData("sql01.database.windows.net;Password=x", "tenant-1")]
        [InlineData("sql01.database.windows.net=x", "tenant-1")]
        [InlineData("sql01.database.windows.net", "tenant;Encrypt=False")]
        [InlineData("sql01.database.windows.net", "tenant=1")]
        public void A_row_with_a_separator_in_its_location_is_never_composed(string server, string database)
        {
            TenantRowComposer composer = new(Sql("Encrypt=True;Max Pool Size=20"), isDevelopment: true, "acme");

            Assert.False(composer.TryCompose(FakeCatalogReader.Row(1, server: server, database: database), out _, out TenantRowRefusal refusal));
            Assert.True(refusal is TenantRowRefusal.ServerRefused or TenantRowRefusal.DatabaseMalformed);
        }

        [Theory]
        [InlineData("Authentication=Active Directory Managed Identity;Encrypt=False;Max Pool Size=20")]
        [InlineData("Authentication=Active Directory Managed Identity;Encrypt=True")]
        [InlineData("Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=21")]
        [InlineData("User Id=tellma_web;Password=x;Encrypt=True;Max Pool Size=20")]
        public void A_profile_that_breaks_the_startup_checks_rules_refuses_its_rows(string profile)
        {
            TenantRowComposer composer = new(Sql(profile), isDevelopment: false, "acme");

            Assert.False(composer.TryCompose(FakeCatalogReader.Row(1), out _, out TenantRowRefusal refusal));
            Assert.Equal(TenantRowRefusal.ProfileMalformed, refusal);
        }

        [Fact]
        public void A_profile_that_is_not_a_valid_fragment_refuses_its_rows()
        {
            TellmaSqlOptions sql = Sql("Encrypt=True;Max Pool Size=20");
            sql.Profiles["broken"] = "Not a connection string at all";
            TenantRowComposer composer = new(sql, isDevelopment: false, "acme");

            Assert.False(composer.TryCompose(FakeCatalogReader.Row(1, profile: "broken"), out _, out TenantRowRefusal refusal));
            Assert.Equal(TenantRowRefusal.ProfileMalformed, refusal);
        }

        [Fact]
        public void A_tenant_logs_everything_but_its_connection_string()
        {
            TellmaSqlOptions sql = Sql("User Id=tellma_web;Password=hunter2;Encrypt=True;Max Pool Size=20");
            sql.AllowSqlPassword = true;
            TenantRowComposer composer = new(sql, isDevelopment: false, "acme");
            Assert.True(composer.TryCompose(FakeCatalogReader.Row(1), out TenantInfo tenant, out _));

            string text = tenant.ToString();

            Assert.Contains("hunter2", tenant.ConnectionString, StringComparison.Ordinal);
            Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
            Assert.DoesNotContain("tellma_web", text, StringComparison.Ordinal);
            Assert.Contains("ConnectionString = [redacted]", text, StringComparison.Ordinal);
            Assert.Contains("tenant-1", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_catalog_string_gets_the_deployment_as_its_application_name()
        {
            string composed = TenantConnectionStrings.ComposeCatalog("Data Source=catalog;Initial Catalog=tellma-acme-catalog;Application Name=x;Max Pool Size=10", "acme");

            SqlConnectionStringBuilder parsed = new(composed);
            Assert.Equal("acme", parsed.ApplicationName);
            Assert.Equal("tellma-acme-catalog", parsed.InitialCatalog);
            Assert.Equal(10, parsed.MaxPoolSize);
        }

        [Fact]
        public void A_row_with_a_third_language_but_no_second_drops_the_unpaired_third()
        {
            TenantRow row = FakeCatalogReader.Row(1) with { Name3 = "Third", Language3 = "am" };

            TenantDescriptor descriptor = row.ToDescriptor()!;

            Assert.Equal(["en"], descriptor.Languages);
            Assert.Null(descriptor.Name2);
            Assert.Null(descriptor.Name3);
        }

        /// <summary>SQL options with a default profile and the Azure suffix allowed.</summary>
        /// <param name="profile">The default profile's fragment.</param>
        /// <returns>The options.</returns>
        private static TellmaSqlOptions Sql(string profile)
        {
            TellmaSqlOptions sql = new();
            sql.Profiles["default"] = profile;
            sql.AllowedServers.Add(".database.windows.net");
            return sql;
        }
    }
}
