// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.AspNetCore;
using Tellma.Distro.Acme.IntegrationTests.Infrastructure;
using Tellma.Identity;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.IntegrationTests
{
    /// <summary>
    ///     Acme's web host over a migrated deployment: a user signs in through the BFF with the
    ///     identity engine in process, the engine's global sign-out reaches the host's session over
    ///     the back channel, a refresh the engine rejects ends the session, a host whose identity store
    ///     is behind refuses to start, and a host started before its catalog exists turns ready once
    ///     it does.
    /// </summary>
    [Trait("Category", "Integration")]
    public sealed class WebHostTests(SqlServerFixture sql)
    {
        /// <summary>The administrator Acme's Development configuration seeds in the identity engine.</summary>
        private const string Administrator = "admin@localhost";

        /// <summary>The identity server a deployment with the engine elsewhere names.</summary>
        private const string Authority = "https://identity.example.com";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task A_user_signs_in_through_the_BFF_with_the_engine_in_process()
        {
            AcmeMigrator acme = new(sql, "acme_signin");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            using OidcFlowClient flow = host.CreateFlowClient();

            await SignInAsync(flow);

            using HttpResponseMessage user = await flow.Browser.GetAsync(new Uri("/bff/user", UriKind.Relative), Token);
            string body = await user.Content.ReadAsStringAsync(Token);
            Assert.True(user.StatusCode == HttpStatusCode.OK, body);
            using var document = JsonDocument.Parse(body);
            Assert.Equal(Administrator, document.RootElement.GetProperty("email").GetString());
        }

        [Fact]
        public async Task A_global_sign_out_at_the_engine_ends_the_host_s_session_over_the_back_channel()
        {
            AcmeMigrator acme = new(sql, "acme_bclogout");
            _ = await acme.MigrateAsync();
            await using AcmeHost host = await AcmeHost.StartAsync(acme.Values);
            using OidcFlowClient flow = host.CreateFlowClient();
            await SignInAsync(flow);

            // The engine posts its logout token to the host's receiver, which ends the session.
            await SignOutAtTheEngineAsync(flow);

            using HttpResponseMessage user = await flow.Browser.GetAsync(new Uri("/bff/user", UriKind.Relative), Token);
            Assert.True(user.StatusCode == HttpStatusCode.Unauthorized, host.Warnings.ToString());
        }

        [Fact]
        public async Task A_refresh_the_engine_rejects_ends_the_session()
        {
            AcmeMigrator acme = new(sql, "acme_refresh");
            _ = await acme.MigrateAsync();

            // Every request refreshes, and the engine's logout tokens never reach the host, so only
            // the refresh can learn that the engine revoked the grant.
            Dictionary<string, string?> values = new(acme.Values) { ["Tellma:Session:RefreshSkew"] = "1.00:00:00" };
            await using AcmeHost host = await AcmeHost.StartAsync(values);
            host.BackchannelLogoutSink = new DiscardingHandler();
            using OidcFlowClient flow = host.CreateFlowClient();
            await SignInAsync(flow);

            using (HttpResponseMessage refreshed = await flow.Browser.GetAsync(new Uri("/bff/user", UriKind.Relative), Token))
            {
                Assert.Equal(HttpStatusCode.OK, refreshed.StatusCode);
            }

            await SignOutAtTheEngineAsync(flow);

            using HttpResponseMessage rejected = await flow.Browser.GetAsync(new Uri("/bff/user", UriKind.Relative), Token);
            Assert.True(rejected.StatusCode == HttpStatusCode.Unauthorized, host.Warnings.ToString());
        }

        [Fact]
        public async Task A_host_whose_identity_store_lacks_the_engine_s_last_migration_fails_startup_naming_the_migrate_command()
        {
            AcmeMigrator acme = new(sql, "acme_idbehind");
            _ = await acme.MigrateAsync();
            await SqlServerFixture.ExecuteAsync(
                acme.CatalogConnectionString,
                $"DELETE FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = N'{TellmaIdentityConstants.LatestMigration}';");

            var host = AcmeHost.Build(acme.Values);
            await using (host)
            {
                Exception failure = await Assert.ThrowsAnyAsync<Exception>(host.StartAsync);
                string message = Describe(failure);
                Assert.Contains("dotnet run --project distributions/acme/src/Tellma.Distro.Acme.Migrator -- migrate", message, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_host_started_before_its_catalog_exists_turns_ready_once_the_catalog_is_migrated()
        {
            AcmeMigrator acme = new(sql, "acme_ready");
            string keyRing = Path.Combine(Path.GetTempPath(), "tellma-acme-keys-" + Guid.NewGuid().ToString("N"));
            Dictionary<string, string?> values = new(acme.Values)
            {
                // A deployment's own settings: the engine elsewhere, a persistent key ring, and a
                // snapshot refreshed every second so the test need not wait long.
                ["Tellma:Identity:Mode"] = "Standalone",
                ["Tellma:Identity:Authority"] = Authority,
                ["Tellma:Identity:ClientSecret"] = "test-client-secret",
                ["Tellma:Identity:ServiceClientSecret"] = "test-service-client-secret",
                ["Tellma:DataProtection:KeyRingPath"] = keyRing,
                ["Tellma:Catalog:RefreshInterval"] = "00:00:01",

                // Outside Development every connection requires encryption, and the suite's
                // container is reached with a SQL login, which a deployment allows explicitly.
                ["Tellma:Catalog:ConnectionString"] = Encrypted(acme.Values["Tellma:Catalog:ConnectionString"]),
                ["Tellma:Sql:Profiles:default"] = Encrypted(acme.Values["Tellma:Sql:Profiles:default"]),
                ["Tellma:Sql:AllowSqlPassword"] = "true",

                // A deployment lists its tenant servers; this test serves no tenant, so any host does.
                ["Tellma:Sql:AllowedServers:0"] = "localhost",
            };

            try
            {
                // The engine runs elsewhere, so its metadata stands in as already read: only the
                // catalog decides readiness here.
                await using AcmeHost host = await AcmeHost.StartAsync(values, "Production", static services => services.Configure<JwtBearerOptions>(
                    TellmaAuthentication.BearerScheme, static bearer => bearer.Configuration = new OpenIdConnectConfiguration { Issuer = Authority }));
                using HttpClient client = host.Server.CreateClient();

                using (HttpResponseMessage before = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token))
                {
                    Assert.Equal(HttpStatusCode.ServiceUnavailable, before.StatusCode);
                }

                MigratorResult migrated = await acme.RunAsync("migrate", "--catalog-only");
                Assert.True(migrated.ExitCode == 0, migrated.ToString());

                // The next refresh finds the catalog.
                HttpStatusCode status = HttpStatusCode.ServiceUnavailable;
                for (int attempt = 0; attempt < 60 && status != HttpStatusCode.OK; attempt++)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(500), Token);
                    using HttpResponseMessage after = await client.GetAsync(new Uri("/health/ready", UriKind.Relative), Token);
                    status = after.StatusCode;
                }

                Assert.Equal(HttpStatusCode.OK, status);
            }
            finally
            {
                if (Directory.Exists(keyRing))
                {
                    Directory.Delete(keyRing, recursive: true);
                }
            }
        }

        /// <summary>
        ///     Signs the administrator in through the BFF: the login's redirect to the engine, the
        ///     engine's email-code sign-in, and the authorization response delivered to the host's
        ///     callback, which starts the session.
        /// </summary>
        /// <param name="flow">The browser.</param>
        /// <returns>A task that completes when the browser holds a session.</returns>
        private static async Task SignInAsync(OidcFlowClient flow)
        {
            // --- The BFF's login pushes the request and sends the browser to the engine ---
            string authorize;
            using (HttpResponseMessage login = await flow.Browser.GetAsync(new Uri("/bff/login", UriKind.Relative), Token))
            {
                Assert.True(login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync(Token));
                authorize = login.Headers.Location!.ToString();
            }

            string loginPage;
            using (HttpResponseMessage challenge = await flow.Browser.GetAsync(new Uri(authorize, UriKind.RelativeOrAbsolute), Token))
            {
                Assert.True(challenge.StatusCode == HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
                loginPage = challenge.Headers.Location!.ToString();
            }

            // --- The engine signs the user in, then answers the authorization request ---
            string returned = await flow.SignInWithEmailCodeAsync(Administrator, loginPage);
            using HttpResponseMessage answer = await flow.Browser.GetAsync(new Uri(returned, UriKind.RelativeOrAbsolute), Token);

            // --- The callback: a redirect to it, or a form the browser posts to it ---
            using HttpResponseMessage callback = answer.StatusCode == HttpStatusCode.Redirect
                ? await flow.Browser.GetAsync(answer.Headers.Location!, Token)
                : await PostAsync(flow, answer);
            Assert.True(callback.StatusCode == HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync(Token));
            Assert.DoesNotContain("/bff/login", callback.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        /// <summary>Posts the form a page carries, as a browser posts an authorization response.</summary>
        /// <param name="flow">The browser.</param>
        /// <param name="page">The page.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> PostAsync(OidcFlowClient flow, HttpResponseMessage page)
        {
            Assert.True(page.StatusCode == HttpStatusCode.OK, await page.Content.ReadAsStringAsync(Token));
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page, "form");
            return await flow.PostFormAsync(action, fields);
        }

        /// <summary>Signs the browser out at the engine, which ends its session there and tells every client.</summary>
        /// <param name="flow">The browser.</param>
        /// <returns>A task that completes when the engine answered.</returns>
        private static async Task SignOutAtTheEngineAsync(OidcFlowClient flow)
        {
            Uri logout = new("/id/Identity/Account/Logout", UriKind.Relative);
            Dictionary<string, string> fields;
            using (HttpResponseMessage page = await flow.Browser.GetAsync(logout, Token))
            {
                (_, fields) = await OidcFlowClient.ParseFormAsync(page);
            }

            using HttpResponseMessage signedOut = await flow.Browser.PostAsync(logout, new FormUrlEncodedContent(fields), Token);
            Assert.True(signedOut.StatusCode == HttpStatusCode.Redirect, await signedOut.Content.ReadAsStringAsync(Token));
        }

        /// <summary>A connection string that requires encryption, trusting the test server's certificate.</summary>
        /// <param name="connectionString">The connection string.</param>
        /// <returns>The encrypted one.</returns>
        private static string Encrypted(string? connectionString)
        {
            return new SqlConnectionStringBuilder(connectionString)
            {
                Encrypt = SqlConnectionEncryptOption.Mandatory,
                TrustServerCertificate = true,
            }.ConnectionString;
        }

        /// <summary>A failure's messages, its inner failures' and its composition problems' included.</summary>
        /// <param name="failure">The failure.</param>
        /// <returns>The text.</returns>
        private static string Describe(Exception failure)
        {
            List<string> parts = [];
            for (Exception? current = failure; current is not null; current = current.InnerException)
            {
                parts.Add(current.Message);
                if (current is TellmaCompositionException composition)
                {
                    parts.AddRange(composition.Problems.Select(static problem => problem.Problem + " " + problem.Fix));
                }
            }

            return string.Join('\n', parts);
        }

        /// <summary>Accepts every request and delivers it nowhere.</summary>
        private sealed class DiscardingHandler : HttpMessageHandler
        {
            /// <inheritdoc />
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            }
        }
    }
}
