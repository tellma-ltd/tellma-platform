// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using System.Net;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Per-tenant resources under a granted origin, end to end: a client granted a
    ///     distribution's origin obtains a token whose audience is one tenant's resource, through
    ///     the code exchange, the refresh and the device grant; a refresh may narrow its grant but
    ///     never widen it; and every refusal is <c>invalid_target</c> with an audit row.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class TenantResourceTests(SqlServerFixture fixture)
    {
        private const string ClientId = "tellma-cli";
        private const string RedirectUri = "http://127.0.0.1/callback";
        private const string Origin = "https://acme.app.tellma.com";
        private const string Tenant17 = Origin + "/17/mcp";
        private const string Tenant18 = Origin + "/18/mcp";

        /// <summary>A public CLI client, which onboarding grants each distribution's origin.</summary>
        private static readonly Dictionary<string, string?> CliSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ClientId,
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Tellma CLI",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Cli",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = RedirectUri,
        };

        [Fact]
        public async Task A_tenant_resource_flows_through_the_code_exchange_and_the_refresh_and_never_widens()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenant");
            using OidcFlowClient flow = new(factory);

            // The authorization names one tenant's resource; the code exchange names it again.
            (string code, string verifier) = await AuthorizeAsync(factory, flow, "tina@example.com", Tenant17);
            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier,
                ["resource"] = Tenant17,
            });
            Assert.Equal([Tenant17], Audiences(tokens));
            string refreshToken = RefreshToken(tokens);

            // A refresh without a resource keeps the tenant audience; one naming it keeps it too.
            using (JsonDocument refreshed = await flow.ExchangeAsync(Refresh(refreshToken)))
            {
                Assert.Equal([Tenant17], Audiences(refreshed));
                refreshToken = RefreshToken(refreshed);
            }

            using (JsonDocument refreshed = await flow.ExchangeAsync(Refresh(refreshToken, Tenant17)))
            {
                Assert.Equal([Tenant17], Audiences(refreshed));
                refreshToken = RefreshToken(refreshed);
            }

            // Another tenant's resource and the origin itself are both wider than the grant.
            Assert.Equal("invalid_target", await flow.ExchangeExpectingErrorAsync(Refresh(refreshToken, Tenant18)));
            Assert.Equal("invalid_target", await flow.ExchangeExpectingErrorAsync(Refresh(refreshToken, Origin)));

            // Each refusal is audited, naming the client and what it asked for.
            List<string> refused = await RefusedResourcesAsync(factory);
            Assert.Equal([Tenant18, Origin], refused);

            // A refused refresh redeemed nothing: the token still renews within its grant.
            using JsonDocument renewed = await flow.ExchangeAsync(Refresh(refreshToken));
            Assert.Equal([Tenant17], Audiences(renewed));
        }

        [Fact]
        public async Task A_refresh_narrows_an_origin_grant_to_one_tenant_and_keeps_the_grant()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantnarrow");
            using OidcFlowClient flow = new(factory);

            (string code, string verifier) = await AuthorizeAsync(factory, flow, "nora@example.com", Origin);
            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier,
            });
            Assert.Equal([Origin], Audiences(tokens));

            // The access token narrows to one tenant...
            string refreshToken;
            using (JsonDocument narrowed = await flow.ExchangeAsync(Refresh(RefreshToken(tokens), Tenant17)))
            {
                Assert.Equal([Tenant17], Audiences(narrowed));
                refreshToken = RefreshToken(narrowed);
            }

            // ...while the rotated refresh token keeps the original grant, so the next refresh may
            // ask for the origin again or for another of its tenants — but for no other origin.
            using (JsonDocument back = await flow.ExchangeAsync(Refresh(refreshToken, Origin)))
            {
                Assert.Equal([Origin], Audiences(back));
                refreshToken = RefreshToken(back);
            }

            using (JsonDocument other = await flow.ExchangeAsync(Refresh(refreshToken, Tenant18)))
            {
                Assert.Equal([Tenant18], Audiences(other));
                refreshToken = RefreshToken(other);
            }

            Assert.Equal(
                "invalid_target",
                await flow.ExchangeExpectingErrorAsync(Refresh(refreshToken, "https://globex.app.tellma.com")));
        }

        [Fact]
        public async Task A_code_exchange_cannot_widen_the_authorized_resource()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantcode");
            using OidcFlowClient flow = new(factory);

            (string code, string verifier) = await AuthorizeAsync(factory, flow, "cody@example.com", Tenant17);
            Assert.Equal(
                "invalid_target",
                await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code",
                    ["client_id"] = ClientId,
                    ["code"] = code,
                    ["redirect_uri"] = RedirectUri,
                    ["code_verifier"] = verifier,
                    ["resource"] = Origin,
                }));
        }

        [Theory]
        [InlineData(Origin + "/017/mcp")]
        [InlineData(Origin + "/17/mcp/")]
        [InlineData(Origin + "/17/other")]
        [InlineData(Origin + "/0/mcp")]
        [InlineData("https://globex.app.tellma.com/17/mcp")]
        public async Task An_authorization_request_for_an_ungranted_resource_is_refused_with_invalid_target(string resource)
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantauth");
            using OidcFlowClient flow = new(factory);
            (_, string challenge) = OidcFlowClient.CreatePkcePair();

            // Refused before any interaction: the request never reaches the sign-in page and never
            // carries a code anywhere. A request refused while it is validated is answered in
            // place, on the error page, rather than redirected.
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(AuthorizeUrl(challenge, resource), UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("invalid_target", html, StringComparison.Ordinal);

            Assert.Equal([resource], await RefusedResourcesAsync(factory));
        }

        [Fact]
        public async Task A_pushed_authorization_request_is_held_to_the_clients_grants()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idtenantpar", CliSeed);
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
            (_, string challenge) = OidcFlowClient.CreatePkcePair();

            // The BFF client holds its origin, so one of its tenants' resources may be pushed...
            using HttpResponseMessage granted = await PushAsync(factory, distribution, challenge, Tenant17);
            Assert.Equal(HttpStatusCode.Created, granted.StatusCode);

            // ...while another distribution's origin never gets a request_uri.
            using HttpResponseMessage refused = await PushAsync(factory, distribution, challenge, "https://globex.app.tellma.com");
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            using var document = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_target", document.RootElement.GetProperty("error").GetString());
            Assert.False(document.RootElement.TryGetProperty("request_uri", out _));
        }

        [Fact]
        public async Task A_device_grant_carries_its_tenant_resource_into_the_token()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantdev");
            await TestData.CreateActiveUserAsync(factory, "devi@example.com");
            using OidcFlowClient flow = new(factory);

            using JsonDocument device = await PostDeviceAuthorizationAsync(factory, Tenant17);
            Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)device.RootElement.GetProperty("status").GetInt32());
            string deviceCode = device.RootElement.GetProperty("body").GetProperty("device_code").GetString()!;
            string userCode = device.RootElement.GetProperty("body").GetProperty("user_code").GetString()!;

            await DeviceFlowTests.ApproveUserCodeAsync(flow, userCode, "devi@example.com");

            using JsonDocument tokens = await flow.ExchangeAsync(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                ["client_id"] = ClientId,
                ["device_code"] = deviceCode,
            });
            Assert.Equal([Tenant17], Audiences(tokens));

            // Its refresh is held to the same grant.
            Assert.Equal("invalid_target", await flow.ExchangeExpectingErrorAsync(Refresh(RefreshToken(tokens), Tenant18)));
        }

        [Fact]
        public async Task A_device_authorization_request_for_an_ungranted_resource_is_refused_with_invalid_target()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantdevbad");

            using JsonDocument device = await PostDeviceAuthorizationAsync(factory, Origin + "/017/mcp");

            Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)device.RootElement.GetProperty("status").GetInt32());
            Assert.Equal("invalid_target", device.RootElement.GetProperty("body").GetProperty("error").GetString());
            Assert.Equal([Origin + "/017/mcp"], await RefusedResourcesAsync(factory));
        }

        /// <summary>Boots a host with the CLI seeded and one distribution onboarded, which grants the CLI its origin.</summary>
        private async Task<StandaloneFactory> CreateFactoryAsync(string databasePrefix)
        {
            StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, databasePrefix, CliSeed);
            await TestData.ProvisionDistributionAsync(factory);
            return factory;
        }

        /// <summary>Pushes the BFF client's authorization request naming one resource.</summary>
        private static async Task<HttpResponseMessage> PushAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution, string challenge, string resource)
        {
            using HttpClient client = factory.CreateClient();
            return await client.PostAsync(
                new Uri("/connect/par", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = "acme",
                    ["client_secret"] = distribution.BffClientSecret,
                    ["redirect_uri"] = "https://acme.app.tellma.com/signin-oidc",
                    ["response_type"] = "code",
                    ["scope"] = "openid tellma_api",
                    ["code_challenge"] = challenge,
                    ["code_challenge_method"] = "S256",
                    ["resource"] = resource,
                }),
                TestContext.Current.CancellationToken);
        }

        /// <summary>The CLI's authorization URL, naming one resource.</summary>
        private static string AuthorizeUrl(string challenge, string resource)
        {
            return QueryHelpers.AddQueryString("/connect/authorize", new Dictionary<string, string?>
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = "openid offline_access tellma_api",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "state-1",
                ["resource"] = resource,
            });
        }

        /// <summary>Signs a new user in through the CLI's authorization request and returns the code.</summary>
        private static async Task<(string Code, string Verifier)> AuthorizeAsync(
            StandaloneFactory factory, OidcFlowClient flow, string email, string resource)
        {
            await TestData.CreateActiveUserAsync(factory, email);
            (string verifier, string challenge) = OidcFlowClient.CreatePkcePair();
            string authorizeUrl = AuthorizeUrl(challenge, resource);

            string loginUrl;
            using (HttpResponseMessage challengeResponse = await flow.Browser.GetAsync(
                new Uri(authorizeUrl, UriKind.Relative), TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.Redirect, challengeResponse.StatusCode);
                loginUrl = challengeResponse.Headers.Location!.ToString();
            }

            string returnUrl = await flow.SignInWithEmailCodeAsync(email, loginUrl);
            using HttpResponseMessage authorizeResponse = await flow.Browser.GetAsync(
                new Uri(returnUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, authorizeResponse.StatusCode);
            Dictionary<string, StringValues> query = QueryHelpers.ParseQuery(authorizeResponse.Headers.Location!.Query);
            string code = (string?)query["code"]
                ?? throw new InvalidOperationException("No code returned: " + authorizeResponse.Headers.Location);
            return (code, verifier);
        }

        /// <summary>A refresh request from the CLI, optionally naming a resource.</summary>
        private static Dictionary<string, string> Refresh(string refreshToken, string? resource = null)
        {
            Dictionary<string, string> parameters = new()
            {
                ["grant_type"] = "refresh_token",
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
            };
            if (resource is not null)
            {
                parameters["resource"] = resource;
            }

            return parameters;
        }

        /// <summary>The access token's audiences.</summary>
        private static string[] Audiences(JsonDocument tokens)
        {
            using JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(
                tokens.RootElement.GetProperty("access_token").GetString()!);
            return ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud");
        }

        /// <summary>The rotated refresh token.</summary>
        private static string RefreshToken(JsonDocument tokens)
        {
            return tokens.RootElement.GetProperty("refresh_token").GetString()!;
        }

        /// <summary>
        ///     Posts a device authorization request for the CLI naming one resource, and returns the
        ///     status and body as one document: <c>{ "status": …, "body": … }</c>.
        /// </summary>
        private static async Task<JsonDocument> PostDeviceAuthorizationAsync(StandaloneFactory factory, string resource)
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/device", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = ClientId,
                    ["scope"] = "openid offline_access tellma_api",
                    ["resource"] = resource,
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            return JsonDocument.Parse($"{{\"status\":{(int)response.StatusCode},\"body\":{body}}}");
        }

        /// <summary>The resources the audit trail records as refused, in order.</summary>
        private static async Task<List<string>> RefusedResourcesAsync(StandaloneFactory factory)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            List<Data.Entities.AuditEvent> rows = await db.Set<Data.Entities.AuditEvent>()
                .Where(static e => e.Action == "ResourceRefused")
                .OrderBy(static e => e.Id)
                .ToListAsync(TestContext.Current.CancellationToken);

            Assert.All(rows, static row => Assert.Equal(ClientId, row.ClientId));
            return [.. rows.Select(static row =>
            {
                using var details = JsonDocument.Parse(row.DetailsJson!);
                return details.RootElement.GetProperty("requested").GetString()!;
            })];
        }
    }
}
