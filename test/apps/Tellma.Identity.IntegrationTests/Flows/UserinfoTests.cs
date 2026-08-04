// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The userinfo endpoint returns the user's claims for a valid access token, gated on the
    ///     granted scopes exactly like the token claims, and challenges without one.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class UserinfoTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Userinfo_returns_scope_gated_claims_for_a_signed_in_user()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iduserinfo");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "grace@example.com");

            string accessToken = await SignInAndGetAccessTokenAsync(
                factory, distribution, "grace@example.com", "openid email profile");

            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/connect/userinfo", UriKind.Relative), TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            Assert.False(string.IsNullOrEmpty(root.GetProperty("sub").GetString()));
            Assert.Equal("grace@example.com", root.GetProperty("email").GetString());
            Assert.Equal("grace@example.com", root.GetProperty("preferred_username").GetString());
            Assert.Equal("grace", root.GetProperty("name").GetString());
        }

        [Fact]
        public async Task Userinfo_withholds_claims_whose_scope_was_not_granted()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iduserinfoscope");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "heidi@example.com");

            string accessToken = await SignInAndGetAccessTokenAsync(factory, distribution, "heidi@example.com", "openid");

            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/connect/userinfo", UriKind.Relative), TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;
            Assert.False(string.IsNullOrEmpty(root.GetProperty("sub").GetString()));
            Assert.False(root.TryGetProperty("email", out _));
            Assert.False(root.TryGetProperty("name", out _));
        }

        [Fact]
        public async Task Userinfo_challenges_without_a_token()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "iduserinfoanon");
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/connect/userinfo", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>Runs the full auth-code sign-in and returns the access token.</summary>
        private static async Task<string> SignInAndGetAccessTokenAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution, string email, string scope)
        {
            using OidcFlowClient flow = new(factory);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();

            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });

            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync(email, loginUrl);
            string code;
            using (HttpResponseMessage authorizeResponse = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken))
            {
                Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
                    Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(authorizeResponse.Headers.Location!.Query);
                code = (string?)query["code"] ?? throw new InvalidOperationException("No code returned.");
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

            return tokens.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
