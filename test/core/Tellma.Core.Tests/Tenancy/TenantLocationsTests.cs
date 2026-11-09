// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>The location grammar and the allowed-servers rule.</summary>
    public class TenantLocationsTests
    {
        private static readonly string[] AzureOnly = [".database.windows.net"];

        [Theory]
        [InlineData("sql01.database.windows.net", "sql01.database.windows.net")]
        [InlineData("tcp:sql01.database.windows.net,1433", "sql01.database.windows.net")]
        [InlineData("TCP:sql01.database.windows.net", "sql01.database.windows.net")]
        [InlineData("sql01.contoso.local\\TELLMA", "sql01.contoso.local")]
        [InlineData("sql01\\TELLMA,14330", "sql01")]
        [InlineData("(localdb)\\MSSQLLocalDB", "(localdb)")]
        [InlineData("localhost", "localhost")]
        [InlineData("10.0.0.4,1433", "10.0.0.4")]
        public void A_server_parses_to_the_host_SqlClient_connects_to(string server, string host)
        {
            Assert.True(TenantLocations.TryParseServer(server, out string parsed));
            Assert.Equal(host, parsed);
        }

        [Theory]
        [InlineData("")]
        [InlineData("np:\\\\sql01\\pipe\\sql\\query")]
        [InlineData("lpc:sql01")]
        [InlineData("admin:sql01")]
        [InlineData("sql01;Password=x")]
        [InlineData("sql01=x")]
        [InlineData("sql01 ")]
        [InlineData(" sql01")]
        [InlineData("sql 01")]
        [InlineData("sql01,")]
        [InlineData("sql01,0")]
        [InlineData("sql01,65536")]
        [InlineData("sql01,14a")]
        [InlineData("sql01\\")]
        [InlineData("sql01\\a b")]
        [InlineData("evil.com:1433.database.windows.net")]
        [InlineData("[fe80::1]")]
        [InlineData("sql01/x")]
        public void A_server_outside_the_grammar_is_refused(string server)
        {
            Assert.False(TenantLocations.TryParseServer(server, out _));
            Assert.False(TenantLocations.IsServerAllowed(server, AzureOnly, isDevelopment: true));
        }

        [Fact]
        public void A_server_longer_than_a_row_holds_is_refused()
        {
            Assert.False(TenantLocations.TryParseServer(new string('a', 256), out _));
            Assert.True(TenantLocations.TryParseServer(new string('a', 255), out _));
        }

        [Theory]
        [InlineData("sql01.database.windows.net", true)]
        [InlineData("SQL01.Database.Windows.NET", true)]
        [InlineData("tcp:sql01.database.windows.net,1433", true)]
        [InlineData("database.windows.net", false)]
        [InlineData(".database.windows.net", false)]
        [InlineData("sql01.database.windows.net.attacker.example.com", false)]
        [InlineData("attackerdatabase.windows.net", false)]
        [InlineData("sql01.contoso.local", false)]
        public void A_suffix_entry_matches_any_longer_host_ending_with_it_ignoring_case(string server, bool allowed)
        {
            Assert.Equal(allowed, TenantLocations.IsServerAllowed(server, AzureOnly, isDevelopment: false));
        }

        [Theory]
        [InlineData("sql01.contoso.local", true)]
        [InlineData("SQL01.CONTOSO.LOCAL\\TELLMA,1433", true)]
        [InlineData("sql02.contoso.local", false)]
        [InlineData("x.sql01.contoso.local", false)]
        public void A_host_entry_matches_that_host_alone(string server, bool allowed)
        {
            Assert.Equal(allowed, TenantLocations.IsServerAllowed(server, ["sql01.contoso.local"], isDevelopment: false));
        }

        [Fact]
        public void An_empty_list_allows_every_host_in_Development_and_none_elsewhere()
        {
            Assert.True(TenantLocations.IsServerAllowed("(localdb)\\MSSQLLocalDB", [], isDevelopment: true));
            Assert.False(TenantLocations.IsServerAllowed("(localdb)\\MSSQLLocalDB", [], isDevelopment: false));

            // A list in Development is honoured as written.
            Assert.False(TenantLocations.IsServerAllowed("(localdb)\\MSSQLLocalDB", AzureOnly, isDevelopment: true));
        }

        [Theory]
        [InlineData(".database.windows.net", true)]
        [InlineData("sql01.contoso.local", true)]
        [InlineData("(localdb)", true)]
        [InlineData(".", false)]
        [InlineData("", false)]
        [InlineData("*.database.windows.net", false)]
        [InlineData("sql01:1433", false)]
        public void An_allowed_servers_entry_is_a_host_or_a_dotted_suffix(string entry, bool valid)
        {
            Assert.Equal(valid, TenantLocations.IsValidAllowedEntry(entry));
        }

        [Theory]
        [InlineData("sql1.contoso.local", "sql1.contoso.local")]
        [InlineData("tcp:sql1.contoso.local", "sql1.contoso.local")]
        [InlineData("SQL1.Contoso.Local,1433", "sql1.contoso.local")]
        [InlineData("tcp:SQL1.contoso.local,1433", "sql1.contoso.local")]
        [InlineData("sql1.contoso.local,14330", "sql1.contoso.local,14330")]
        [InlineData("SQL1\\Tellma,1433", "sql1\\tellma")]
        [InlineData("(localdb)\\MSSQLLocalDB", "(localdb)\\mssqllocaldb")]
        public void Spellings_of_one_server_share_one_canonical_form(string server, string canonical)
        {
            Assert.Equal(canonical, TenantLocations.Canonicalize(server));
        }

        [Fact]
        public void Only_a_server_in_the_grammar_has_a_canonical_form()
        {
            _ = Assert.Throws<ArgumentException>(() => TenantLocations.Canonicalize("np:sql1"));
        }

        [Theory]
        [InlineData("Tellma.dev.acme.1", true)]
        [InlineData("tellma-acme-1000", true)]
        [InlineData("tenant;Password=x", false)]
        [InlineData("tenant=x", false)]
        [InlineData("tenant]x", false)]
        [InlineData(" tenant", false)]
        [InlineData("tenant\n", false)]
        [InlineData("", false)]
        public void A_database_name_admits_neither_separators_nor_control_characters(string database, bool valid)
        {
            Assert.Equal(valid, TenantLocations.IsValidDatabaseName(database));
            Assert.False(TenantLocations.IsValidDatabaseName(new string('d', 129)));
        }

        [Theory]
        [InlineData("default", true)]
        [InlineData("warm-1", true)]
        [InlineData("on_prem.linux", true)]
        [InlineData("my profile", false)]
        [InlineData("x;y", false)]
        [InlineData("", false)]
        public void A_profile_name_is_letters_digits_dots_hyphens_and_underscores(string profile, bool valid)
        {
            Assert.Equal(valid, TenantLocations.IsValidProfileName(profile));
        }
    }
}
