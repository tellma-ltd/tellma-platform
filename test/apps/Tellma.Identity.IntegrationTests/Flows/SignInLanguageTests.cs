// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using AngleSharp.Dom;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using System.Net;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The language a client asks for with <c>ui_locales</c>. An authorization request states
    ///     it once — on the URL, or inside a pushed request the browser only holds a reference
    ///     to — and every page between that request and the client's callback has to honour it:
    ///     the sign-in page, the steps after it, and consent.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SignInLanguageTests(SqlServerFixture fixture)
    {
        /// <summary>The redirect URI the query-request client is seeded with.</summary>
        private const string CallbackUri = "http://127.0.0.1/callback";

        /// <summary>The redirect URI of the provisioned first-party client, which must push its requests.</summary>
        private const string BffCallbackUri = "https://acme.app.tellma.com/signin-oidc";

        /// <summary>
        ///     A client that may put its request on the URL and that asks for consent, and an
        ///     external provider so the sign-in page has a provider button to follow.
        /// </summary>
        private static readonly Dictionary<string, string?> Seed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "third-party",
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Third Party App",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Native",
            ["TellmaIdentity:Seed:Clients:0:RequireConsent"] = "true",
            ["TellmaIdentity:Seed:Clients:0:ThirdParty"] = "true",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = CallbackUri,
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
        };

        [Fact]
        public async Task The_sign_in_redirect_carries_the_language_of_a_request_on_the_url()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idlangquery", Seed);
            using OidcFlowClient flow = factory.CreateFlowClient();

            // A preference list, passed on whole: the sign-in page picks the first it offers.
            string authorizeUrl = QueryAuthorizeUrl("fr ar en");
            using HttpResponseMessage challenge = await GetAsync(flow, authorizeUrl);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

            string loginUrl = challenge.Headers.Location!.ToString();
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(loginUrl);
            Assert.Equal("/Identity/Account/Login", path);
            Assert.Equal("fr ar en", (string?)query["ui_locales"]);
            Assert.Equal(authorizeUrl, (string?)query["returnUrl"]);

            using (HttpResponseMessage loginPage = await GetAsync(flow, loginUrl))
            {
                IDocument login = await HtmlPage.ReadAsync(loginPage);
                Assert.Equal("ar", login.DocumentElement.GetAttribute("lang"));
                Assert.Equal("rtl", login.DocumentElement.GetAttribute("dir"));
            }

            // A request that names no language adds none, and the page falls back as it always has.
            using (HttpResponseMessage plain = await GetAsync(flow, QueryAuthorizeUrl(uiLocales: null)))
            {
                string plainLogin = plain.Headers.Location!.ToString();
                Assert.False(HtmlPage.Split(plainLogin).Query.ContainsKey("ui_locales"));

                using HttpResponseMessage loginPage = await GetAsync(flow, plainLogin);
                Assert.Equal("en", (await HtmlPage.ReadAsync(loginPage)).DocumentElement.GetAttribute("lang"));
            }

            // So does one whose language is not a language tag at all.
            using (HttpResponseMessage malformed = await GetAsync(flow, QueryAuthorizeUrl("<script>alert(1)</script>")))
            {
                Assert.False(HtmlPage.Split(malformed.Headers.Location!.ToString()).Query.ContainsKey("ui_locales"));
            }

            // A language the deployment does not offer is passed on and simply selects nothing.
            using (HttpResponseMessage unoffered = await GetAsync(flow, QueryAuthorizeUrl("fr")))
            {
                string unofferedLogin = unoffered.Headers.Location!.ToString();
                Assert.Equal("fr", (string?)HtmlPage.Split(unofferedLogin).Query["ui_locales"]);

                using HttpResponseMessage loginPage = await GetAsync(flow, unofferedLogin);
                Assert.Equal("en", (await HtmlPage.ReadAsync(loginPage)).DocumentElement.GetAttribute("lang"));
            }
        }

        [Fact]
        public async Task A_pushed_requests_language_holds_from_the_sign_in_page_to_the_callback()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idlangpar", Seed);
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "arabic@example.com");
            using OidcFlowClient flow = factory.CreateFlowClient();

            // The language rides inside the pushed request; the browser's URL names only the
            // client and the reference.
            string authorizeUrl = await PushAsync(flow, distribution, uiLocales: "ar-SA en");
            Assert.DoesNotContain("ui_locales", authorizeUrl, StringComparison.Ordinal);

            using HttpResponseMessage challenge = await GetAsync(flow, authorizeUrl);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
            string loginUrl = challenge.Headers.Location!.ToString();
            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(loginUrl);
            Assert.Equal("/Identity/Account/Login", path);
            Assert.Equal("ar-SA en", (string?)query["ui_locales"]);
            Assert.Equal(authorizeUrl, (string?)query["returnUrl"]);

            // The sign-in page: Arabic, and every way off it to another sign-in page says so.
            using HttpResponseMessage loginPage = await GetAsync(flow, loginUrl);
            await AssertArabicAsync(loginPage, minimumAddresses: 2);

            // Asking for a code moves to the code page — a redirect a page handler makes.
            int delivered = factory.Emails.CountFor("arabic@example.com");
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(loginPage);
            fields["Email"] = "arabic@example.com";
            using HttpResponseMessage codeRedirect = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, codeRedirect.StatusCode);

            string codeUrl = codeRedirect.Headers.Location!.ToString();
            (string codePath, Dictionary<string, StringValues> codeQuery) = HtmlPage.Split(codeUrl);
            Assert.Equal("/Identity/Account/EmailCode", codePath);
            Assert.Equal("ar-SA en", (string?)codeQuery["ui_locales"]);

            // The code page: Arabic too, its submit and its resend button included.
            using HttpResponseMessage codePage = await GetAsync(flow, codeUrl);
            await AssertArabicAsync(codePage, minimumAddresses: 2);

            // Entering the code returns to the authorization endpoint with the request exactly
            // as the client pushed it: nothing the pages carried is added to it.
            string code = await factory.Emails.WaitForCodeAfterAsync("arabic@example.com", delivered);
            (string verifyAction, Dictionary<string, string> verifyFields) = await OidcFlowClient.ParseFormAsync(codePage);
            verifyFields["Code"] = code;
            using HttpResponseMessage signedIn = await flow.PostFormAsync(verifyAction, verifyFields);
            Assert.Equal(HttpStatusCode.Redirect, signedIn.StatusCode);
            Assert.Equal(authorizeUrl, signedIn.Headers.Location!.ToString());

            using HttpResponseMessage authorized = await GetAsync(flow, authorizeUrl);
            Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
            string callback = authorized.Headers.Location!.ToString();
            Assert.StartsWith(BffCallbackUri, callback, StringComparison.Ordinal);
            Assert.Contains("code=", callback, StringComparison.Ordinal);
            Assert.DoesNotContain("ui_locales", callback, StringComparison.Ordinal);

            // A later request that demands a fresh sign-in is a step-up on the session just
            // made, and it is sent to sign in under the language it asks for itself.
            string stepUpUrl = await PushAsync(flow, distribution, uiLocales: "ar", prompt: "login");
            using HttpResponseMessage stepUp = await GetAsync(flow, stepUpUrl);
            Assert.Equal(HttpStatusCode.Redirect, stepUp.StatusCode);
            (string stepUpPath, Dictionary<string, StringValues> stepUpQuery) = HtmlPage.Split(stepUp.Headers.Location!.ToString());
            Assert.Equal("/Identity/Account/Login", stepUpPath);
            Assert.Equal("true", (string?)stepUpQuery["stepUp"]);
            Assert.Equal("ar", (string?)stepUpQuery["ui_locales"]);

            using HttpResponseMessage stepUpPage = await GetAsync(flow, stepUp.Headers.Location!.ToString());
            await AssertArabicAsync(stepUpPage, minimumAddresses: 1);
        }

        [Fact]
        public async Task The_consent_page_follows_the_requests_language_until_the_user_chooses_another()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idlangconsent", Seed);
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "consent-lang@example.com");
            await RequireConsentAsync(factory, "acme");

            using OidcFlowClient flow = factory.CreateFlowClient();
            await flow.SignInWithEmailCodeAsync("consent-lang@example.com", "/Identity/Account/Login");

            // A pushed request: nothing on the URL names the language, and the page follows the
            // request all the same.
            string pushedUrl = await PushAsync(flow, distribution, uiLocales: "ar");
            using (HttpResponseMessage consent = await GetAsync(flow, pushedUrl))
            {
                Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
                IDocument page = await HtmlPage.ReadAsync(consent);
                Assert.Equal("ar", page.DocumentElement.GetAttribute("lang"));
                Assert.Equal("rtl", page.DocumentElement.GetAttribute("dir"));
            }

            // A request on the URL: the same, by the URL's own value.
            string queryUrl = QueryAuthorizeUrl("ar");
            using (HttpResponseMessage consent = await GetAsync(flow, queryUrl))
            {
                Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
                Assert.Equal("ar", (await HtmlPage.ReadAsync(consent)).DocumentElement.GetAttribute("lang"));

                // The user switches to English on that page. The request comes back asking for
                // English: the choice replaces the client's, and nothing else about it changes.
                (string action, Dictionary<string, string> fields) =
                    await OidcFlowClient.ParseFormAsync(consent, "form.tmi-lang-menu");
                fields["culture"] = "en";
                using HttpResponseMessage switched = await flow.PostFormAsync(action, fields);
                Assert.Equal(HttpStatusCode.Redirect, switched.StatusCode);
                Assert.Equal(QueryAuthorizeUrl("en"), switched.Headers.Location!.ToString());

                using HttpResponseMessage english = await GetAsync(flow, switched.Headers.Location!.ToString());
                Assert.Equal(HttpStatusCode.OK, english.StatusCode);
                Assert.Equal("en", (await HtmlPage.ReadAsync(english)).DocumentElement.GetAttribute("lang"));
            }

            // The choice is now the browser's own, and a pushed request does not override it: the
            // switch on its consent page has no URL value to replace, so it must be able to win.
            using (HttpResponseMessage consent = await GetAsync(flow, pushedUrl))
            {
                Assert.Equal(HttpStatusCode.OK, consent.StatusCode);
                Assert.Equal("en", (await HtmlPage.ReadAsync(consent)).DocumentElement.GetAttribute("lang"));
            }
        }

        [Fact]
        public async Task An_external_sign_in_returns_to_a_page_in_the_same_language()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idlangfed", Seed);
            using OidcFlowClient flow = factory.CreateFlowClient();

            using HttpResponseMessage loginPage = await GetAsync(
                flow, "/Identity/Account/Login?returnUrl=%2FIdentity%2FManage&ui_locales=ar");

            // The provider's button posts to the external-login page, in the language.
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(
                loginPage, "form[action*='provider=Google']");
            Assert.Equal("ar", (string?)HtmlPage.Split(action).Query["ui_locales"]);

            using HttpResponseMessage challenge = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

            // Where the provider sends the browser back is sealed inside the challenge's state.
            // Opened with the handler's own key, it names the callback page — with the language,
            // so a refusal there, or the next step, is shown the way the sign-in page was.
            string state = (string?)QueryHelpers.ParseQuery(challenge.Headers.Location!.Query)["state"]
                ?? throw new InvalidOperationException("The challenge carries no state.");
            GoogleOptions google = factory.Services
                .GetRequiredService<IOptionsMonitor<GoogleOptions>>()
                .Get(GoogleDefaults.AuthenticationScheme);
            AuthenticationProperties properties = google.StateDataFormat.Unprotect(state)
                ?? throw new InvalidOperationException("The challenge state cannot be read.");

            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(properties.RedirectUri!);
            Assert.Equal("/Identity/Account/ExternalLogin", path);
            Assert.Equal("Callback", (string?)query["handler"]);
            Assert.Equal("/Identity/Manage", (string?)query["returnUrl"]);
            Assert.Equal("ar", (string?)query["ui_locales"]);
        }

        /// <summary>Builds an authorization request that states everything on the URL.</summary>
        /// <param name="uiLocales">The language list to ask for, or null to ask for none.</param>
        /// <returns>The authorization URL.</returns>
        private static string QueryAuthorizeUrl(string? uiLocales)
        {
            // A fixed challenge: these requests are never redeemed, only begun.
            string url = "/connect/authorize?client_id=third-party&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(CallbackUri)
                + "&scope=openid&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256";
            return uiLocales is null ? url : url + "&ui_locales=" + Uri.EscapeDataString(uiLocales);
        }

        /// <summary>Pushes an authorization request for the first-party client.</summary>
        /// <param name="flow">The protocol driver.</param>
        /// <param name="distribution">The client's credentials.</param>
        /// <param name="uiLocales">The language list the request asks for.</param>
        /// <param name="prompt">The request's <c>prompt</c>, when it has one.</param>
        /// <returns>The authorization URL the browser opens: the client and the reference only.</returns>
        private static async Task<string> PushAsync(
            OidcFlowClient flow, DistributionClientCredentials distribution, string uiLocales, string? prompt = null)
        {
            (string _, string challenge) = OidcFlowClient.CreatePkcePair();
            Dictionary<string, string> parameters = new()
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = BffCallbackUri,
                ["response_type"] = "code",
                ["scope"] = "openid",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["ui_locales"] = uiLocales,
            };

            if (prompt is not null)
            {
                parameters["prompt"] = prompt;
            }

            string requestUri = await flow.PushAuthorizationRequestAsync(parameters);
            return "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
        }

        /// <summary>Makes a registered client ask for consent, as a third-party client does.</summary>
        /// <param name="factory">The host under test.</param>
        /// <param name="clientId">The client.</param>
        private static async Task RequireConsentAsync(StandaloneFactory factory, string clientId)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();

            object application = await manager.FindByClientIdAsync(clientId, TestContext.Current.CancellationToken)
                ?? throw new InvalidOperationException($"The client '{clientId}' is not registered.");
            OpenIddictApplicationDescriptor descriptor = new();
            await manager.PopulateAsync(descriptor, application, TestContext.Current.CancellationToken);
            descriptor.ConsentType = ConsentTypes.Explicit;
            await manager.UpdateAsync(application, descriptor, TestContext.Current.CancellationToken);
        }

        /// <summary>Requests an address with the browser, following nothing.</summary>
        /// <param name="flow">The browser.</param>
        /// <param name="address">The address, relative or on the test host.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> GetAsync(OidcFlowClient flow, string address)
        {
            return flow.Browser.GetAsync(new Uri(address, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Asserts that a sign-in page rendered in Arabic, right to left, and that every
        ///     address on it leading to another sign-in page keeps the language.
        /// </summary>
        /// <param name="response">The page response.</param>
        /// <param name="minimumAddresses">How many such addresses the page is known to render at least.</param>
        private static async Task AssertArabicAsync(HttpResponseMessage response, int minimumAddresses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            IDocument page = await HtmlPage.ReadAsync(response);
            Assert.Equal("ar", page.DocumentElement.GetAttribute("lang"));
            Assert.Equal("rtl", page.DocumentElement.GetAttribute("dir"));

            IReadOnlyList<string> addresses = HtmlPage.IdentityPageAddresses(page);
            Assert.True(addresses.Count >= minimumAddresses, $"The page renders {addresses.Count} internal addresses.");
            Assert.All(addresses, static address => Assert.StartsWith(
                "ar", (string?)HtmlPage.Split(address).Query.GetValueOrDefault("ui_locales"), StringComparison.Ordinal));
        }
    }
}
