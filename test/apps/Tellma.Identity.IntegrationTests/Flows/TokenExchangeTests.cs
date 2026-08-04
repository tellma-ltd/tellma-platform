// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Token exchange (RFC 8693): a distribution backend down-scopes its own client-credentials
    ///     token. The exchange can never widen scope, and never carries a user.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class TokenExchangeTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Backend_exchanges_its_token_down_scoping_to_the_distribution_api()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idte");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(
                factory, allowTokenExchange: true);

            using HttpClient client = factory.CreateClient();

            // The backend obtains a client-credentials token carrying both scopes.
            using JsonDocument original = await GetClientCredentialsTokenAsync(
                client, distribution.ServiceClientId, distribution.ServiceClientSecret,
                scope: "tellma_identity tellma_api", resource: "https://acme.app.tellma.com");
            string subjectToken = original.RootElement.GetProperty("access_token").GetString()!;

            // It exchanges that token, down-scoping to just the distribution API.
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["subject_token"] = subjectToken,
                    ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var exchanged = JsonDocument.Parse(body);
            string accessToken = exchanged.RootElement.GetProperty("access_token").GetString()!;
            using JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(accessToken);
            Assert.Equal("https://acme.app.tellma.com", ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud").Single());

            // The scope was actually narrowed: tellma_api survives, tellma_identity is dropped. The
            // scope claim serializes as a single space-delimited string (RFC 9068).
            string[] scopes = payload.RootElement.GetProperty("scope").GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            Assert.Contains("tellma_api", scopes);
            Assert.DoesNotContain("tellma_identity", scopes);
        }

        [Fact]
        public async Task Exchange_cannot_widen_the_subject_token_scopes()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idtewiden");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(
                factory, allowTokenExchange: true);

            using HttpClient client = factory.CreateClient();

            // A narrow subject token (tellma_api only).
            using JsonDocument original = await GetClientCredentialsTokenAsync(
                client, distribution.ServiceClientId, distribution.ServiceClientSecret,
                scope: "tellma_api", resource: "https://acme.app.tellma.com");
            string subjectToken = original.RootElement.GetProperty("access_token").GetString()!;

            // Attempting to widen to tellma_identity must be rejected.
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["subject_token"] = subjectToken,
                    ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["scope"] = "tellma_identity",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_scope", document.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task A_backend_cannot_exchange_a_token_issued_to_the_browser_client()
        {
            // A user's token is issued to the distribution's BFF client, and the backend client is
            // neither its presenter nor its audience, so the protocol layer refuses this before the
            // endpoint runs. That is the outer of the two barriers against impersonation; the inner
            // one is Endpoint_refuses_a_user_token_even_from_the_client_that_holds_it below.
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idteuser");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(
                factory, allowTokenExchange: true);
            await TestData.CreateActiveUserAsync(factory, "trudy@example.com");

            string userToken = await SignInAndGetAccessTokenAsync(factory, distribution, "trudy@example.com");

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["subject_token"] = userToken,
                    ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Endpoint_refuses_a_user_token_even_from_the_client_that_holds_it()
        {
            // The client a user token was issued to *is* its presenter, so the protocol layer would
            // let that client exchange it. Provisioning never grants the browser client the exchange
            // grant type — but that is a registration choice, and one edit away from becoming an
            // impersonation capability, so the endpoint refuses user subjects itself. This test
            // grants the browser client that permission to reach the refusal, which is otherwise
            // unreachable and would be untested.
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idteself");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(
                factory, allowTokenExchange: true);
            await TestData.CreateActiveUserAsync(factory, "mallory@example.com");

            using (IServiceScope scope = factory.Services.CreateScope())
            {
                IOpenIddictApplicationManager manager =
                    scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                object application = await manager.FindByClientIdAsync("acme", TestContext.Current.CancellationToken)
                    ?? throw new InvalidOperationException("The browser client was not provisioned.");

                OpenIddictApplicationDescriptor descriptor = new();
                await manager.PopulateAsync(descriptor, application, TestContext.Current.CancellationToken);
                descriptor.Permissions.Add(Permissions.GrantTypes.TokenExchange);
                await manager.UpdateAsync(application, descriptor, TestContext.Current.CancellationToken);
            }

            string userToken = await SignInAndGetAccessTokenAsync(factory, distribution, "mallory@example.com");

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                    ["client_id"] = "acme",
                    ["client_secret"] = distribution.BffClientSecret,
                    ["subject_token"] = userToken,
                    ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
            Assert.Equal(
                "A token issued to a user cannot be exchanged.",
                document.RootElement.GetProperty("error_description").GetString());
        }

        /// <summary>Runs a full auth-code sign-in and returns the user's access token.</summary>
        private static async Task<string> SignInAndGetAccessTokenAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution, string email)
        {
            using OidcFlowClient flow = new(factory);
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

        /// <summary>Obtains a client-credentials access token.</summary>
        private static async Task<JsonDocument> GetClientCredentialsTokenAsync(
            HttpClient client, string clientId, string clientSecret, string scope, string resource)
        {
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope,
                    ["resource"] = resource,
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body);
        }
    }
}
