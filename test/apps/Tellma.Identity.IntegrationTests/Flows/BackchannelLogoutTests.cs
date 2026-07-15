// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.IdentityModel.Tokens;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Global logout at the authority fans a signed <c>logout_token</c> out to every
    ///     distribution holding a session under the same <c>sid</c>, and revokes the grants so
    ///     renewal stops.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class BackchannelLogoutTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Global_logout_notifies_the_distribution_and_stops_renewal()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idbcl");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(
                factory, backchannelLogoutUri: new Uri("https://acme.app.tellma.com/backchannel-logout"));
            await TestData.CreateActiveUserAsync(factory, "grace@example.com");

            using OidcFlowClient flow = new(factory);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();

            // Sign in through the full flow so a session and a refresh token exist.
            string realRequestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid offline_access tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["tellma_allowed_methods"] = "email_code",
            });

            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(realRequestUri);
            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync("grace@example.com", loginUrl);
            string code;
            using (HttpResponseMessage authorizeResponse = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken))
            {
                Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
                    Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizeResponse.Headers.Location!.Query);
                code = (string?)query["code"] ?? throw new InvalidOperationException("No code returned.");
            }

            string refreshToken;
            using (JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["code"] = code,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["code_verifier"] = verifier,
            }))
            {
                refreshToken = tokens.RootElement.GetProperty("refresh_token").GetString()!;
            }

            // Log out globally at the authority.
            using (HttpResponseMessage logout = await flow.Browser.PostAsync(
                new Uri("/Identity/Account/Logout", UriKind.Relative),
                await AntiforgeryContentAsync(flow),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(System.Net.HttpStatusCode.Redirect, logout.StatusCode);
            }

            // The distribution received exactly one signed logout token carrying a sid and the
            // back-channel logout event.
            Assert.Single(factory.BackchannelLogouts.LogoutTokens);
            string logoutToken = factory.BackchannelLogouts.LogoutTokens.First();
            await AssertValidLogoutTokenAsync(factory, logoutToken);

            // The grant backing the refresh token was revoked: renewal now fails.
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage refreshResponse = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = "acme",
                    ["client_secret"] = distribution.BffClientSecret,
                    ["refresh_token"] = refreshToken,
                }),
                TestContext.Current.CancellationToken);
            Assert.False(refreshResponse.IsSuccessStatusCode);
        }

        /// <summary>Posts the logout form after reading its antiforgery token.</summary>
        private static async Task<FormUrlEncodedContent> AntiforgeryContentAsync(OidcFlowClient flow)
        {
            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Logout", UriKind.Relative), TestContext.Current.CancellationToken);
            (string _, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page);
            return new FormUrlEncodedContent(fields);
        }

        /// <summary>Validates the logout token's signature against JWKS and its required claims.</summary>
        private static async Task AssertValidLogoutTokenAsync(StandaloneFactory factory, string logoutToken)
        {
            using HttpClient client = factory.CreateClient();
            JsonWebKeySet jwks = (await client.GetFromJsonAsync<JsonWebKeySet>(
                new Uri("/.well-known/jwks", UriKind.Relative), TestContext.Current.CancellationToken))!;

            Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler handler = new();
            TokenValidationResult result = await handler.ValidateTokenAsync(logoutToken, new TokenValidationParameters
            {
                ValidIssuer = "http://localhost/",
                ValidAudience = "acme",
                IssuerSigningKeys = jwks.Keys,
                ValidateLifetime = false,
            });

            Assert.True(result.IsValid, result.Exception?.Message);
            Assert.False(string.IsNullOrEmpty((string?)result.Claims["sid"]));
            Assert.True(result.Claims.ContainsKey("events"));
        }
    }
}
