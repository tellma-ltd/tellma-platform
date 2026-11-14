// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Net;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Explicit consent, end to end: a seeded client can require it, an authorization then stops
    ///     at the consent screen instead of returning a code, and the buttons on that screen decide
    ///     the outcome. Every provisioned client is first-party and implicit, so without a way to
    ///     configure this the consent view had no caller at all. Remembered consent is bound to the
    ///     places it was given for: another tenant, or the distribution as a whole, is asked again.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConsentTests(SqlServerFixture fixture)
    {
        /// <summary>The redirect URI the consent-requiring client is seeded with.</summary>
        private const string CallbackUri = "http://127.0.0.1/callback";

        /// <summary>The distribution onboarded by the sign-in, whose origin every native client is granted.</summary>
        private const string Origin = "https://acme.app.tellma.com";

        private const string Tenant17 = Origin + "/17/mcp";
        private const string Tenant18 = Origin + "/18/mcp";

        /// <summary>The scopes an agent asks for to call a distribution's API.</summary>
        private const string ApiScopes = "openid tellma_api";

        [Fact]
        public async Task A_client_requiring_consent_stops_at_the_consent_screen()
        {
            using StandaloneFactory factory = CreateFactory("idconsent");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "consent@example.com");

            using HttpResponseMessage response = await RequestConsentAsync(flow);

            // The consent view renders in place rather than redirecting back with a code.
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Third Party App", html, StringComparison.Ordinal);
            Assert.Contains("Authorize application", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Allowing_consent_returns_an_authorization_code()
        {
            using StandaloneFactory factory = CreateFactory("idallow");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "allow@example.com");

            using HttpResponseMessage consentPage = await RequestConsentAsync(flow);
            Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);

            // Post the form the way the browser does: every hidden protocol parameter the view
            // round-tripped, plus the Accept button's own name and value as the submitter.
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(consentPage);
            fields["submit.Accept"] = "yes";
            fields.Remove("submit.Deny");

            using HttpResponseMessage granted = await flow.PostFormAsync(action, fields);

            string body = await granted.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(
                granted.StatusCode == HttpStatusCode.Redirect,
                $"Expected a redirect back to the client, got {(int)granted.StatusCode}. Body:\n{body}");

            string location = granted.Headers.Location!.ToString();
            Assert.StartsWith(CallbackUri, location, StringComparison.Ordinal);
            Assert.Contains("code=", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_consent_page_allows_its_clients_callback_as_a_form_action()
        {
            using StandaloneFactory factory = CreateFactory("idcsp");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "csp@example.com");

            // A browser applies form-action to every hop of the navigation a submission produces,
            // so the redirect that completes the grant needs naming here or it is discarded — the
            // grant succeeds on the server and the page simply never moves.
            using HttpResponseMessage consentPage = await RequestConsentAsync(flow);
            string consentPolicy = Assert.Single(consentPage.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("form-action 'self' " + CallbackUri + ";", consentPolicy, StringComparison.Ordinal);

            // Only the page that needs it is widened; everything else keeps the bare directive.
            using HttpResponseMessage loginPage = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Login", UriKind.Relative), TestContext.Current.CancellationToken);
            string loginPolicy = Assert.Single(loginPage.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("form-action 'self';", loginPolicy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Denying_consent_returns_the_access_denied_error()
        {
            using StandaloneFactory factory = CreateFactory("iddeny");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "deny@example.com");

            using HttpResponseMessage consentPage = await RequestConsentAsync(flow);
            Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);

            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(consentPage);
            fields["submit.Deny"] = "no";
            fields.Remove("submit.Accept");

            using HttpResponseMessage denied = await flow.PostFormAsync(action, fields);

            string body = await denied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(
                denied.StatusCode == HttpStatusCode.Redirect,
                $"Expected the error redirect, got {(int)denied.StatusCode}. Body:\n{body}");

            string location = denied.Headers.Location!.ToString();
            Assert.StartsWith(CallbackUri, location, StringComparison.Ordinal);
            Assert.Contains("error=access_denied", location, StringComparison.Ordinal);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Accepting_without_a_session_sends_the_client_an_error()
        {
            using StandaloneFactory factory = CreateFactory("idnosession");
            using OidcFlowClient flow = factory.CreateFlowClient();

            // An anonymous but otherwise well-formed grant. The token is minted on a page loaded
            // without a session so it validates for an anonymous post, which is what puts the
            // request past the antiforgery filter and onto the branch under test.
            (_, Dictionary<string, string> fields) = await LoadAntiforgeryAsync(flow);
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            fields["client_id"] = "third-party";
            fields["response_type"] = "code";
            fields["redirect_uri"] = CallbackUri;
            fields["scope"] = "openid profile";
            fields["code_challenge"] = challenge;
            fields["code_challenge_method"] = "S256";
            fields["submit.Accept"] = "yes";

            using HttpResponseMessage response = await flow.PostFormAsync("/connect/authorize", fields);

            // The answer belongs back at the client. A challenge here would instead redirect to
            // the login page, and because these parameters ride in the body rather than the query
            // they would be dropped — returning the user to a bare authorize request that can only
            // fail, with nothing left to identify the client they were trying to reach.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location!.ToString();
            Assert.StartsWith(CallbackUri, location, StringComparison.Ordinal);
            Assert.Contains("error=login_required", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_first_party_grant_does_not_stand_in_for_consent()
        {
            using StandaloneFactory factory = CreateFactory("idscope");
            using OidcFlowClient flow = factory.CreateFlowClient();

            // Signing in runs a full authorization through the first-party client, which is
            // implicit and therefore records a permanent authorization of its own. Consent is
            // remembered by exactly such a row, so this is the shape that would let one client's
            // grant answer for another's if the lookup were not scoped to the client asking.
            await SignInAsync(factory, flow, "scope@example.com");

            using HttpResponseMessage response = await RequestConsentAsync(flow);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Authorize application", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Consent_is_remembered_for_the_next_authorization()
        {
            using StandaloneFactory factory = CreateFactory("idremember");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "remember@example.com");

            using (HttpResponseMessage consentPage = await RequestConsentAsync(flow))
            {
                (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(consentPage);
                fields["submit.Accept"] = "yes";
                fields.Remove("submit.Deny");
                using HttpResponseMessage granted = await flow.PostFormAsync(action, fields);
                Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            }

            // The grant it just recorded is what remembers the decision, so asking again goes
            // straight through rather than making the user re-approve the same client.
            using HttpResponseMessage again = await RequestConsentAsync(flow);
            Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
            Assert.Contains("code=", again.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_consent_screen_names_the_place_the_access_is_for()
        {
            using StandaloneFactory factory = CreateFactory("idconsenttarget");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "target@example.com");

            using HttpResponseMessage page = await RequestConsentAsync(flow, ApiScopes, Tenant17);

            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            string html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("acme.app.tellma.com/17", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Consent_for_one_tenant_does_not_cover_another()
        {
            using StandaloneFactory factory = CreateFactory("idconsenttenant");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "tenants@example.com");

            using (HttpResponseMessage consentPage = await RequestConsentAsync(flow, ApiScopes, Tenant17))
            {
                Assert.Equal(HttpStatusCode.OK, consentPage.StatusCode);
                using HttpResponseMessage granted = await AcceptAsync(flow, consentPage);
                Assert.Contains("code=", granted.Headers.Location!.ToString(), StringComparison.Ordinal);
            }

            // The same place is remembered...
            using (HttpResponseMessage same = await RequestConsentAsync(flow, ApiScopes, Tenant17))
            {
                Assert.Equal(HttpStatusCode.Redirect, same.StatusCode);
                Assert.Contains("code=", same.Headers.Location!.ToString(), StringComparison.Ordinal);
            }

            // ...but another tenant of the same distribution, and the distribution itself, are
            // places the user never saw: each is asked for again, naming the new place.
            foreach ((string resource, string place) in (ValueTuple<string, string>[])[(Tenant18, "acme.app.tellma.com/18"), (Origin, "acme.app.tellma.com")])
            {
                using HttpResponseMessage other = await RequestConsentAsync(flow, ApiScopes, resource);
                Assert.Equal(HttpStatusCode.OK, other.StatusCode);
                string html = await other.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
                Assert.Contains("Authorize application", html, StringComparison.Ordinal);
                Assert.Contains(place, html, StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_silent_request_for_an_unconsented_place_answers_consent_required()
        {
            using StandaloneFactory factory = CreateFactory("idconsentnone");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "silent@example.com");

            using (HttpResponseMessage consentPage = await RequestConsentAsync(flow, ApiScopes, Tenant17))
            {
                using HttpResponseMessage granted = await AcceptAsync(flow, consentPage);
                Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            }

            using (HttpResponseMessage same = await RequestConsentAsync(flow, ApiScopes, Tenant17, prompt: "none"))
            {
                Assert.Contains("code=", same.Headers.Location!.ToString(), StringComparison.Ordinal);
            }

            using HttpResponseMessage other = await RequestConsentAsync(flow, ApiScopes, Tenant18, prompt: "none");
            Assert.Equal(HttpStatusCode.Redirect, other.StatusCode);
            string location = other.Headers.Location!.ToString();
            Assert.StartsWith(CallbackUri, location, StringComparison.Ordinal);
            Assert.Contains("error=consent_required", location, StringComparison.Ordinal);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Consenting_to_a_second_place_extends_the_one_authorization()
        {
            using StandaloneFactory factory = CreateFactory("idconsentextend");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, "extend@example.com");

            foreach (string resource in (string[])[Tenant17, Tenant18])
            {
                using HttpResponseMessage consentPage = await RequestConsentAsync(flow, ApiScopes, resource);
                using HttpResponseMessage granted = await AcceptAsync(flow, consentPage);
                Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            }

            // One authorization per client and session is what global logout revokes, so the
            // second consent is recorded on the first authorization rather than beside it.
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object client = (await applications.FindByClientIdAsync("third-party", TestContext.Current.CancellationToken))!;
            string clientObjectId = (await applications.GetIdAsync(client, TestContext.Current.CancellationToken))!;
            List<object> rows = [];
            await foreach (object row in authorizations.FindByApplicationIdAsync(clientObjectId, TestContext.Current.CancellationToken))
            {
                rows.Add(row);
            }

            object authorization = Assert.Single(rows);
            Assert.Equal(
                [Tenant17, Tenant18],
                AuthorizationResources.Read(await authorizations.GetPropertiesAsync(authorization, TestContext.Current.CancellationToken)));
        }

        /// <summary>
        ///     Boots a host that seeds one consent-requiring third-party client alongside the
        ///     first-party distribution the sign-in itself runs through.
        /// </summary>
        private StandaloneFactory CreateFactory(string databasePrefix)
        {
            return DatabaseBackedFactory.CreateStandalone(
                fixture,
                databasePrefix,
                new Dictionary<string, string?>
                {
                    ["TellmaIdentity:Seed:Clients:0:ClientId"] = "third-party",
                    ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Third Party App",
                    ["TellmaIdentity:Seed:Clients:0:Kind"] = "Native",
                    ["TellmaIdentity:Seed:Clients:0:RequireConsent"] = "true",
                    ["TellmaIdentity:Seed:Clients:0:ThirdParty"] = "true",
                    ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = CallbackUri,
                });
        }

        /// <summary>Loads a page anonymously to obtain a matching antiforgery cookie and token.</summary>
        /// <param name="flow">The browser-role client whose cookie jar receives the pair.</param>
        /// <returns>The form's action and its fields, the antiforgery token among them.</returns>
        private static async Task<(string Action, Dictionary<string, string> Fields)> LoadAntiforgeryAsync(
            OidcFlowClient flow)
        {
            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Login", UriKind.Relative), TestContext.Current.CancellationToken);
            return await OidcFlowClient.ParseFormAsync(page);
        }

        /// <summary>Establishes an SSO session through the first-party client, which stays implicit.</summary>
        private static async Task SignInAsync(StandaloneFactory factory, OidcFlowClient flow, string email)
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, email);

            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });

            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri("/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri), UriKind.Relative),
                TestContext.Current.CancellationToken))
            {
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync(email, loginUrl);
            using (await flow.Browser.GetAsync(new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken))
            {
            }
        }

        /// <summary>
        ///     Starts an authorization for the consent-requiring client on the established session,
        ///     optionally naming a resource and a prompt.
        /// </summary>
        private static async Task<HttpResponseMessage> RequestConsentAsync(
            OidcFlowClient flow, string scope = "openid profile", string? resource = null, string? prompt = null)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string url = "/connect/authorize?client_id=third-party&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(CallbackUri)
                + "&scope=" + Uri.EscapeDataString(scope)
                + "&code_challenge=" + challenge + "&code_challenge_method=S256";
            if (resource is not null)
            {
                url += "&resource=" + Uri.EscapeDataString(resource);
            }

            if (prompt is not null)
            {
                url += "&prompt=" + prompt;
            }

            return await flow.Browser.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        }

        /// <summary>Submits a consent page's Accept button the way the browser does.</summary>
        private static async Task<HttpResponseMessage> AcceptAsync(OidcFlowClient flow, HttpResponseMessage consentPage)
        {
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(consentPage);
            fields["submit.Accept"] = "yes";
            fields.Remove("submit.Deny");
            return await flow.PostFormAsync(action, fields);
        }
    }
}
