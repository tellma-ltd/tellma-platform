// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     An end-session request the server refuses never ends anything: the user stays signed in,
    ///     no session is terminated and no termination is audited, so a page cannot sign a user out
    ///     everywhere by navigating them to a request with an unregistered post-logout redirect.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class EndSessionRefusalTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_refused_end_session_request_leaves_the_user_signed_in()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idendrefuse");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            await TestData.CreateActiveUserAsync(factory, "ellen@example.com");
            using OidcFlowClient flow = new(factory);
            await SignInAsync(flow, distribution, "ellen@example.com");

            string endSessionUrl = "/connect/endsession?client_id=acme&post_logout_redirect_uri="
                + Uri.EscapeDataString("https://evil.example/");
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(endSessionUrl, UriKind.Relative), TestContext.Current.CancellationToken);

            // Refused in place, on the error page, with no redirect anywhere.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);

            // The identity cookie still authenticates: the account pages answer instead of sending
            // the browser to sign in.
            using HttpResponseMessage manage = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, manage.StatusCode);

            // Nothing was terminated, so nothing was audited as terminated.
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Assert.False(await db.Set<Data.Entities.AuditEvent>()
                .AnyAsync(static e => e.Action == "SessionTerminated", TestContext.Current.CancellationToken));
        }

        /// <summary>Establishes an SSO session through the distribution's BFF client.</summary>
        private static async Task SignInAsync(OidcFlowClient flow, DistributionClientCredentials distribution, string email)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
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

            string returnUrl = await flow.SignInWithEmailCodeAsync(email, loginUrl);
            using HttpResponseMessage authorized = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, authorized.StatusCode);
            Assert.Contains("code=", authorized.Headers.Location!.Query, StringComparison.Ordinal);
        }
    }
}
