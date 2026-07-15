// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The full browser journey a distribution BFF drives: PAR (carrying the tenant's method
    ///     allow-list) → authorize → login redirect → email-code sign-in → code issuance with
    ///     <c>iss</c> and state → server-side code exchange with PKCE → tokens with the right
    ///     claims and the origin-derived audience.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class AuthorizationCodeFlowTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Bff_completes_the_authorization_code_flow_end_to_end()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idflow");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "alice@example.com");

            using OidcFlowClient flow = new(factory);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();

            // 1. The BFF pushes the authorization parameters server-to-server (PAR), including
            //    the tenant's method allow-list, so nothing sensitive rides the browser URL.
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid profile email offline_access tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "state-123",
                ["nonce"] = "nonce-456",
                ["tellma_allowed_methods"] = "email_code",
            });

            // 2. The browser opens authorize; with no SSO session it is sent to the login UI.
            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
            using HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, challengeResponse.StatusCode);
            string loginUrl = challengeResponse.Headers.Location!.ToString();
            Assert.Contains("/Identity/Account/Login", loginUrl, StringComparison.Ordinal);
            Assert.Contains("email_code", Uri.UnescapeDataString(loginUrl), StringComparison.Ordinal);

            // 3. Sign in with the emailed one-time code; the browser returns to authorize.
            string returnUrl = await flow.SignInWithEmailCodeAsync("alice@example.com", loginUrl);
            using HttpResponseMessage authorizeResponse = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, authorizeResponse.StatusCode);

            // 4. The authorization response redirects to the exact registered redirect URI with
            //    the code, the state, and the RFC 9207 issuer.
            Uri redirect = authorizeResponse.Headers.Location!;
            Assert.StartsWith("https://acme.app.tellma.com/signin-oidc", redirect.AbsoluteUri, StringComparison.Ordinal);
            Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query = QueryHelpers.ParseQuery(redirect.Query);
            Assert.Equal("state-123", (string?)query["state"]);
            Assert.Equal("http://localhost/", (string?)query["iss"]);
            string code = (string?)query["code"] ?? throw new InvalidOperationException("No code returned.");

            // 5. The BFF exchanges the code server-side with its secret and the PKCE verifier.
            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["code"] = code,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["code_verifier"] = verifier,
            });

            Assert.True(tokens.RootElement.TryGetProperty("id_token", out _), "The id_token is missing.");
            Assert.True(tokens.RootElement.TryGetProperty("refresh_token", out _), "The refresh_token is missing.");

            // 6. The access token is a signed JWT carrying the assurance, session, and audience
            //    contract the platform depends on.
            string accessToken = tokens.RootElement.GetProperty("access_token").GetString()!;
            using JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(accessToken);
            JsonElement claims = payload.RootElement;

            Assert.Equal("alice@example.com", claims.GetProperty("email").GetString());
            Assert.Equal("urn:tellma:acr:aal1", claims.GetProperty("acr").GetString());
            Assert.Contains("otp", ClientCredentialsTests.ReadSingleOrArray(claims, "amr"));
            Assert.Contains("email_code", ClientCredentialsTests.ReadSingleOrArray(claims, "tellma_methods"));
            Assert.False(string.IsNullOrEmpty(claims.GetProperty("sid").GetString()));
            Assert.True(claims.TryGetProperty("auth_time", out _), "auth_time is missing.");
            Assert.Equal("https://acme.app.tellma.com", ClientCredentialsTests.ReadSingleOrArray(claims, "aud").Single());

            // The private allow-list snapshot and the security stamp never reach tokens.
            Assert.False(claims.TryGetProperty("tellma_allowed_methods", out _));
            Assert.False(claims.TryGetProperty("AspNet.Identity.SecurityStamp", out _));
        }
    }
}
