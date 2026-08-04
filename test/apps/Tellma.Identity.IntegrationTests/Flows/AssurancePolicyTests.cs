// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Security.Cryptography;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The aal3 tier is reachable only through a device-bound passkey, and the authorization
    ///     endpoint fails closed — with <c>unmet_authentication_requirements</c>, not an endless
    ///     login redirect — when the signed-in user holds none.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class AssurancePolicyTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Aal3_fails_closed_when_the_user_has_only_a_synced_passkey()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idaal3synced");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "synced@example.com");
            await AddPasskeyAsync(factory, user, deviceBound: false);

            using OidcFlowClient flow = new(factory);
            await EstablishSessionAsync(flow, distribution, "synced@example.com");

            using HttpResponseMessage response = await AuthorizeWithAal3Async(flow, distribution);

            // The browser is sent back to the client with the protocol error, not to the login
            // page: no interaction can produce a device-bound assertion from this user.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location!.ToString();
            Assert.StartsWith("https://acme.app.tellma.com/signin-oidc", location, StringComparison.Ordinal);
            Assert.Contains("error=unmet_authentication_requirements", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Aal3_sends_a_device_bound_passkey_holder_to_the_login_page()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idaal3bound");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "bound@example.com");
            await AddPasskeyAsync(factory, user, deviceBound: true);

            using OidcFlowClient flow = new(factory);
            await EstablishSessionAsync(flow, distribution, "bound@example.com");

            using HttpResponseMessage response = await AuthorizeWithAal3Async(flow, distribution);

            // Step-up is possible for this user, so the browser goes to the login page offering
            // only the passkey ceremony, with the tier so the page explains what is needed.
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location!.ToString();
            Assert.Contains("/Identity/Account/Login", location, StringComparison.Ordinal);
            Assert.Contains("methods=passkey", location, StringComparison.Ordinal);

            // The tier travels by value: the page's device-bound guidance — and the assertion-time
            // check that refuses a synced credential — both key on it being aal3 specifically.
            Assert.Contains(
                "tier=" + Uri.EscapeDataString("urn:tellma:acr:aal3"), location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Prompt_none_returns_login_required_rather_than_the_login_page()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idpromptnone");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            using OidcFlowClient flow = new(factory);
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["prompt"] = "none",
            });

            // No session at all: a silent-auth iframe must hear the protocol error, never be
            // parked on an interactive page it cannot drive.
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri("/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri), UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            string location = response.Headers.Location!.ToString();
            Assert.StartsWith("https://acme.app.tellma.com/signin-oidc", location, StringComparison.Ordinal);
            Assert.Contains("error=login_required", location, StringComparison.Ordinal);
            Assert.DoesNotContain("/Identity/Account/Login", location, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Max_age_zero_forces_one_reauthentication_then_completes()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idmaxage0");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "ivan@example.com");

            using OidcFlowClient flow = new(factory);
            await EstablishSessionAsync(flow, distribution, "ivan@example.com");

            // max_age=0 (OIDC: equivalent to prompt=login) with an existing session: one forced
            // re-authentication, then the code must be issued — never an endless login bounce
            // (a strict elapsed-seconds freshness check could essentially never pass).
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["max_age"] = "0",
            });

            string authorizeUrl = "/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri);
            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Redirect, challengeResponse.StatusCode);
                loginUrl = challengeResponse.Headers.Location!.ToString();
                Assert.Contains("/Identity/Account/Login", loginUrl, StringComparison.Ordinal);
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync("ivan@example.com", loginUrl);
            using HttpResponseMessage completed = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
            string location = completed.Headers.Location!.ToString();
            Assert.StartsWith("https://acme.app.tellma.com/signin-oidc", location, StringComparison.Ordinal);
            Assert.Contains("code=", location, StringComparison.Ordinal);
        }

        /// <summary>Signs the browser in at aal1 through the ordinary email-code flow.</summary>
        private static async Task EstablishSessionAsync(
            OidcFlowClient flow, DistributionClientCredentials distribution, string email)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid",
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

            string returnUrl = await flow.SignInWithEmailCodeAsync(email, loginUrl);
            using HttpResponseMessage completed = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, completed.StatusCode);
        }

        /// <summary>Pushes and starts an authorization demanding the aal3 tier.</summary>
        private static async Task<HttpResponseMessage> AuthorizeWithAal3Async(
            OidcFlowClient flow, DistributionClientCredentials distribution)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string requestUri = await flow.PushAuthorizationRequestAsync(new Dictionary<string, string>
            {
                ["client_id"] = "acme",
                ["client_secret"] = distribution.BffClientSecret,
                ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                ["response_type"] = "code",
                ["scope"] = "openid",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["acr_values"] = "urn:tellma:acr:aal3",
            });

            return await flow.Browser.GetAsync(
                new Uri("/connect/authorize?client_id=acme&request_uri=" + Uri.EscapeDataString(requestUri), UriKind.Relative),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Registers a passkey record directly in the store, bypassing the WebAuthn ceremony.</summary>
        private static async Task AddPasskeyAsync(StandaloneFactory factory, TellmaIdentityUser user, bool deviceBound)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();
            TellmaIdentityUser tracked = (await userManager.FindByIdAsync(user.Id))!;

            // The backup-eligibility flag is what classifies a credential as device-bound.
            UserPasskeyInfo passkey = new(
                credentialId: RandomNumberGenerator.GetBytes(16),
                publicKey: RandomNumberGenerator.GetBytes(32),
                createdAt: DateTimeOffset.UtcNow,
                signCount: 0,
                transports: null,
                isUserVerified: true,
                isBackupEligible: !deviceBound,
                isBackedUp: !deviceBound,
                attestationObject: [],
                clientDataJson: []);

            IdentityResult result = await userManager.AddOrUpdatePasskeyAsync(tracked, passkey);
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
        }
    }
}
