// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Token exchange (RFC 8693): a distribution backend down-scopes its own client-credentials
    ///     token, and the exchange can never widen scope.
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
