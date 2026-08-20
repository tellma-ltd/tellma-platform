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

        [Fact]
        public async Task Another_distribution_cannot_read_or_delete_the_account()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsvcacctown");

            // acme creates a service account.
            using HttpClient acmeClient = factory.CreateClient();
            acmeClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));
            using HttpResponseMessage createResponse = await acmeClient.PostAsJsonAsync(
                new Uri("/api/identity/service-accounts", UriKind.Relative),
                new { displayName = "Acme job", resources = Array.Empty<string>() },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(
                await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string clientId = created.RootElement.GetProperty("clientId").GetString()!;

            // beta — a different distribution with the same tellma_identity scope — must see 404
            // on both read and delete: the account belongs to acme.
            using HttpClient betaClient = factory.CreateClient();
            betaClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory, slug: "beta"));

            using HttpResponseMessage foreignGet = await betaClient.GetAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignGet.StatusCode);

            using HttpResponseMessage foreignDelete = await betaClient.DeleteAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NotFound, foreignDelete.StatusCode);

            // The owner still reads and deletes it.
            using HttpResponseMessage ownerGet = await acmeClient.GetAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, ownerGet.StatusCode);

            using HttpResponseMessage ownerDelete = await acmeClient.DeleteAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, ownerDelete.StatusCode);
        }

        [Fact]
        public async Task The_control_plane_administers_any_distributions_service_account()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idsvcacctop",
                new Dictionary<string, string?>
                {
                    ["TellmaIdentity:Seed:Clients:0:ClientId"] = "tellma-control-plane",
                    ["TellmaIdentity:Seed:Clients:0:Kind"] = "ControlPlane",
                    ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = ControlPlaneSecret,
                });

            // acme creates a service account.
            using HttpClient acmeClient = factory.CreateClient();
            acmeClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));
            using HttpResponseMessage createResponse = await acmeClient.PostAsJsonAsync(
                new Uri("/api/identity/service-accounts", UriKind.Relative),
                new { displayName = "Acme job", resources = Array.Empty<string>() },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
            using var created = JsonDocument.Parse(
                await createResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            string clientId = created.RootElement.GetProperty("clientId").GetString()!;

            // The control plane holds no distribution origin, so ownership scoping alone would
            // leave it — and therefore everyone — unable to clean the account up. Holding the
            // control-plane scope is what makes it the operator.
            using HttpClient operatorClient = factory.CreateClient();
            operatorClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetControlPlaneTokenAsync(factory));

            using HttpResponseMessage operatorGet = await operatorClient.GetAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, operatorGet.StatusCode);

            using HttpResponseMessage operatorDelete = await operatorClient.DeleteAsync(
                new Uri($"/api/identity/service-accounts/{clientId}", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, operatorDelete.StatusCode);
        }

        [Fact]
        public async Task Create_rejects_a_foreign_distribution_audience()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idsvcacctforeign");
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));

            // The caller (acme's backend) names a different distribution's origin as an audience.
            string[] resources = ["https://evil.app.tellma.com"];
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/service-accounts", UriKind.Relative),
                new { displayName = "Cross-tenant job", resources },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        /// <summary>The seeded control-plane client's secret for the operator-path test.</summary>
        private const string ControlPlaneSecret = "control-plane-test-secret-0123456789";

        /// <summary>Obtains a token carrying both the control-plane and identity scopes.</summary>
        private static async Task<string> GetControlPlaneTokenAsync(StandaloneFactory factory)
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = "tellma-control-plane",
                    ["client_secret"] = ControlPlaneSecret,
                    ["scope"] = "tellma_control_plane tellma_identity",
                    ["resource"] = "http://localhost",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("access_token").GetString()!;
        }

        /// <summary>Provisions a distribution and obtains a token carrying the tellma_identity scope.</summary>
        private static async Task<string> GetIdentityScopeTokenAsync(StandaloneFactory factory, string slug = "acme")
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, slug);

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
