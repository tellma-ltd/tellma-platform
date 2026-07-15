// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The service-account API: the secret is returned exactly once, metadata never includes
    ///     it, and a freshly created account can immediately obtain tokens.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ServiceAccountApiTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Create_returns_the_secret_once_and_the_account_can_authenticate()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsvcacct");
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));

            string[] resources = ["https://acme.app.tellma.com"];
            using HttpResponseMessage createResponse = await client.PostAsJsonAsync(
                new Uri("/api/identity/service-accounts", UriKind.Relative),
                new { displayName = "Nightly job", resources },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(
                await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string clientId = created.RootElement.GetProperty("clientId").GetString()!;
            string clientSecret = created.RootElement.GetProperty("clientSecret").GetString()!;
            Assert.StartsWith("svc_", clientId, StringComparison.Ordinal);

            // The metadata endpoint never returns the secret.
            using HttpResponseMessage getResponse = await client.GetAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            string metadata = await getResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain(clientSecret, metadata, StringComparison.Ordinal);
            Assert.DoesNotContain("secret", metadata, StringComparison.OrdinalIgnoreCase);

            // The account authenticates immediately with client credentials.
            using HttpResponseMessage tokenResponse = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);
            Assert.True(tokenResponse.IsSuccessStatusCode,
                await tokenResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>Provisions a distribution and obtains a token carrying the tellma_identity scope.</summary>
        private static async Task<string> GetIdentityScopeTokenAsync(StandaloneFactory factory)
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["scope"] = "tellma_identity",
                    ["resource"] = "http://localhost",
                }),
                TestContext.Current.CancellationToken);

            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
