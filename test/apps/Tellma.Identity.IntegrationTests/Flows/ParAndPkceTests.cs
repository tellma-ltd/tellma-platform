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
    ///     The security-BCP guardrails: BFF clients must push their parameters (PAR), PKCE cannot
    ///     be downgraded, and redirect URIs match exactly — no wildcard trust.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ParAndPkceTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Bff_front_channel_authorization_without_par_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idpar");
            await TestData.ProvisionDistributionAsync(factory);

            using OidcFlowClient flow = new(factory);
            (string _, string challenge) = OidcFlowClient.CreatePkcePair();

            // Plain front-channel parameters, tamperable in the browser URL — exactly what the
            // per-client PAR requirement forbids.
            string authorizeUrl = "/connect/authorize?client_id=acme"
                + "&redirect_uri=" + Uri.EscapeDataString("https://acme.app.tellma.com/signin-oidc")
                + "&response_type=code&scope=openid"
                + "&code_challenge=" + challenge + "&code_challenge_method=S256";

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken);

            // The request is refused before any interaction: it must never reach the login UI and
            // must never carry an authorization code back to the client.
            string location = response.Headers.Location?.ToString() ?? string.Empty;
            Assert.DoesNotContain("/Identity/Account/Login", location, StringComparison.Ordinal);
            Assert.DoesNotContain("code=", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Pushing_without_a_code_challenge_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idpkce");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            using HttpClient backchannel = factory.CreateClient();
            using HttpResponseMessage response = await backchannel.PostAsync(
                new Uri("/connect/par", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "acme",
                    ["client_secret"] = distribution.BffClientSecret,
                    ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                    ["response_type"] = "code",
                    ["scope"] = "openid",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_request", document.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Pushing_a_mismatched_redirect_uri_is_rejected()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idredir");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            (string _, string challenge) = OidcFlowClient.CreatePkcePair();

            using HttpClient backchannel = factory.CreateClient();
            using HttpResponseMessage response = await backchannel.PostAsync(
                new Uri("/connect/par", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "acme",
                    ["client_secret"] = distribution.BffClientSecret,
                    // Same host, different path: exact matching must still reject it.
                    ["redirect_uri"] = "https://acme.app.tellma.com/evil-callback",
                    ["response_type"] = "code",
                    ["scope"] = "openid",
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                }),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_request", document.RootElement.GetProperty("error").GetString());
        }
    }
}
