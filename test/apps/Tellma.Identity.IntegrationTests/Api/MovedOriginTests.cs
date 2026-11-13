// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using System.Net;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     A distribution that moves to a new origin leaves nothing behind at the old one: the
    ///     control plane and the CLI can no longer name the old origin, nor a tenant under it, and
    ///     the new origin works for both.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class MovedOriginTests(SqlServerFixture fixture)
    {
        private const string ControlPlaneId = "tellma-control-plane";
        private const string ControlPlaneSecret = "control-plane-test-secret-0123456789";
        private const string CliId = "tellma-cli";
        private const string CliRedirectUri = "http://127.0.0.1/callback";
        private const string OldOrigin = "https://acme.app.tellma.com";
        private const string NewOrigin = "https://acme.tellma.example";

        /// <summary>The control plane and the CLI, the two kinds of client the distribution-origin list reaches.</summary>
        private static readonly Dictionary<string, string?> Seed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ControlPlaneId,
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "ControlPlane",
            ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = ControlPlaneSecret,
            ["TellmaIdentity:Seed:Clients:1:ClientId"] = CliId,
            ["TellmaIdentity:Seed:Clients:1:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:1:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:1:RedirectUris:0"] = CliRedirectUri,
        };

        [Fact]
        public async Task The_old_origin_of_a_moved_distribution_is_refused_and_the_new_one_granted()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idmoved", Seed);
            await OnboardAsync(factory, OldOrigin);
            Assert.Equal(HttpStatusCode.OK, await ControlPlaneTokenStatusAsync(factory, OldOrigin));

            // Re-onboarding the distribution at its new origin moves it.
            await OnboardAsync(factory, NewOrigin);

            using (JsonDocument refused = await ControlPlaneTokenAsync(factory, OldOrigin))
            {
                Assert.Equal("invalid_target", refused.RootElement.GetProperty("error").GetString());
            }

            Assert.Equal(HttpStatusCode.OK, await ControlPlaneTokenStatusAsync(factory, NewOrigin));

            // The CLI is refused the old origin and a tenant under it before any interaction, and
            // is granted the new one (it is sent on to sign in).
            using OidcFlowClient flow = factory.CreateFlowClient();
            foreach (string resource in (string[])[OldOrigin, OldOrigin + "/17/mcp"])
            {
                using HttpResponseMessage response = await AuthorizeCliAsync(flow, resource);
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                Assert.Contains(
                    "invalid_target",
                    await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
                    StringComparison.Ordinal);
            }

            using (HttpResponseMessage granted = await AuthorizeCliAsync(flow, NewOrigin + "/17/mcp"))
            {
                Assert.Equal(HttpStatusCode.Redirect, granted.StatusCode);
            }

            // The registrations agree: the list holds the new origin alone.
            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictScopeManager scopes = scope.ServiceProvider.GetRequiredService<IOpenIddictScopeManager>();
            object api = (await scopes.FindByNameAsync("tellma_api", TestContext.Current.CancellationToken))!;
            Assert.Equal([NewOrigin], await scopes.GetResourcesAsync(api, TestContext.Current.CancellationToken));
        }

        /// <summary>Onboards the acme distribution at an origin.</summary>
        private static async Task OnboardAsync(StandaloneFactory factory, string origin)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IClientProvisioningService>().CreateDistributionAsync(
                "acme", new Uri(origin), backchannelLogoutUri: null, allowTokenExchange: false, TestContext.Current.CancellationToken);
        }

        /// <summary>Requests a control-plane token naming a distribution's origin, and returns its status.</summary>
        private static async Task<HttpStatusCode> ControlPlaneTokenStatusAsync(StandaloneFactory factory, string resource)
        {
            using HttpResponseMessage response = await PostControlPlaneTokenRequestAsync(factory, resource);
            return response.StatusCode;
        }

        /// <summary>Requests a control-plane token naming a distribution's origin, and returns the response body.</summary>
        private static async Task<JsonDocument> ControlPlaneTokenAsync(StandaloneFactory factory, string resource)
        {
            using HttpResponseMessage response = await PostControlPlaneTokenRequestAsync(factory, resource);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }

        /// <summary>Posts a control-plane client-credentials request naming one resource.</summary>
        private static async Task<HttpResponseMessage> PostControlPlaneTokenRequestAsync(StandaloneFactory factory, string resource)
        {
            using HttpClient client = factory.CreateClient();
            return await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = ControlPlaneId,
                    ["client_secret"] = ControlPlaneSecret,
                    ["scope"] = "tellma_control_plane",
                    ["resource"] = resource,
                }),
                TestContext.Current.CancellationToken);
        }

        /// <summary>Starts a CLI authorization naming one resource, without a session.</summary>
        private static async Task<HttpResponseMessage> AuthorizeCliAsync(OidcFlowClient flow, string resource)
        {
            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            string url = QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = CliId,
                ["redirect_uri"] = CliRedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["resource"] = resource,
            });
            return await flow.Browser.GetAsync(new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);
        }
    }
}
