// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The revocation endpoint (RFC 7009): a client revokes a refresh token it holds, the
    ///     token stops working, and the revocation lands in the audit trail with the affected
    ///     user's subject.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class RevocationTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Revoking_a_refresh_token_stops_it_and_is_audited_with_the_subject()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idrevoke");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "frank@example.com");

            (string refreshToken, string clientSecret) = await SignInAndGetRefreshTokenAsync(factory, distribution);

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage revokeResponse = await client.PostAsync(
                new Uri("/connect/revoke", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "acme",
                    ["client_secret"] = clientSecret,
                    ["token"] = refreshToken,
                    ["token_type_hint"] = "refresh_token",
                }),
                TestContext.Current.CancellationToken);
            Assert.True(revokeResponse.IsSuccessStatusCode,
                await revokeResponse.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));

            // The revoked token no longer refreshes.
            using HttpResponseMessage refreshResponse = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "refresh_token",
                    ["client_id"] = "acme",
                    ["client_secret"] = clientSecret,
                    ["refresh_token"] = refreshToken,
                }),
                TestContext.Current.CancellationToken);
            Assert.False(refreshResponse.IsSuccessStatusCode);

            // The revocation is in the audit trail, attributed to both the client and the user.
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();
            string userId = (await userManager.FindByEmailAsync("frank@example.com"))!.Id;

            // Assert on the single row rather than pre-filtering to the expected one, so a
            // spurious extra revocation row cannot hide behind the filter.
            Data.Entities.AuditEvent revokedEvent = Assert.Single(
                await db.Set<Data.Entities.AuditEvent>()
                    .Where(static e => e.Action == "TokenRevoked")
                    .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal("success", revokedEvent.Outcome);
            Assert.Equal(userId, revokedEvent.Subject);
            Assert.Equal("acme", revokedEvent.ClientId);

            // The alerting fields §15 pivots on are populated, not silently null: the caller's
            // address and the session the revoked grant belonged to.
            Assert.Equal(StandaloneFactory.RemoteIpAddress, revokedEvent.IpAddress);
            Assert.False(string.IsNullOrEmpty(revokedEvent.Sid));

            // RFC 7009 answers 200 for an unknown token, so the trail records whether the
            // presented token was authentic at all.
            Assert.Contains("\"tokenAuthentic\":true", revokedEvent.DetailsJson, StringComparison.Ordinal);
        }

        /// <summary>Runs the full auth-code sign-in and returns the refresh token.</summary>
        private static async Task<(string RefreshToken, string ClientSecret)> SignInAndGetRefreshTokenAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution)
        {
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
            });

            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync("frank@example.com", loginUrl);
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

            return (tokens.RootElement.GetProperty("refresh_token").GetString()!, distribution.BffClientSecret);
        }
    }
}
