// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Tenancy;

namespace Tellma.Core.Tests.Tenancy
{
    /// <summary>
    ///     What the catalog's write side refuses before it touches the catalog: requested ids outside
    ///     the band, a live tenant naming a live tenant, servers and profiles the deployment does not
    ///     allow, and malformed names, codes, actors and reasons. The catalog connection here throws if
    ///     anything opens it.
    /// </summary>
    public class TenantCatalogValidationTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(1000)]
        [InlineData(5000)]
        public async Task A_requested_id_outside_1_to_999_is_refused(int requestedId)
        {
            using Fixture fixture = new();

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(
                () => fixture.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", requestedId, Token));

            Assert.Equal(TenantProblemCodes.RegistrationRefused, refused.Code);
            Assert.Contains(requestedId.ToString(System.Globalization.CultureInfo.InvariantCulture), refused.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_live_tenant_may_not_name_a_live_tenant()
        {
            using Fixture fixture = new();

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(
                () => fixture.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, 17, null), "migrator", cancellationToken: Token));

            Assert.Equal(TenantProblemCodes.RegistrationRefused, refused.Code);
        }

        [Theory]
        [InlineData("attacker.example.com")]
        [InlineData("sql01.database.windows.net;Password=x")]
        [InlineData("np:\\\\sql01\\pipe\\sql\\query")]
        public async Task A_registration_or_relocation_to_a_server_outside_the_allowed_list_is_refused(string server)
        {
            using Fixture fixture = new();
            TenantLocation location = new(server, "tenant-5", "default");

            TenantStateException registration = await Assert.ThrowsAsync<TenantStateException>(
                () => fixture.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, location), "migrator", cancellationToken: Token));
            TenantStateException relocation = await Assert.ThrowsAsync<TenantStateException>(
                () => fixture.Catalog.RelocateAsync(5, location, "migrator", Token));

            Assert.Equal(TenantProblemCodes.RegistrationRefused, registration.Code);
            Assert.Equal(TenantProblemCodes.LocationRefused, relocation.Code);
            Assert.Contains("outside Tellma:Sql:AllowedServers", relocation.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_registration_without_a_location_needs_the_catalogs_server_to_be_allowed()
        {
            // The catalog of this fixture lives on a host the allowed list does not name.
            using Fixture fixture = new(catalogServer: "catalog.contoso.local");

            TenantStateException refused = await Assert.ThrowsAsync<TenantStateException>(
                () => fixture.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, null), "migrator", cancellationToken: Token));

            Assert.Contains("catalog.contoso.local", refused.Detail, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_profile_that_is_not_configured_is_refused()
        {
            using Fixture fixture = new();
            TenantLocation location = new("sql01.database.windows.net", "tenant-5", "missing");

            Assert.Equal(
                TenantProblemCodes.RegistrationRefused,
                (await Assert.ThrowsAsync<TenantStateException>(() => fixture.Catalog.RegisterAsync(new TenantRegistration("Acme", TenantCategory.Live, null, location), "migrator", cancellationToken: Token))).Code);
            Assert.Equal(
                TenantProblemCodes.LocationRefused,
                (await Assert.ThrowsAsync<TenantStateException>(() => fixture.Catalog.RelocateAsync(5, location, "migrator", Token))).Code);
        }

        [Fact]
        public async Task Malformed_arguments_are_argument_errors_not_refusals()
        {
            using Fixture fixture = new();
            TenantRegistration sound = new("Acme", TenantCategory.Live, null, null);

            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(sound with { Name = " " }, "migrator", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(sound with { Name = new string('n', 256) }, "migrator", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(sound, "", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(sound, new string('a', 129), cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(sound, "line\nbreak", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Catalog.RegisterAsync(sound with { Category = (TenantCategory)7 }, "migrator", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RegisterAsync(
                sound with { Location = new TenantLocation("sql01.database.windows.net", "tenant;5", "default") }, "migrator", cancellationToken: Token));
            _ = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Catalog.SetStateAsync(5, (TenantState)42, null, "migrator", Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.SetStateAsync(5, TenantState.Suspended, new string('r', 1025), "migrator", Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RelocateAsync(5, new TenantLocation("sql01.database.windows.net", "a]b", "default"), "migrator", Token));
        }

        [Theory]
        [MemberData(nameof(UnpairedNames))]
        public async Task A_rename_must_pair_every_name_with_its_code(TenantDisplayNames names)
        {
            using Fixture fixture = new();

            _ = await Assert.ThrowsAsync<ArgumentException>(() => fixture.Catalog.RenameAsync(5, names, Token));
        }

        /// <summary>Names and codes a rename refuses.</summary>
        public static TheoryData<TenantDisplayNames> UnpairedNames =>
        [
            new TenantDisplayNames("Acme", "أكمي", null, ["en"]),
            new TenantDisplayNames("Acme", null, null, ["en", "ar"]),
            new TenantDisplayNames("Acme", "أكمي", "Acme Am", ["en", "ar"]),
            new TenantDisplayNames("Acme", null, "Acme Am", ["en", "ar", "am"]),
            new TenantDisplayNames("Acme", "a", "b", ["en", "ar", "am", "fr"]),
            new TenantDisplayNames("Acme", null, null, ["en_GB"]),
            new TenantDisplayNames(" ", null, null, ["en"]),
        ];

        /// <summary>A catalog write side over a connection that must never be opened.</summary>
        private sealed class Fixture : IDisposable
        {
            private readonly RegistryHarness _registry = new();

            /// <summary>Creates the fixture.</summary>
            /// <param name="catalogServer">The server the catalog connection names.</param>
            public Fixture(string catalogServer = "catalog.database.windows.net")
            {
                TellmaSqlOptions sql = new();
                sql.Profiles["default"] = "Authentication=Active Directory Managed Identity;Encrypt=True;Max Pool Size=20";
                sql.AllowedServers.Add(".database.windows.net");

                Catalog = new TenantCatalog(
                    new UnopenedConnections(),
                    new CatalogConnection(
                        Options.Create(new TellmaCatalogOptions { ConnectionString = $"Data Source={catalogServer};Initial Catalog=catalog;Encrypt=True" }),
                        RegistryHarness.Deployment),
                    _registry.Registry,
                    new ChangeableOptionsMonitor<TellmaSqlOptions>(sql),
                    new TenancyEnvironment("acme", IsDevelopment: false),
                    RegistryHarness.Deployment,
                    NullLogger<TenantCatalog>.Instance);
            }

            /// <summary>The write side.</summary>
            public TenantCatalog Catalog { get; }

            /// <inheritdoc />
            public void Dispose()
            {
                _registry.Dispose();
            }
        }

        /// <summary>A connection factory that fails the test if anything opens a connection.</summary>
        private sealed class UnopenedConnections : ITenantConnectionFactory
        {
            /// <inheritdoc />
            public string GetConnectionString(int tenantId)
            {
                throw new InvalidOperationException("A refusal must not reach the catalog.");
            }

            /// <inheritdoc />
            public Task<SqlConnection> OpenAsync(int tenantId, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("A refusal must not reach the catalog.");
            }

            /// <inheritdoc />
            public Task<SqlConnection> OpenCatalogAsync(CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("A refusal must not reach the catalog.");
            }
        }
    }
}
