// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     RP-initiated logout ends a session only when the request is tied to it or the user
    ///     confirms: a refused request ends nothing; a request with no hint, or with another
    ///     session's hint, asks first and signs out only on the user's anti-forgery-protected
    ///     confirmation; a request whose hint names the signed-in session proceeds at once. So no
    ///     page can sign a user out everywhere by navigating them to the endpoint.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class EndSessionTests(SqlServerFixture fixture)
    {
        /// <summary>The BFF client's registered post-logout redirect URI.</summary>
        private const string PostLogoutRedirectUri = "https://acme.app.tellma.com/signout-callback-oidc";

        [Fact]
        public async Task A_refused_end_session_request_leaves_the_user_signed_in()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idendrefuse");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, distribution, "ellen@example.com");

            string endSessionUrl = "/connect/endsession?client_id=acme&post_logout_redirect_uri="
                + Uri.EscapeDataString("https://evil.example/");
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(endSessionUrl, UriKind.Relative), TestContext.Current.CancellationToken);

            // Refused in place, on the error page, with no redirect anywhere.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);

            await AssertSignedInAsync(flow);
            Assert.Equal(0, await SessionTerminationsAsync(factory));
        }

        [Fact]
        public async Task A_request_with_no_hint_asks_before_signing_out_and_signs_out_on_confirmation()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idendconfirm");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, distribution, "fiona@example.com");

            // A bare request — any page can navigate a browser to it — only asks.
            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/connect/endsession?post_logout_redirect_uri=" + Uri.EscapeDataString(PostLogoutRedirectUri), UriKind.Relative),
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, page.StatusCode);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page);
            Assert.True(fields.ContainsKey("__RequestVerificationToken"), "The confirmation carries no anti-forgery token.");
            Assert.Equal(PostLogoutRedirectUri, fields["post_logout_redirect_uri"]);

            await AssertSignedInAsync(flow);
            Assert.Equal(0, await SessionTerminationsAsync(factory));

            // A cross-site post of the confirmation, which cannot carry the anti-forgery token,
            // ends nothing either.
            Dictionary<string, string> forged = new(fields) { ["submit.SignOut"] = "yes" };
            forged.Remove("__RequestVerificationToken");
            using (HttpResponseMessage refused = await flow.PostFormAsync(action, forged))
            {
                Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            }

            await AssertSignedInAsync(flow);
            Assert.Equal(0, await SessionTerminationsAsync(factory));

            // The user's own confirmation signs out and returns to the registered callback.
            fields["submit.SignOut"] = "yes";
            using HttpResponseMessage confirmed = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
            Assert.StartsWith(PostLogoutRedirectUri, confirmed.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);

            await AssertSignedOutAsync(flow);
            Assert.Equal(1, await SessionTerminationsAsync(factory));
        }

        [Fact]
        public async Task A_request_whose_hint_names_the_signed_in_session_signs_out_at_once()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idendhint");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            using OidcFlowClient flow = factory.CreateFlowClient();
            string idToken = await SignInAsync(factory, flow, distribution, "gwen@example.com");

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(EndSessionUrl(idToken), UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.StartsWith(PostLogoutRedirectUri, response.Headers.Location!.AbsoluteUri, StringComparison.Ordinal);
            await AssertSignedOutAsync(flow);
            Assert.Equal(1, await SessionTerminationsAsync(factory));
        }

        [Fact]
        public async Task A_request_carrying_another_sessions_hint_asks_first()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idendotherhint");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            // Another user's valid id token, such as one a hostile relying party holds.
            string otherIdToken;
            using (OidcFlowClient other = factory.CreateFlowClient())
            {
                otherIdToken = await SignInAsync(factory, other, distribution, "hank@example.com");
            }

            using OidcFlowClient flow = factory.CreateFlowClient();
            await SignInAsync(factory, flow, distribution, "iris@example.com");

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(EndSessionUrl(otherIdToken), UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains("submit.SignOut", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            await AssertSignedInAsync(flow);
            Assert.Equal(0, await SessionTerminationsAsync(factory));
        }

        /// <summary>The BFF's end-session URL carrying an id token hint and its registered callback.</summary>
        private static string EndSessionUrl(string idToken)
        {
            return "/connect/endsession?client_id=acme"
                + "&id_token_hint=" + Uri.EscapeDataString(idToken)
                + "&post_logout_redirect_uri=" + Uri.EscapeDataString(PostLogoutRedirectUri);
        }

        /// <summary>Asserts the identity cookie still authenticates: the account pages answer.</summary>
        private static async Task AssertSignedInAsync(OidcFlowClient flow)
        {
            using HttpResponseMessage manage = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, manage.StatusCode);
        }

        /// <summary>Asserts the identity cookie is gone: the account pages send the browser to sign in.</summary>
        private static async Task AssertSignedOutAsync(OidcFlowClient flow)
        {
            using HttpResponseMessage manage = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, manage.StatusCode);
            Assert.Contains("/Identity/Account/Login", manage.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        /// <summary>The number of session terminations the audit trail records.</summary>
        private static async Task<int> SessionTerminationsAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            return await db.Set<Data.Entities.AuditEvent>()
                .CountAsync(static e => e.Action == "SessionTerminated", TestContext.Current.CancellationToken);
        }

        /// <summary>
        ///     Signs a new user in through the distribution's BFF client and returns the id token the
        ///     code exchange issues.
        /// </summary>
        private static async Task<string> SignInAsync(
            StandaloneFactory factory, OidcFlowClient flow, DistributionClientCredentials distribution, string email)
        {
            await TestData.CreateActiveUserAsync(factory, email);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
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
            string code;
            using (HttpResponseMessage authorized = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
                code = (string?)Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorized.Headers.Location!.Query)["code"]
                    ?? throw new InvalidOperationException("No code returned.");
            }

            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["code"] = code,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["code_verifier"] = verifier,
            });
            return tokens.RootElement.GetProperty("id_token").GetString()!;
        }
    }
}
