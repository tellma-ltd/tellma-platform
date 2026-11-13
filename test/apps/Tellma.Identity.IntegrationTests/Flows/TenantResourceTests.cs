// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using OpenIddict.Abstractions;
using System.Net;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.AuthenticationPolicy;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Per-tenant resources under a granted origin, end to end: a client granted a
    ///     distribution's origin obtains a token whose audience is one tenant's resource, through
    ///     the code exchange, the refresh and the device grant; a refresh may narrow its grant but
    ///     never widen it; and every refusal is <c>invalid_target</c> with an audit row. No
    ///     interactive grant may carry the management scope.
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
            using OidcFlowClient flow = factory.CreateFlowClient();

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
            using OidcFlowClient flow = factory.CreateFlowClient();

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
            using OidcFlowClient flow = factory.CreateFlowClient();

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
            using OidcFlowClient flow = factory.CreateFlowClient();
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

        [Theory]
        [InlineData(Tenant17)]
        [InlineData(Origin)]
        [InlineData(null)]
        public async Task An_authorization_cannot_ask_for_the_management_scope(string? resource)
        {
            // The management API is a machine-to-machine surface, so no interactive client is
            // permitted its scope: a person's token is never a credential there, with a
            // distribution's resource or without one.
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantmgmt");
            using OidcFlowClient flow = factory.CreateFlowClient();
            (_, string challenge) = OidcFlowClient.CreatePkcePair();

            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(AuthorizeUrl(challenge, resource, "openid offline_access tellma_api tellma_identity"), UriKind.Relative),
                TestContext.Current.CancellationToken);

            // Refused before any interaction, for the scope, and never with a code.
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("invalid_request", html, StringComparison.Ordinal);
            Assert.Contains("not allowed to use the specified scope", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_device_authorization_cannot_ask_for_the_management_scope()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantdevmgmt");

            using JsonDocument device = await PostDeviceAuthorizationAsync(factory, resource: null, "openid tellma_identity");

            Assert.Equal(HttpStatusCode.BadRequest, (HttpStatusCode)device.RootElement.GetProperty("status").GetInt32());
            JsonElement body = device.RootElement.GetProperty("body");
            Assert.Equal("invalid_request", body.GetProperty("error").GetString());
            Assert.Contains("not allowed to use the specified scope", body.GetProperty("error_description").GetString(), StringComparison.Ordinal);
            Assert.False(body.TryGetProperty("device_code", out _));
        }

        [Fact]
        public async Task A_first_party_client_moves_between_tenants_without_consent_on_one_authorization()
        {
            // Consent is implicit for the platform's own clients, so binding remembered consent to
            // its places changes nothing they see: the next tenant goes straight through, and is
            // recorded on the authorization the session already holds for the client.
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantswitch");
            using OidcFlowClient flow = factory.CreateFlowClient();
            await AuthorizeAsync(factory, flow, "sami@example.com", Tenant17);

            (_, string challenge) = OidcFlowClient.CreatePkcePair();
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(AuthorizeUrl(challenge, Tenant18), UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            Assert.Contains("code=", response.Headers.Location!.ToString(), StringComparison.Ordinal);

            using IServiceScope scope = factory.Services.CreateScope();
            IOpenIddictApplicationManager applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
            IOpenIddictAuthorizationManager authorizations = scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>();
            object client = (await applications.FindByClientIdAsync(ClientId, TestContext.Current.CancellationToken))!;
            List<object> rows = [];
            await foreach (object row in authorizations.FindByApplicationIdAsync(
                (await applications.GetIdAsync(client, TestContext.Current.CancellationToken))!, TestContext.Current.CancellationToken))
            {
                rows.Add(row);
            }

            object authorization = Assert.Single(rows);
            Assert.Equal(
                [Tenant17, Tenant18],
                AuthorizationResources.Read(await authorizations.GetPropertiesAsync(authorization, TestContext.Current.CancellationToken)));
        }

        [Fact]
        public async Task An_authorization_naming_two_distributions_is_refused_with_invalid_target()
        {
            // The CLI holds both origins once both are onboarded; one user token valid at both
            // would carry acme's grant into globex, so the request is refused before any interaction.
            using StandaloneFactory factory = await CreateFactoryAsync("idtenanttwo");
            await TestData.ProvisionDistributionAsync(factory, "globex");
            using OidcFlowClient flow = factory.CreateFlowClient();
            (_, string challenge) = OidcFlowClient.CreatePkcePair();

            string url = AuthorizeUrl(challenge, Origin) + "&resource=" + Uri.EscapeDataString("https://globex.app.tellma.com");
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Null(response.Headers.Location);
            Assert.Contains("invalid_target", await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
            Assert.Equal(["https://globex.app.tellma.com"], await RefusedResourcesAsync(factory));
        }

        [Fact]
        public async Task A_code_presented_without_its_verifier_is_refused_before_its_resources_are_read()
        {
            // Someone holding an intercepted code but not its verifier learns nothing from the
            // resource they name: the request is invalid_grant whatever it asks for, and nothing is
            // audited as a refused resource under the client's name.
            using StandaloneFactory factory = await CreateFactoryAsync("idtenantprobe");
            using OidcFlowClient flow = factory.CreateFlowClient();
            (string code, string verifier) = await AuthorizeAsync(factory, flow, "pete@example.com", Tenant17);
            (string wrongVerifier, _) = OidcFlowClient.CreatePkcePair();

            foreach (string probe in (string[])[Tenant18, Origin, Tenant17])
            {
                Assert.Equal(
                    "invalid_grant",
                    await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
                    {
                        ["grant_type"] = "authorization_code",
                        ["client_id"] = ClientId,
                        ["code"] = code,
                        ["redirect_uri"] = RedirectUri,
                        ["code_verifier"] = wrongVerifier,
                        ["resource"] = probe,
                    }));
            }

            Assert.Empty(await RefusedResourcesAsync(factory));
            Assert.NotEqual(verifier, wrongVerifier);
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
            using OidcFlowClient flow = factory.CreateFlowClient();

            using JsonDocument device = await PostDeviceAuthorizationAsync(factory, Tenant17);
            Assert.Equal(HttpStatusCode.OK, (HttpStatusCode)device.RootElement.GetProperty("status").GetInt32());
            string deviceCode = device.RootElement.GetProperty("body").GetProperty("device_code").GetString()!;
            string userCode = device.RootElement.GetProperty("body").GetProperty("user_code").GetString()!;

            await DeviceFlowTests.ApproveUserCodeAsync(flow, userCode, "devi@example.com");

            // Redeeming the device code for more than was approved is refused and audited, and
            // redeems nothing: the same code then succeeds within its grant.
            Assert.Equal(
                "invalid_target",
                await flow.ExchangeExpectingErrorAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                    ["client_id"] = ClientId,
                    ["device_code"] = deviceCode,
                    ["resource"] = Origin,
                }));
            Assert.Equal([Origin], await RefusedResourcesAsync(factory));

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

        /// <summary>The CLI's authorization URL, naming at most one resource, and the given scopes.</summary>
        private static string AuthorizeUrl(string challenge, string? resource, string scope = "openid offline_access tellma_api")
        {
            Dictionary<string, string?> parameters = new()
            {
                ["client_id"] = ClientId,
                ["redirect_uri"] = RedirectUri,
                ["response_type"] = "code",
                ["scope"] = scope,
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["state"] = "state-1",
            };
            if (resource is not null)
            {
                parameters["resource"] = resource;
            }

            return QueryHelpers.AddQueryString("/connect/authorize", parameters);
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
        ///     Posts a device authorization request for the CLI naming at most one resource, and
        ///     returns the status and body as one document: <c>{ "status": …, "body": … }</c>.
        /// </summary>
        private static async Task<JsonDocument> PostDeviceAuthorizationAsync(
            StandaloneFactory factory, string? resource, string scope = "openid offline_access tellma_api")
        {
            Dictionary<string, string> parameters = new()
            {
                ["client_id"] = ClientId,
                ["scope"] = scope,
            };
            if (resource is not null)
            {
                parameters["resource"] = resource;
            }

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/device", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
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
