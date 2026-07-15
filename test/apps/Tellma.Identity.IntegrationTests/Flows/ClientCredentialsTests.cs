// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The client-credentials flow end-to-end: a runtime-provisioned service account obtains
    ///     a signed JWT access token whose audiences come from its explicit resources; bad
    ///     secrets are rejected; no refresh token is ever issued.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ClientCredentialsTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Service_account_obtains_an_access_token_with_explicit_audience()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idcc");
            using HttpClient client = factory.CreateClient();

            ServiceAccountCredentials account = await CreateServiceAccountAsync(
                factory, ["https://acme.app.tellma.com"]);

            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = account.ClientId,
                    ["client_secret"] = account.ClientSecret,
                    ["scope"] = "tellma_api",
                    ["resource"] = "https://acme.app.tellma.com",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            Assert.Equal("Bearer", document.RootElement.GetProperty("token_type").GetString());

            // Client credentials never issues a refresh token.
            Assert.False(document.RootElement.TryGetProperty("refresh_token", out _));

            // The access token is a signed-only JWT readable without a database call.
            string accessToken = document.RootElement.GetProperty("access_token").GetString()!;
            using JsonDocument payload = DecodeJwtPayload(accessToken);
            Assert.Equal(account.ClientId, payload.RootElement.GetProperty("sub").GetString());
            Assert.Equal("http://localhost/", payload.RootElement.GetProperty("iss").GetString());
            Assert.Equal("https://acme.app.tellma.com", ReadSingleOrArray(payload.RootElement, "aud").Single());
        }

        [Fact]
        public async Task Invalid_client_secret_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idccbad");
            using HttpClient client = factory.CreateClient();

            ServiceAccountCredentials account = await CreateServiceAccountAsync(factory, []);

            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = account.ClientId,
                    ["client_secret"] = "not-the-secret",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_client", document.RootElement.GetProperty("error").GetString());
        }

        /// <summary>Provisions a service account through the engine's provisioning surface.</summary>
        internal static async Task<ServiceAccountCredentials> CreateServiceAccountAsync(
            StandaloneFactory factory, IReadOnlyCollection<string> resources)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            IClientProvisioningService provisioning =
                scope.ServiceProvider.GetRequiredService<IClientProvisioningService>();
            return await provisioning.CreateServiceAccountAsync(
                "Integration test account", resources, createdByClientId: null, TestContext.Current.CancellationToken);
        }

        /// <summary>Decodes a JWT's payload segment without validating it (assert-only).</summary>
        internal static JsonDocument DecodeJwtPayload(string jwt)
        {
            string payload = jwt.Split('.')[1];
            string padded = payload.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + ((4 - (padded.Length % 4)) % 4), '=');
            return JsonDocument.Parse(Convert.FromBase64String(padded));
        }

        /// <summary>Reads a claim that may serialize as a string or an array.</summary>
        internal static string[] ReadSingleOrArray(JsonElement root, string property)
        {
            JsonElement element = root.GetProperty(property);
            return element.ValueKind == JsonValueKind.Array
                ? [.. element.EnumerateArray().Select(static e => e.GetString()!)]
                : [element.GetString()!];
        }
    }
}
