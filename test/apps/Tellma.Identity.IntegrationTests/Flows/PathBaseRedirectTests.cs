// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using System.Net;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Mounted in a host under a path base, every address the engine redirects the browser to
    ///     on its own account stays under that path base: the sign-in page an authorization request
    ///     sends the browser to, the page an email-code sign-in lands on when it has nowhere to
    ///     return to, and the logged-out page an end-session request ends on. An address that left
    ///     the path base would reach the host's own routes instead of the engine's pages.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class PathBaseRedirectTests(SqlServerFixture fixture)
    {
        /// <summary>The origin of the distribution the engine is mounted in.</summary>
        private const string Origin = "http://localhost";

        /// <summary>The distribution's BFF client secret, from its seed entry.</summary>
        private const string BffSecret = "acme-bff-secret-0123456789abcdef";

        /// <summary>The distribution's registered sign-in callback.</summary>
        private const string RedirectUri = Origin + "/signin-oidc";

        /// <summary>The distribution the engine serves, from its seed entry.</summary>
        private static readonly Dictionary<string, string?> DistributionSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "acme",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Distribution",
            ["TellmaIdentity:Seed:Clients:0:Origin"] = Origin,
            ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = BffSecret,
            ["TellmaIdentity:Seed:Clients:0:ServiceClientSecret"] = "acme-svc-secret-0123456789abcdef",
        };

        [Fact]
        public async Task An_authorization_request_sends_the_browser_to_sign_in_under_the_path_base_and_completes()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idpbauthz", DistributionSeed);
            await TestData.CreateActiveUserAsync(factory, "paula@example.com");
            using OidcFlowClient flow = factory.CreateFlowClient();

            (string _, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = BffSecret,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });

            // The challenge names the sign-in page under the path base, and returns to the
            // authorization request under it too.
            string authorize = "/id/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
            string loginUrl;
            using (HttpResponseMessage challenged = await GetAsync(flow, authorize))
            {
                Assert.Equal(HttpStatusCode.Redirect, challenged.StatusCode);
                loginUrl = challenged.Headers.Location!.ToString();
            }

            (string path, Dictionary<string, StringValues> query) = HtmlPage.Split(loginUrl);
            Assert.Equal("/id/Identity/Account/Login", path);
            Assert.Equal(authorize, (string?)query["returnUrl"]);

            // Signing in there returns to the request, which then completes at the client.
            string returnUrl = await flow.SignInWithEmailCodeAsync("paula@example.com", loginUrl);
            Assert.Equal(authorize, returnUrl);

            using HttpResponseMessage authorized = await GetAsync(flow, returnUrl);
            Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
            Uri callback = authorized.Headers.Location!;
            Assert.StartsWith(RedirectUri + "?", callback.AbsoluteUri, StringComparison.Ordinal);
            Assert.False(string.IsNullOrEmpty((string?)QueryHelpers.ParseQuery(callback.Query)["code"]), "No code was issued.");
        }

        [Fact]
        public async Task An_email_code_sign_in_with_nowhere_to_return_lands_on_the_sign_in_page_under_the_path_base()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idpbcode");
            await TestData.CreateActiveUserAsync(factory, "quinn@example.com");
            using OidcFlowClient flow = factory.CreateFlowClient();

            // A sign-in started on the page itself carries no return address, so the code entry
            // falls back to the sign-in page.
            string landed = await flow.SignInWithEmailCodeAsync("quinn@example.com", "/id/Identity/Account/Login");
            Assert.Equal("/id/Identity/Account/Login", landed);

            // Which is the engine's page, and sends a signed-in user on to the account pages.
            using HttpResponseMessage onward = await GetAsync(flow, landed);
            Assert.Equal(HttpStatusCode.Redirect, onward.StatusCode);
            Assert.Equal("/id/Identity/Manage", onward.Headers.Location!.ToString());
        }

        [Fact]
        public async Task An_end_session_request_with_nowhere_to_return_lands_on_the_logged_out_page_under_the_path_base()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idpbend");
            await TestData.CreateActiveUserAsync(factory, "rory@example.com");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await flow.SignInWithEmailCodeAsync("rory@example.com", "/id/Identity/Account/Login");

            // A request with no hint asks first; the user's confirmation signs out.
            using HttpResponseMessage confirm = await GetAsync(flow, "/id/connect/endsession");
            Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(confirm);
            fields["submit.SignOut"] = "yes";

            using HttpResponseMessage signedOut = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, signedOut.StatusCode);

            // With no post-logout address to return to, the logged-out page is where it ends,
            // under the path base, and it is there.
            string loggedOut = signedOut.Headers.Location!.ToString();
            Assert.Equal("/id/Identity/Account/LoggedOut", HtmlPage.Split(loggedOut).Path);
            using (HttpResponseMessage page = await GetAsync(flow, loggedOut))
            {
                Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            }

            // And the session is over: the account pages challenge again.
            using HttpResponseMessage manage = await GetAsync(flow, "/id/Identity/Manage");
            Assert.Equal(HttpStatusCode.Redirect, manage.StatusCode);
            Assert.Equal("/id/Identity/Account/Login", HtmlPage.Split(manage.Headers.Location!.ToString()).Path);
        }

        /// <summary>Requests an address with the browser, following nothing.</summary>
        /// <param name="flow">The browser.</param>
        /// <param name="address">The address, relative or on the test host.</param>
        /// <returns>The response.</returns>
        private static Task<HttpResponseMessage> GetAsync(OidcFlowClient flow, string address)
        {
            return flow.Browser.GetAsync(new Uri(address, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
        }
    }
}
