// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Identity re-issues the session cookie every validation interval to pick up a changed
    ///     security stamp, and rebuilds the principal from the user alone — dropping every claim
    ///     the engine recorded at sign-in. A session that outlives the interval must still be able
    ///     to authorize; without the refresh hook it could not, and the user was bounced back to
    ///     sign-in with "the session must be re-established".
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SecurityStampRefreshTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_refreshed_session_can_still_authorize()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idstamp");

            // Zero makes every request re-validate, so the refresh this covers happens on the very
            // next hop instead of five minutes later.
            factory.ServiceOverrides.Add(static services =>
                services.Configure<SecurityStampValidatorOptions>(static validator =>
                    validator.ValidationInterval = TimeSpan.Zero));

            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "stamp@example.com");

            using OidcFlowClient flow = new(factory);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
            await SignInAsync(flow, distribution, challenge);

            // A second authorization on the same session: by now the cookie has been through at
            // least one refresh, which is exactly when the evidence used to disappear.
            string code = await AuthorizeAsync(flow, distribution, challenge);
            Assert.NotEmpty(code);

            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["code"] = code,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["code_verifier"] = verifier,
            });

            // The evidence survived the rebuild, not merely the sign-in: acr and the methods are
            // derived from the cookie claims the refresh would otherwise have discarded.
            using JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(
                tokens.RootElement.GetProperty("access_token").GetString()!);
            Assert.Equal("urn:tellma:acr:aal1", payload.RootElement.GetProperty("acr").GetString());
            Assert.Equal("email_code", payload.RootElement.GetProperty("tellma_methods").GetString());
            Assert.NotEqual(0, payload.RootElement.GetProperty("auth_time").GetInt64());
            Assert.NotEmpty(payload.RootElement.GetProperty("sid").GetString()!);
        }

        /// <summary>Signs the user in through the full authorization-code flow.</summary>
        private static async Task SignInAsync(
            OidcFlowClient flow, DistributionClientCredentials distribution, string challenge)
        {
            string requestUri = await PushAsync(flow, distribution, challenge);
            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);

            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync("stamp@example.com", loginUrl);
            using HttpResponseMessage authorizeResponse = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.NotNull(authorizeResponse.Headers.Location);
        }

        /// <summary>Runs another authorization on the existing session and returns the code.</summary>
        private static async Task<string> AuthorizeAsync(
            OidcFlowClient flow, DistributionClientCredentials distribution, string challenge)
        {
            string requestUri = await PushAsync(flow, distribution, challenge);
            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken);

            string location = response.Headers.Location?.ToString()
                ?? throw new InvalidOperationException("The authorization request did not redirect.");

            // A refused authorization redirects back to sign-in (or carries an error) instead of
            // returning a code, which is the failure this test exists to catch.
            Dictionary<string, Microsoft.Extensions.Primitives.StringValues> query =
                Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(location, UriKind.RelativeOrAbsolute).IsAbsoluteUri
                    ? new Uri(location).Query
                    : location[location.IndexOf('?')..]);

            Assert.False(query.ContainsKey("error"), $"authorization failed: {location}");
            return (string?)query["code"] ?? throw new InvalidOperationException($"No code in {location}");
        }

        /// <summary>Pushes an authorization request and returns its request_uri.</summary>
        private static Task<string> PushAsync(
            OidcFlowClient flow, DistributionClientCredentials distribution, string challenge)
        {
            return flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
            });
        }
    }
}
