// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Refresh-token rotation, reuse detection, and the authority-side policy re-evaluation
    ///     that makes the short access-token lifetime the point at which a tightened tenant policy
    ///     takes effect.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class RefreshTokenTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Refresh_rotates_the_token_and_a_reused_token_revokes_the_family()
        {
            // Disable the concurrency reuse leeway so an immediate re-use is treated as replay
            // (production keeps the ~30 s leeway that tolerates the multi-tab refresh race).
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idrt", new Dictionary<string, string?> { ["TellmaIdentity:Lifetimes:RefreshTokenReuseLeeway"] = "00:00:00" });
            RefreshContext context = await SignInAndGetTokensAsync(factory, "carol@example.com");

            // A first refresh rotates the token (rotation is on by default).
            using JsonDocument rotated = await RefreshAsync(context, context.RefreshToken);
            string secondRefreshToken = rotated.RootElement.GetProperty("refresh_token").GetString()!;
            Assert.NotEqual(context.RefreshToken, secondRefreshToken);

            // Reusing the now-redeemed first token is replay: it revokes the whole family, so even
            // the legitimately rotated second token stops working.
            string? reuseError = await RefreshExpectingErrorAsync(context, context.RefreshToken);
            Assert.Equal("invalid_grant", reuseError);

            string? familyError = await RefreshExpectingErrorAsync(context, secondRefreshToken);
            Assert.Equal("invalid_grant", familyError);
        }

        [Fact]
        public async Task Sign_out_everywhere_stops_renewal_via_the_security_stamp()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idrtstamp");
            RefreshContext context = await SignInAndGetTokensAsync(factory, "dave@example.com");

            // Bump the security stamp the way "sign out everywhere" does.
            using (IServiceScope scope = factory.Services.CreateScope())
            {
                Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> userManager =
                    scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();
                TellmaIdentityUser user = (await userManager.FindByEmailAsync("dave@example.com"))!;
                await userManager.UpdateSecurityStampAsync(user);
            }

            string? error = await RefreshExpectingErrorAsync(context, context.RefreshToken);
            Assert.Equal("invalid_grant", error);
        }

        [Fact]
        public async Task A_disabled_method_stops_renewal()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idrtmethod");
            RefreshContext context = await SignInAndGetTokensAsync(factory, "erin@example.com");

            // The tenant tightens policy: email code is no longer allowed. The BFF pushes the new
            // allow-list on the refresh call; the session authenticated with email code, so
            // renewal must stop.
            Dictionary<string, string> parameters = RefreshParameters(context, context.RefreshToken);
            parameters["tellma_allowed_methods"] = "passkey";

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_grant", document.RootElement.GetProperty("error").GetString());
        }

        /// <summary>Holds what the refresh calls need: the client secret and the first refresh token.</summary>
        private sealed record RefreshContext(StandaloneFactory Factory, string ClientSecret, string RefreshToken);

        /// <summary>Runs a full auth-code sign-in and returns the refresh context.</summary>
        private static async Task<RefreshContext> SignInAndGetTokensAsync(StandaloneFactory factory, string email)
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, email);

            using OidcFlowClient flow = new(factory);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();

            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid offline_access tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["tellma_allowed_methods"] = "email_code",
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

            return new RefreshContext(factory, distribution.BffClientSecret, tokens.RootElement.GetProperty("refresh_token").GetString()!);
        }

        /// <summary>Builds the parameters for a refresh call.</summary>
        private static Dictionary<string, string> RefreshParameters(RefreshContext context, string refreshToken)
        {
            return new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = "acme",
                ["client_secret"] = context.ClientSecret,
                ["refresh_token"] = refreshToken,
            };
        }

        /// <summary>Performs a successful refresh.</summary>
        private static async Task<JsonDocument> RefreshAsync(RefreshContext context, string refreshToken)
        {
            using HttpClient client = context.Factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(RefreshParameters(context, refreshToken)),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body);
        }

        /// <summary>Performs a refresh expected to fail and returns the error code.</summary>
        private static async Task<string?> RefreshExpectingErrorAsync(RefreshContext context, string refreshToken)
        {
            using HttpClient client = context.Factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(RefreshParameters(context, refreshToken)),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("error").GetString();
        }
    }
}
