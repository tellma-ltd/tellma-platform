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
    ///     RFC 8707 resource indicators are enforced per client: a caller can only mint tokens
    ///     for audiences it was explicitly granted, so a token for one distribution is not
    ///     obtainable — let alone valid — at another.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ResourceIndicatorTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Requesting_a_foreign_resource_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idrsrc");
            using HttpClient client = factory.CreateClient();

            // Granted acme only; asks for globex.
            ServiceAccountCredentials account = await ClientCredentialsTests.CreateServiceAccountAsync(
                factory, ["https://acme.app.tellma.com"]);

            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = account.ClientId,
                    ["client_secret"] = account.ClientSecret,
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://globex.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_request", document.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Machine_caller_requesting_tellma_api_without_a_resource_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idrsrc2");
            using HttpClient client = factory.CreateClient();

            ServiceAccountCredentials account = await ClientCredentialsTests.CreateServiceAccountAsync(
                factory, ["https://acme.app.tellma.com"]);

            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = account.ClientId,
                    ["client_secret"] = account.ClientSecret,
                    ["scope"] = "tellma_api",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_target", document.RootElement.GetProperty("error").GetString());
        }
    }
}
