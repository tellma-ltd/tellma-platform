// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Explicit consent: a seeded client can require it, and then an authorization stops at the
    ///     consent screen instead of returning a code. Every provisioned client is first-party and
    ///     implicit, so without a way to configure this the consent view had no caller at all.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ConsentTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_client_requiring_consent_stops_at_the_consent_screen()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture,
                "idconsent",
                new Dictionary<string, string?>
                {
                    ["TellmaIdentity:Seed:Clients:0:ClientId"] = "third-party",
                    ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Third Party App",
                    ["TellmaIdentity:Seed:Clients:0:Kind"] = "Native",
                    ["TellmaIdentity:Seed:Clients:0:RequireConsent"] = "true",
                    ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = "http://127.0.0.1/callback",
                });

            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "consent@example.com");

            using OidcFlowClient flow = new(factory);
            (_, string challenge) = OidcFlowClient.CreatePkcePair();

            // Sign in through the first-party client, which stays implicit.
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

            string returnUrl = await flow.SignInWithEmailCodeAsync("consent@example.com", loginUrl);
            using (await flow.Browser.GetAsync(new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken))
            {
            }

            // Now the consent-requiring client, on the session just established.
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(
                    "/connect/authorize?client_id=third-party&response_type=code"
                    + "&redirect_uri=" + Uri.EscapeDataString("http://127.0.0.1/callback")
                    + "&scope=" + Uri.EscapeDataString("openid profile")
                    + "&code_challenge=" + challenge + "&code_challenge_method=S256",
                    UriKind.Relative),
                TestContext.Current.CancellationToken);

            // The consent view renders in place rather than redirecting back with a code.
            Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("Third Party App", html, StringComparison.Ordinal);
            Assert.Contains("Authorize application", html, StringComparison.Ordinal);
        }
    }
}
