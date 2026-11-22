// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Net;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Distro.Acme.TestSupport;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     The realtime hub's front door over Acme's tenant database: a member's negotiate admitted
    ///     without stamping the member's activity, the same member deactivated while the connect cache
    ///     still admits them refused by the door's fresh read of the tenant's users, and a frozen
    ///     tenant whose database is set read-only still admitting a negotiate.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class RealtimeHubTests(SqlServerFixture sql)
    {
        /// <summary>Why a negotiate a tenant admits to the seeded administrator cannot be asserted yet.</summary>
        private const string NeedsUserConnectionAndBootstrap =
            "Needs the user connection initializer, which binds a tenant request to its tenant user, "
            + "and the administrator bootstrap provisioning step, which makes the seeded administrator a user of the seed tenants; "
            + "until the initializer is composed, every request a tenant serves fails closed.";

        /// <summary>The tenant every test connects to: Acme's live seed tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The <c>Tellma-Client</c> value the SPA's realtime client sends.</summary>
        private const string ClientHeaderValue = "web/1.0";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_member_s_negotiate_stamps_no_activity_and_a_deactivation_behind_a_warm_cache_refuses_the_next()
        {
            AcmeMigrator acme = new(sql, "acme_hub_member");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);
            int administrator = await AdministratorIdAsync(acme);
            DateTimeOffset? before = await LastActiveAtAsync(acme, administrator);

            // --- The negotiate admits the member and leaves the activity stamp alone. It is the
            // session's first request to the tenant, so its connect is the cold round trip a read's
            // stamp would land on ---
            using (HttpResponseMessage negotiate = await NegotiateAsync(flow.Browser))
            {
                Assert.True(negotiate.StatusCode == HttpStatusCode.OK, await Describe(negotiate, host));
            }

            Assert.Equal(before, await LastActiveAtAsync(acme, administrator));

            // --- The connect cache, warm from that connect, still serves the member ---
            using (HttpResponseMessage read = await TenantProbes.ReadAsync(flow.Browser, Tenant))
            {
                Assert.True(read.StatusCode == HttpStatusCode.OK, await Describe(read, host));
            }

            // --- Deactivated in the tenant's database, which the warm cache has not seen ---
            await SqlServerFixture.ExecuteAsync(
                acme.TenantConnectionString(Tenant),
                string.Create(CultureInfo.InvariantCulture, $"UPDATE [core].[Users] SET [IsActive] = 0 WHERE [Id] = {administrator};"));

            using HttpResponseMessage refused = await NegotiateAsync(flow.Browser);
            string code = await TenantProbes.ProblemCodeAsync(refused);
            Assert.True(
                refused.StatusCode == HttpStatusCode.NotFound && code == TenantProblemCodes.NotFound,
                $"{(int)refused.StatusCode} {code}\n{host.Warnings}");
        }

        [Fact(Skip = NeedsUserConnectionAndBootstrap, SkipUnless = nameof(PlatformSeams.ServesTheAdministrator), SkipType = typeof(PlatformSeams))]
        public async Task A_frozen_tenant_admits_a_negotiate_against_a_database_set_read_only()
        {
            AcmeMigrator acme = new(sql, "acme_hub_frozen");
            _ = await acme.MigrateAsync();

            // --- The relocation runbook's first two steps: freeze, then set the database read-only ---
            await acme.SetStateAsync(Tenant, TenantState.Frozen, "relocation");
            await acme.SetDatabaseReadOnlyAsync(Tenant);
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values, new AcmeHostOptions { MapsProbes = true });
            using OidcFlowClient flow = host.CreateFlowClient();
            await AcmeSignIn.SignInAsync(flow);

            // The door's round trip writes nothing, so a database that refuses every write admits it.
            using HttpResponseMessage negotiate = await NegotiateAsync(flow.Browser);

            Assert.True(negotiate.StatusCode == HttpStatusCode.OK, await Describe(negotiate, host));
        }

        /// <summary>Posts the hub's negotiate for the tenant as the SPA's realtime client does.</summary>
        /// <param name="browser">A browser holding a session.</param>
        /// <returns>The response; the caller disposes it.</returns>
        private static async Task<HttpResponseMessage> NegotiateAsync(HttpClient browser)
        {
            using HttpRequestMessage request = new(HttpMethod.Post, new Uri(string.Create(CultureInfo.InvariantCulture, $"/{Tenant}/hub/negotiate?negotiateVersion=1"), UriKind.Relative));
            _ = request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-origin");
            _ = request.Headers.TryAddWithoutValidation("Origin", AcmeMigrator.PublicOrigin);
            _ = request.Headers.TryAddWithoutValidation(TellmaAuthentication.CsrfHeaderName, ClientHeaderValue);
            return await browser.SendAsync(request, Token);
        }

        /// <summary>A response's status and body, with the host's warnings, for a failed assertion.</summary>
        /// <param name="response">The response.</param>
        /// <param name="host">The host that answered.</param>
        /// <returns>The text.</returns>
        private static async Task<string> Describe(HttpResponseMessage response, AcmeHost host)
        {
            return $"{(int)response.StatusCode} {await response.Content.ReadAsStringAsync(Token)}\n{host.Warnings}";
        }

        /// <summary>The seeded administrator's user id in the tenant.</summary>
        /// <param name="acme">The deployment.</param>
        /// <returns>The id.</returns>
        private static async Task<int> AdministratorIdAsync(AcmeMigrator acme)
        {
            return await SqlServerFixture.ScalarAsync<int?>(
                acme.TenantConnectionString(Tenant), $"SELECT [Id] FROM [core].[Users] WHERE [Email] = N'{AcmeSignIn.Administrator}';")
                ?? throw new InvalidOperationException($"Tenant {Tenant} has no user {AcmeSignIn.Administrator}.");
        }

        /// <summary>When a user was last active in the tenant, as the user's stamp row records it.</summary>
        /// <param name="acme">The deployment.</param>
        /// <param name="userId">The user.</param>
        /// <returns>The stamp, or null when the user has never been active.</returns>
        private static Task<DateTimeOffset?> LastActiveAtAsync(AcmeMigrator acme, int userId)
        {
            return SqlServerFixture.ScalarAsync<DateTimeOffset?>(
                acme.TenantConnectionString(Tenant), string.Create(CultureInfo.InvariantCulture, $"SELECT [LastActiveAt] FROM [core].[UserStamps] WHERE [UserId] = {userId};"));
        }
    }
}
