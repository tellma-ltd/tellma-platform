// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using OpenIddict.EntityFrameworkCore.Models;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     One user signing in to one client from two tabs of one session at once. Both grants read
    ///     the client's permanent authorization and the session's registration of the client, and
    ///     both write them. Neither sign-in may fail for the race: a grant whose write loses reads
    ///     again and goes on from what the other grant wrote, so both tabs get a code, the one
    ///     authorization ends up covering every place either tab asked for, and the session holds
    ///     one registration of the client. Whatever authorizations a session's grants end up
    ///     with, a logout of the session revokes every one, including one a grant registers while
    ///     the logout runs; a grant that loses to the logout gets no code.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConcurrentGrantTests(SqlServerFixture fixture)
    {
        private const string ClientId = "tellma-cli";
        private const string RedirectUri = "http://127.0.0.1/callback";
        private const string Origin = "https://acme.app.tellma.com";
        private const string Tenant17 = Origin + "/17/mcp";
        private const string Tenant18 = Origin + "/18/mcp";
        private const string Tenant19 = Origin + "/19/mcp";

        /// <summary>The scopes the CLI asks for unless a test asks for more.</summary>
        private const string DefaultScope = "openid offline_access tellma_api";

        /// <summary>The distribution's registered post-logout callback, which the end-session prompt returns to.</summary>
        private const string PostLogoutRedirectUri = Origin + "/signout-callback-oidc";

        /// <summary>The store's first migration, the schema the previous engine release runs on.</summary>
        private const string InitialMigration = "20261108125718_Initial";

        /// <summary>A public CLI client, which onboarding grants each distribution's origin.</summary>
        private static readonly Dictionary<string, string?> CliSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ClientId,
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = RedirectUri,
        };

        [Theory]
        [InlineData(Tenant18, Tenant18)]
        [InlineData(Tenant18, Tenant19)]
        public async Task Two_tabs_extending_one_authorization_at_once_both_get_a_code(string first, string second)
        {
            // Each grant's write to the authorization waits until the other's has arrived too, so
            // both extend the copy they read before either was written: the race the timing of
            // two real tabs only sometimes produces.
            SaveBarrier barrier = new(static tracker => Has<OpenIddictEntityFrameworkCoreAuthorization>(tracker, EntityState.Modified));
            using StandaloneFactory factory = CreateFactory("idgrantrace", barrier);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "tabs@example.com");

            // The first grant creates the client's authorization, for one tenant.
            using (HttpResponseMessage created = await AuthorizeAsync(flow, Tenant17))
            {
                Assert.NotNull(Code(created));
            }

            // Two tabs then ask, at once, for places the authorization does not cover yet.
            barrier.Arm();
            await AuthorizeInTwoTabsAsync(flow, first, second);
            Assert.True(barrier.BothArrived, "The two grants did not both write to the authorization.");

            // Still one authorization for the client, now covering every place either tab asked
            // for: the losing write was made again over what the winner wrote, never beside it.
            string[] expected = [.. new[] { Tenant17, first, second }.Distinct().Order(StringComparer.Ordinal)];
            Assert.Equal(expected, await CoveredResourcesAsync(factory));
        }

        [Fact]
        public async Task Two_tabs_making_a_sessions_first_grant_at_once_both_get_tokens_that_a_logout_ends()
        {
            // Both grants create the authorization and then register the client under the session,
            // each held until the other arrives, so both find nothing before either inserts.
            SaveBarrier creates = new(static tracker => Has<OpenIddictEntityFrameworkCoreAuthorization>(tracker, EntityState.Added));
            SaveBarrier registrations = new(static tracker => Has<IdentitySessionClient>(tracker, EntityState.Added));
            using StandaloneFactory factory = CreateFactory("idgrantfirst", creates, registrations);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "first@example.com");

            creates.Arm();
            registrations.Arm();
            Grant[] grants = await AuthorizeInTwoTabsAsync(flow, Tenant17, Tenant17);
            Assert.True(creates.BothArrived, "The two grants did not both create an authorization.");
            Assert.True(registrations.BothArrived, "The two grants did not both register the client.");

            // Each tab redeems its code for a refresh token. Each grant created an authorization of
            // its own, and the session's one registration of the client records both.
            string[] refreshTokens = [await RedeemAsync(flow, grants[0]), await RedeemAsync(flow, grants[1])];
            List<string> authorizationIds = await AuthorizationIdsAsync(factory);
            Assert.Equal(2, authorizationIds.Count);
            Assert.Equal(authorizationIds, await RegisteredAuthorizationIdsAsync(factory));

            // Signing out of the session stops both tabs' renewal, not only the one registered last.
            await SignOutAsync(flow);
            await AssertEndedAsync(factory, flow, refreshTokens);
        }

        [Fact]
        public async Task A_grant_asking_for_more_scopes_gets_its_own_authorization_and_a_logout_ends_both()
        {
            // No race: a later grant of the session asks for a scope the client's authorization
            // lacks, so it finds none to extend and creates a second. Both back refresh tokens.
            using StandaloneFactory factory = CreateFactory("idgrantscopes");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "scopes@example.com");

            string first = await RedeemAsync(flow, await AuthorizeForCodeAsync(flow, Tenant17, DefaultScope));
            string second = await RedeemAsync(flow, await AuthorizeForCodeAsync(flow, Tenant17, DefaultScope + " profile"));
            List<string> authorizationIds = await AuthorizationIdsAsync(factory);
            Assert.Equal(2, authorizationIds.Count);
            Assert.Equal(authorizationIds, await RegisteredAuthorizationIdsAsync(factory));

            await SignOutAsync(flow);
            await AssertEndedAsync(factory, flow, [first, second]);
        }

        [Fact]
        public async Task A_grant_whose_session_ends_while_it_registers_the_client_gets_no_code()
        {
            // The session's row is deleted, as a sign-out's prune would, between the grant's read
            // of the registration and its insert. The insert's failure is not a lost race: the
            // grant fails, and no code is issued under a session that no longer exists, which no
            // logout could ever end.
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idgrantended", CliSeed);
            SessionEnder ender = new(factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!, SessionEnder.Delete);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(ender)));
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "ended@example.com");

            ender.Arm();
            using HttpResponseMessage response = await AuthorizeAsync(flow, Tenant17);

            Assert.True(ender.Ended, "The session was not ended while the client was registered.");
            Assert.Null(Code(response));
            Assert.Empty(await RegisteredAuthorizationIdsAsync(factory));
        }

        [Fact]
        public async Task A_grant_whose_session_a_logout_ends_while_it_registers_the_client_gets_no_code()
        {
            // A logout marks the session ended between the grant's read of the registration and its
            // insert. The session's row stays, so the insert itself would succeed: the grant must
            // see the end and record nothing, or it would hold an authorization the logout never
            // read and no later logout of the session reaches.
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idgrantlogout", CliSeed);
            SessionEnder ender = new(factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!, SessionEnder.Terminate);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(ender)));
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "logout@example.com");

            ender.Arm();
            using HttpResponseMessage response = await AuthorizeAsync(flow, Tenant17);

            Assert.True(ender.Ended, "The session was not ended while the client was registered.");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            Assert.False(query.ContainsKey("code"), "A code was issued under the ended session.");
            Assert.Equal("login_required", (string?)query["error"]);
            Assert.Empty(await RegisteredAuthorizationIdsAsync(factory));
        }

        [Fact]
        public async Task A_logout_that_waits_on_a_grants_registration_revokes_the_authorization_it_registers()
        {
            // The grant's registration has written its rows and holds the session's row, and is
            // held there before it commits. A logout of the session meanwhile must wait for it and
            // then revoke what it registered, not read the registrations before the grant commits
            // and leave the new authorization out.
            RegistrationHold hold = new();
            using StandaloneFactory factory = CreateFactory("idgrantwait", hold);
            string connectionString = factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!;
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "wait@example.com");
            (string action, Dictionary<string, string> fields) = await OpenSignOutAsync(flow);

            hold.Arm();
            Task<Grant> granting = Task.Run(
                () => AuthorizeForCodeAsync(flow, Tenant17, DefaultScope), TestContext.Current.CancellationToken);
            await hold.Held.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

            // The logout runs while the registration is held, and is let go only once its write
            // to the session waits on the registration's lock.
            Task<HttpResponseMessage> signingOut = Task.Run(
                () => flow.PostFormAsync(action, fields), TestContext.Current.CancellationToken);
            await WaitUntilBlockedAsync(connectionString, "%SET [[]TerminatedUtc]%");
            hold.Release();

            // The grant got its code, and the logout revoked the authorization behind it.
            await granting;
            using (HttpResponseMessage confirmed = await signingOut)
            {
                Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
            }

            Assert.Single(await AuthorizationIdsAsync(factory));
            Assert.Equal(await AuthorizationIdsAsync(factory), await RegisteredAuthorizationIdsAsync(factory));
            await AssertEndedAsync(factory, flow, []);
        }

        [Fact]
        public async Task Two_tabs_recording_an_authorization_new_to_the_session_at_once_both_get_a_code()
        {
            // The client is registered under the session already, and both tabs use an
            // authorization the session has not recorded, which another session of the user's
            // made. Both find the session's record of it missing before either inserts it, so they
            // race on that row alone.
            SaveBarrier rows = new(static tracker => Has<IdentitySessionClientAuthorization>(tracker, EntityState.Added));
            using StandaloneFactory factory = CreateFactory("idgrantrow", rows);
            using OidcFlowClient elsewhere = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, elsewhere, "rows@example.com");
            using (HttpResponseMessage made = await AuthorizeAsync(elsewhere, Tenant17))
            {
                Assert.NotNull(Code(made));
            }

            // This session registers the client with an authorization of its own first: it asks
            // for more scopes, and for another tenant, so it does not cover the tabs' grants.
            using OidcFlowClient flow = factory.CreateFlowClient();
            await flow.SignInWithEmailCodeAsync("rows@example.com", "/Identity/Account/Login");
            using (HttpResponseMessage own = await AuthorizeAsync(flow, Tenant18, DefaultScope + " profile"))
            {
                Assert.NotNull(Code(own));
            }

            rows.Arm();
            await AuthorizeInTwoTabsAsync(flow, Tenant17, Tenant17);
            Assert.True(rows.BothArrived, "The two grants did not both record the authorization.");

            // The other session records the shared authorization; this one records it once, beside
            // its own.
            List<string[]> sessions = await RegisteredAuthorizationIdsBySessionAsync(factory);
            Assert.Equal(2, sessions.Count);
            string shared = Assert.Single(sessions[0]);
            Assert.Equal(await AuthorizationIdsAsync(factory), sessions[1]);
            Assert.Contains(shared, sessions[1]);
        }

        [Fact]
        public async Task Signing_out_everywhere_revokes_every_authorization_a_session_used()
        {
            // Two authorizations back the CLI's refresh tokens in one session. Signing out
            // everywhere revokes both, not only the one the registration names last.
            using StandaloneFactory factory = CreateFactory("idgranteverywhere");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "everywhere@example.com");

            string first = await RedeemAsync(flow, await AuthorizeForCodeAsync(flow, Tenant17, DefaultScope));
            string second = await RedeemAsync(flow, await AuthorizeForCodeAsync(flow, Tenant17, DefaultScope + " profile"));
            Assert.Equal(2, (await AuthorizationIdsAsync(factory)).Count);

            using (HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/Sessions", UriKind.Relative), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
                (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page);
                using HttpResponseMessage signedOut = await flow.PostFormAsync(action, fields);
                Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);
            }

            await AssertEndedAsync(factory, flow, [first, second]);
        }

        [Fact]
        public async Task A_session_registered_before_the_upgrade_is_still_ended_by_a_logout_after_it()
        {
            // The store is taken back to the schema the previous engine release runs on, and
            // forward again. What is left is what that release writes: the client's registration
            // naming the authorization in its own column, and no row beneath it.
            using StandaloneFactory factory = CreateFactory("idgrantupgrade");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "upgrade@example.com");
            string refreshToken = await RedeemAsync(flow, await AuthorizeForCodeAsync(flow, Tenant17, DefaultScope));

            using (IServiceScope scope = factory.Services.CreateScope())
            {
                IMigrator migrator = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>().GetService<IMigrator>();
                await migrator.MigrateAsync(InitialMigration, TestContext.Current.CancellationToken);
                await migrator.MigrateAsync(TellmaIdentityConstants.LatestMigration, TestContext.Current.CancellationToken);
            }

            Assert.Empty(await RegisteredAuthorizationIdsAsync(factory));

            // The logout after the upgrade still stops the session's renewal.
            await SignOutAsync(flow);
            await AssertEndedAsync(factory, flow, [refreshToken]);
        }

        [Fact]
        public async Task A_grant_that_keeps_losing_its_write_gives_up_after_three_attempts()
        {
            // Every write to the authorization is overtaken by another writer, as if one tab kept
            // racing a stream of others. The grant stops after three attempts, and the client is
            // told to try again rather than given a code the authorization does not record.
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idgrantlost", CliSeed);
            AuthorizationWriteSaboteur saboteur = new(factory.ConfigurationOverrides["TellmaIdentity:ConnectionString"]!);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(saboteur)));
            using OidcFlowClient flow = factory.CreateFlowClient();
            await ConsentTests.SignInAsync(factory, flow, "lost@example.com");

            using (HttpResponseMessage created = await AuthorizeAsync(flow, Tenant17))
            {
                Assert.NotNull(Code(created));
            }

            using HttpResponseMessage refused = await AuthorizeAsync(flow, Tenant18);

            Assert.Equal(HttpStatusCode.Redirect, refused.StatusCode);
            Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(refused.Headers.Location!.Query);
            Assert.Equal("temporarily_unavailable", (string?)query["error"]);
            Assert.False(query.ContainsKey("code"));
            Assert.Equal(3, saboteur.Attempts);
            Assert.Equal([Tenant17], await CoveredResourcesAsync(factory));
        }

        /// <summary>Boots a host with the CLI seeded and the given interceptors on its store context.</summary>
        private StandaloneFactory CreateFactory(string databasePrefix, params IInterceptor[] interceptors)
        {
            StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, databasePrefix, CliSeed);
            factory.ServiceOverrides.Add(services => services.ConfigureDbContext<TellmaIdentityDbContext>(
                (_, builder) => builder.AddInterceptors(interceptors)));
            return factory;
        }

        /// <summary>Runs two of the CLI's authorizations at once on one session, and asserts each returns a code.</summary>
        private static async Task<Grant[]> AuthorizeInTwoTabsAsync(OidcFlowClient flow, string first, string second)
        {
            return await Task.WhenAll(
                Task.Run(() => AuthorizeForCodeAsync(flow, first, DefaultScope), TestContext.Current.CancellationToken),
                Task.Run(() => AuthorizeForCodeAsync(flow, second, DefaultScope), TestContext.Current.CancellationToken));
        }

        /// <summary>Runs the CLI's authorization on the established session, and asserts it returns a code.</summary>
        private static async Task<Grant> AuthorizeForCodeAsync(OidcFlowClient flow, string resource, string scope)
        {
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
            using HttpResponseMessage response = await AuthorizeAsync(flow, resource, scope, challenge);
            string? code = Code(response);
            Assert.True(code is not null, $"No code returned: {(int)response.StatusCode} {response.Headers.Location}");
            return new Grant(code, verifier, resource);
        }

        /// <summary>Starts the CLI's authorization for one resource on the established session.</summary>
        private static async Task<HttpResponseMessage> AuthorizeAsync(
            OidcFlowClient flow, string resource, string scope = DefaultScope, string? challenge = null)
        {
            challenge ??= OidcFlowClient.CreatePkcePair().Challenge;
            string url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "state-1",
                ["resource"] = resource,
            });

            return await flow.Browser.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        }

        /// <summary>Redeems a grant's code at the token endpoint and returns its refresh token.</summary>
        private static async Task<string> RedeemAsync(OidcFlowClient flow, Grant grant)
        {
            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = grant.Code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = grant.Verifier,
                ["resource"] = grant.Resource,
            });

            return tokens.RootElement.GetProperty("refresh_token").GetString()!;
        }

        /// <summary>Signs the browser out of its session, confirming the end-session prompt as the user would.</summary>
        private static async Task SignOutAsync(OidcFlowClient flow)
        {
            (string action, Dictionary<string, string> fields) = await OpenSignOutAsync(flow);
            using HttpResponseMessage confirmed = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
        }

        /// <summary>Opens the end-session prompt and returns its confirmation, ready to post.</summary>
        private static async Task<(string Action, Dictionary<string, string> Fields)> OpenSignOutAsync(OidcFlowClient flow)
        {
            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/connect/endsession?post_logout_redirect_uri=" + Uri.EscapeDataString(PostLogoutRedirectUri), UriKind.Relative),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page);
            fields["submit.SignOut"] = "yes";
            return (action, fields);
        }

        /// <summary>
        ///     Waits until a statement in the store, matching a pattern, waits on another
        ///     transaction's lock. Fails after 30 seconds.
        /// </summary>
        /// <param name="connectionString">The store's connection string.</param>
        /// <param name="pattern">A <c>LIKE</c> pattern over the waiting statement's text.</param>
        private static async Task WaitUntilBlockedAsync(string connectionString, string pattern)
        {
            await using SqlConnection connection = new(connectionString);
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using SqlCommand command = new(
                """
                SELECT COUNT(*)
                  FROM [sys].[dm_exec_requests] AS [r]
                 CROSS APPLY [sys].[dm_exec_sql_text]([r].[sql_handle]) AS [t]
                 WHERE [r].[database_id] = DB_ID() AND [r].[blocking_session_id] <> 0 AND [t].[text] LIKE @pattern;
                """,
                connection);
            command.Parameters.AddWithValue("@pattern", pattern);
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while ((int)await command.ExecuteScalarAsync(TestContext.Current.CancellationToken) == 0)
            {
                Assert.True(DateTime.UtcNow < deadline, $"No statement like '{pattern}' waited on a lock.");
                await Task.Delay(50, TestContext.Current.CancellationToken);
            }
        }

        /// <summary>
        ///     Asserts that every authorization of the CLI's is revoked and that none of the given
        ///     refresh tokens renews any more.
        /// </summary>
        private static async Task AssertEndedAsync(StandaloneFactory factory, OidcFlowClient flow, string[] refreshTokens)
        {
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
                foreach (string id in await AuthorizationIdsAsync(factory))
                {
                    object authorization = (await authorizations.FindByIdAsync(id, TestContext.Current.CancellationToken))!;
                    Assert.Equal(
                        OpenIddictConstants.Statuses.Revoked,
                        await authorizations.GetStatusAsync(authorization, TestContext.Current.CancellationToken));
                }
            }

            foreach (string refreshToken in refreshTokens)
            {
                Assert.Equal(
                    "invalid_grant",
                    await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "refresh_token",
                        ["client_id"] = ClientId,
                        ["refresh_token"] = refreshToken,
                    }));
            }
        }

        /// <summary>The ids of every authorization of the CLI's, in ordinal order.</summary>
        private static async Task<List<string>> AuthorizationIdsAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object client = (await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken))!;
            List<object> rows = [];
            await foreach (object row in authorizations.FindByApplicationIdAsync(
                (await applications.GetIdAsync(client, TestContext.Current.CancellationToken))!, TestContext.Current.CancellationToken))
            {
                rows.Add(row);
            }

            List<string> ids = [];
            foreach (object row in rows)
            {
                ids.Add((await authorizations.GetIdAsync(row, TestContext.Current.CancellationToken))!);
            }

            return [.. ids.Order(StringComparer.Ordinal)];
        }

        /// <summary>The authorizations the session registry records for the CLI, in ordinal order.</summary>
        private static async Task<List<string>> RegisteredAuthorizationIdsAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            List<string> ids = await db.Set<IdentitySessionClientAuthorization>()
                .Where(static row => row.ClientId == ClientId)
                .Select(static row => row.AuthorizationId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. ids.Order(StringComparer.Ordinal)];
        }

        /// <summary>
        ///     The authorizations the session registry records for the CLI, one ordinal-ordered
        ///     array per session, the sessions ordered by how many they record.
        /// </summary>
        private static async Task<List<string[]>> RegisteredAuthorizationIdsBySessionAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            List<IdentitySessionClientAuthorization> rows = await db.Set<IdentitySessionClientAuthorization>()
                .AsNoTracking()
                .Where(static row => row.ClientId == ClientId)
                .ToListAsync(TestContext.Current.CancellationToken);
            return [.. rows
                .GroupBy(static row => row.Sid, StringComparer.Ordinal)
                .Select(static session => session.Select(static row => row.AuthorizationId).Order(StringComparer.Ordinal).ToArray())
                .OrderBy(static ids => ids.Length)];
        }

        /// <summary>The code a redirect back to the client carries, if it carries one.</summary>
        private static string? Code(HttpResponseMessage response)
        {
            return response.StatusCode == HttpStatusCode.Redirect
                && response.Headers.Location is { } location
                && QueryHelpers.ParseQuery(location.Query).TryGetValue("code", out StringValues code)
                    ? (string?)code
                    : null;
        }

        /// <summary>The resources the CLI's one authorization covers, in ordinal order.</summary>
        private static async Task<List<string>> CoveredResourcesAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object client = (await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken))!;
            List<object> rows = [];
            await foreach (object row in authorizations.FindByApplicationIdAsync(
                (await applications.GetIdAsync(client, TestContext.Current.CancellationToken))!, TestContext.Current.CancellationToken))
            {
                rows.Add(row);
            }

            object authorization = Assert.Single(rows);
            return [.. AuthorizationResources
                .Read(await authorizations.GetPropertiesAsync(authorization, TestContext.Current.CancellationToken))
                .Order(StringComparer.Ordinal)];
        }

        /// <summary>Whether a save is about to write an entity of a type in a state.</summary>
        private static bool Has<TEntity>(ChangeTracker tracker, EntityState state)
            where TEntity : class
        {
            return tracker.Entries<TEntity>().Any(entry => entry.State == state);
        }

        /// <summary>
        ///     Once armed, holds the first two saves that match a condition until both have
        ///     arrived, so each grant read what it is about to write before the other wrote it.
        ///     Later saves pass straight through. A save waits at most 30 seconds for the other, so
        ///     a grant that never makes the write fails the test's assertion rather than hanging it.
        /// </summary>
        /// <param name="holds">Whether a save, by what its context is about to write, is held.</param>
        private sealed class SaveBarrier(Func<ChangeTracker, bool> holds) : SaveChangesInterceptor
        {
            /// <summary>Completes when the second save arrives.</summary>
            private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>How many matching saves have arrived.</summary>
            private int _arrivals;

            /// <summary>Whether matching saves are held yet (1) or not (0).</summary>
            private int _armed;

            /// <summary>Whether both of the held saves arrived.</summary>
            public bool BothArrived => _bothArrived.Task.IsCompleted;

            /// <summary>Starts holding matching saves: the setup before the race writes freely.</summary>
            public void Arm()
            {
                Volatile.Write(ref _armed, 1);
            }

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);
                if (Volatile.Read(ref _armed) == 1 && eventData.Context is { } context && holds(context.ChangeTracker))
                {
                    int arrival = Interlocked.Increment(ref _arrivals);
                    if (arrival == 2)
                    {
                        _bothArrived.TrySetResult();
                    }

                    if (arrival <= 2)
                    {
                        await Task.WhenAny(_bothArrived.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));
                    }
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        /// <summary>A code the CLI's authorization returned, with what redeeming it takes.</summary>
        /// <param name="Code">The authorization code.</param>
        /// <param name="Verifier">The PKCE verifier its challenge was made from.</param>
        /// <param name="Resource">The resource the authorization named.</param>
        private sealed record Grant(string Code, string Verifier, string Resource);

        /// <summary>
        ///     Once armed, ends the session from another connection when a save is about to
        ///     register a client under it, between the grant's read of the registration and its
        ///     insert. It acts once.
        /// </summary>
        /// <param name="connectionString">The store's connection string.</param>
        /// <param name="statement">How it ends the session, given the session's id as <c>@sid</c>.</param>
        private sealed class SessionEnder(string connectionString, string statement) : SaveChangesInterceptor
        {
            /// <summary>Deletes the session's row, as a sign-out's prune would.</summary>
            public const string Delete = "DELETE FROM [idsvr].[Sessions] WHERE [Sid] = @sid;";

            /// <summary>Marks the session ended, as a logout does.</summary>
            public const string Terminate =
                "UPDATE [idsvr].[Sessions] SET [TerminatedUtc] = SYSDATETIMEOFFSET() WHERE [Sid] = @sid AND [TerminatedUtc] IS NULL;";

            /// <summary>Whether it is armed (1) or not (0).</summary>
            private int _armed;

            /// <summary>Whether it has ended a session (1) or not (0).</summary>
            private int _ended;

            /// <summary>Whether it ended a session.</summary>
            public bool Ended => Volatile.Read(ref _ended) == 1;

            /// <summary>Starts watching: the setup before the grant registers freely.</summary>
            public void Arm()
            {
                Volatile.Write(ref _armed, 1);
            }

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);
                IdentitySessionClient? registering = Volatile.Read(ref _armed) == 1 && eventData.Context is { } context
                    ? context.ChangeTracker.Entries<IdentitySessionClient>()
                        .Where(static entry => entry.State == EntityState.Added)
                        .Select(static entry => entry.Entity)
                        .FirstOrDefault()
                    : null;
                if (registering is not null && Interlocked.CompareExchange(ref _ended, 1, 0) == 0)
                {
                    // On a connection of its own, as the logout's or the prune's write would be.
                    await using SqlConnection connection = new(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await using SqlCommand command = new(statement, connection);
                    command.Parameters.AddWithValue("@sid", registering.Sid);
                    Assert.Equal(1, await command.ExecuteNonQueryAsync(cancellationToken));
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }

        /// <summary>
        ///     Once armed, holds the first transaction that commits a client registration until it is
        ///     released, with the registration written and its locks taken. A hold lasts at most 30
        ///     seconds, so a test that never releases it fails rather than hangs.
        /// </summary>
        private sealed class RegistrationHold : DbTransactionInterceptor
        {
            /// <summary>Completes when a registration's commit is held.</summary>
            private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Completes when the held commit may go on.</summary>
            private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>Whether it is armed (1) or not (0).</summary>
            private int _armed;

            /// <summary>Whether it has held a commit (1) or not (0).</summary>
            private int _holding;

            /// <summary>Completes when a registration's commit is held.</summary>
            public Task Held => _held.Task;

            /// <summary>Starts watching: the setup before the grant registers freely.</summary>
            public void Arm()
            {
                Volatile.Write(ref _armed, 1);
            }

            /// <summary>Lets the held commit go on.</summary>
            public void Release()
            {
                _released.TrySetResult();
            }

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
                DbTransaction transaction,
                TransactionEventData eventData,
                InterceptionResult result,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);

                // Only a commit whose context is registering a client, the first one.
                if (Volatile.Read(ref _armed) == 1
                    && eventData.Context is { } context
                    && context.ChangeTracker.Entries<IdentitySessionClient>().Any()
                    && Interlocked.CompareExchange(ref _holding, 1, 0) == 0)
                {
                    _held.TrySetResult();
                    await Task.WhenAny(_released.Task, Task.Delay(TimeSpan.FromSeconds(30), cancellationToken));
                }

                return await base.TransactionCommittingAsync(transaction, eventData, result, cancellationToken);
            }
        }

        /// <summary>
        ///     Lets another writer change an authorization just before every save that updates it,
        ///     so every such save loses on the authorization's concurrency token, and counts them.
        /// </summary>
        /// <param name="connectionString">The store's connection string.</param>
        private sealed class AuthorizationWriteSaboteur(string connectionString) : SaveChangesInterceptor
        {
            /// <summary>How many authorization updates were attempted.</summary>
            private int _attempts;

            /// <summary>How many authorization updates were attempted.</summary>
            public int Attempts => Volatile.Read(ref _attempts);

            /// <inheritdoc />
            public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
                DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(eventData);
                List<string> updated = eventData.Context is { } context
                    ? [.. context.ChangeTracker
                        .Entries<OpenIddictEntityFrameworkCoreAuthorization>()
                        .Where(static entry => entry.State == EntityState.Modified)
                        .Select(static entry => entry.Entity.Id!)]
                    : [];
                if (updated.Count > 0)
                {
                    Interlocked.Increment(ref _attempts);
                }

                foreach (string id in updated)
                {
                    // On a connection of its own, as a concurrent grant's write would be.
                    await using SqlConnection connection = new(connectionString);
                    await connection.OpenAsync(cancellationToken);
                    await using SqlCommand command = new(
                        "UPDATE [idsvr].[OpenIddictAuthorizations] SET [ConcurrencyToken] = CONVERT(nvarchar(50), NEWID()) WHERE [Id] = @id;",
                        connection);
                    command.Parameters.AddWithValue("@id", id);
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                return await base.SavingChangesAsync(eventData, result, cancellationToken);
            }
        }
    }
}
