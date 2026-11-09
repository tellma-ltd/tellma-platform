// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Flows;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     Control-plane grants and the identity server's own audiences: the control plane names
    ///     a distribution by its origin, checked against the distribution-origin list at request
    ///     time; that origin is then the token's whole audience, so the token is refused by the
    ///     identity server's operator and management APIs; and a control-plane token requested
    ///     without a resource is accepted by the operator API.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ControlPlaneAudienceTests(SqlServerFixture fixture)
    {
        private const string ControlPlaneId = "tellma-control-plane";
        private const string ControlPlaneSecret = "control-plane-test-secret-0123456789";
        private const string Acme = "https://acme.app.tellma.com";

        /// <summary>The control-plane and management scopes together, as the control plane asks for them.</summary>
        private const string ControlPlaneScopes = "tellma_control_plane tellma_identity";

        private static readonly Dictionary<string, string?> ControlPlaneSeed = new()
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ControlPlaneId,
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "ControlPlane",
            ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = ControlPlaneSecret,
        };

        /// <summary>A delivery-status body naming a subject nobody invited.</summary>
        private static readonly string[] UnknownSubject = ["unknown-sub"];

        [Fact]
        public async Task A_control_plane_token_for_a_distribution_carries_only_its_origin_and_is_refused_by_both_apis()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idcpdist");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "opal@example.com");

            using JsonDocument tokens = await RequestTokenAsync(factory, ControlPlaneScopes, Acme);
            string token = tokens.RootElement.GetProperty("access_token").GetString()!;

            // The named origin is the whole audience: no platform audience rides along.
            using (JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(token))
            {
                Assert.Equal([Acme], ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud"));
                Assert.Equal("service", payload.RootElement.GetProperty("tellma_kind").GetString());
            }

            // The operator API and the management API both refuse it, though its scopes match.
            using HttpClient client = Authorized(factory, token);
            Assert.Equal(HttpStatusCode.Forbidden, await GetUserStatusAsync(client, user.Id));
            Assert.Equal(HttpStatusCode.Forbidden, await PostDeliveryStatusAsync(client));
            using HttpResponseMessage serviceAccount = await client.GetAsync(
                new Uri("/api/identity/v1/service-accounts/sa-anything", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.Forbidden, serviceAccount.StatusCode);
        }

        [Fact]
        public async Task A_control_plane_token_without_a_resource_is_accepted_by_the_operator_api()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idcpplat");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, "pearl@example.com");

            using JsonDocument tokens = await RequestTokenAsync(factory, ControlPlaneScopes, resource: null);
            string token = tokens.RootElement.GetProperty("access_token").GetString()!;

            // Its audiences are its scopes' platform audiences, and never a distribution's.
            using (JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(token))
            {
                string[] audiences = ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud");
                Assert.Contains("urn:tellma:control-plane", audiences);
                Assert.Contains("urn:tellma:identity", audiences);
                Assert.DoesNotContain(Acme, audiences);
            }

            using HttpClient client = Authorized(factory, token);
            Assert.Equal(HttpStatusCode.OK, await GetUserStatusAsync(client, user.Id));
            Assert.Equal(HttpStatusCode.OK, await PostDeliveryStatusAsync(client));
        }

        [Theory]
        [InlineData("https://initech.app.tellma.com")]
        [InlineData(Acme + "/17/mcp")]
        [InlineData(Acme + "/")]
        [InlineData("https://ACME.app.tellma.com")]
        public async Task A_control_plane_resource_outside_the_distribution_origin_list_is_refused_with_invalid_target(string resource)
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idcpoff");

            Assert.Equal("invalid_target", await RequestTokenErrorAsync(factory, ControlPlaneScopes, resource));

            // The refusal is audited, naming the client and what it asked for.
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext db = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();
            Data.Entities.AuditEvent audit = Assert.Single(await db.Set<Data.Entities.AuditEvent>()
                .Where(static e => e.Action == "ResourceRefused")
                .ToListAsync(TestContext.Current.CancellationToken));
            Assert.Equal(ControlPlaneId, audit.ClientId);
            using var details = JsonDocument.Parse(audit.DetailsJson!);
            Assert.Equal(resource, details.RootElement.GetProperty("requested").GetString());
        }

        [Theory]
        [InlineData("https://globex.app.tellma.com")]
        [InlineData("urn:tellma:control-plane")]
        [InlineData("urn:tellma:identity")]
        public async Task A_control_plane_token_naming_a_distribution_names_nothing_else(string other)
        {
            // Each resource is granted on its own; together they would be one token valid at two
            // distributions, or at a distribution and the identity server's own APIs.
            using StandaloneFactory factory = await CreateFactoryAsync("idcpcombo");

            Assert.Equal(
                "invalid_target",
                await RequestTokenErrorAsync(factory, ControlPlaneScopes, [Acme, other]));
        }

        [Fact]
        public async Task A_distribution_backend_token_naming_its_origin_cannot_also_name_the_issuer()
        {
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idsvccombo");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(
                [
                    new("grant_type", "client_credentials"),
                    new("client_id", distribution.ServiceClientId),
                    new("client_secret", distribution.ServiceClientSecret),
                    new("scope", "tellma_identity tellma_api"),
                    new("resource", Acme),
                    new("resource", "urn:tellma:identity"),
                ]),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            Assert.Equal("invalid_target", document.RootElement.GetProperty("error").GetString());
        }

        [Fact]
        public async Task A_control_plane_request_names_a_distribution_only_with_the_control_plane_scope()
        {
            using StandaloneFactory factory = await CreateFactoryAsync("idcpscope");

            Assert.Equal("invalid_target", await RequestTokenErrorAsync(factory, "tellma_identity", Acme));
        }

        [Fact]
        public async Task A_distribution_backend_token_naming_its_origin_is_refused_by_the_management_api()
        {
            // The backend holds both its origin and the management audience; naming its origin makes that
            // the whole audience, so the same token is no credential at the management API.
            using StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, "idsvcaud");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            using HttpClient tokenClient = factory.CreateClient();
            using HttpResponseMessage response = await tokenClient.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["scope"] = "tellma_identity tellma_api",
                    ["resource"] = Acme,
                }),
                TestContext.Current.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            using var tokens = JsonDocument.Parse(body);
            string token = tokens.RootElement.GetProperty("access_token").GetString()!;

            using (JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(token))
            {
                Assert.Equal([Acme], ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud"));
            }

            using HttpClient client = Authorized(factory, token);
            Assert.Equal(HttpStatusCode.Forbidden, await PostDeliveryStatusAsync(client));
        }

        /// <summary>
        ///     Boots a host with the control plane seeded and two distributions onboarded, whose
        ///     origins form the distribution-origin list.
        /// </summary>
        private async Task<StandaloneFactory> CreateFactoryAsync(string databasePrefix)
        {
            StandaloneFactory factory = DatabaseBackedFactory.CreateStandalone(fixture, databasePrefix, ControlPlaneSeed);
            await TestData.ProvisionDistributionAsync(factory, "acme");
            await TestData.ProvisionDistributionAsync(factory, "globex");
            return factory;
        }

        /// <summary>Requests a control-plane client-credentials token, expecting success.</summary>
        private static async Task<JsonDocument> RequestTokenAsync(StandaloneFactory factory, string scope, string? resource)
        {
            using HttpResponseMessage response = await PostTokenRequestAsync(factory, scope, resource);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body);
        }

        /// <summary>Requests a control-plane client-credentials token, expecting an error, and returns its code.</summary>
        private static Task<string?> RequestTokenErrorAsync(StandaloneFactory factory, string scope, string resource)
        {
            return RequestTokenErrorAsync(factory, scope, [resource]);
        }

        /// <summary>Requests a control-plane client-credentials token naming several resources, expecting an error.</summary>
        private static async Task<string?> RequestTokenErrorAsync(StandaloneFactory factory, string scope, string[] resources)
        {
            using HttpResponseMessage response = await PostTokenRequestAsync(factory, scope, resources);
            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("error").GetString();
        }

        /// <summary>Posts a control-plane client-credentials request naming at most one resource.</summary>
        private static Task<HttpResponseMessage> PostTokenRequestAsync(StandaloneFactory factory, string scope, string? resource)
        {
            return PostTokenRequestAsync(factory, scope, resource is null ? [] : [resource]);
        }

        /// <summary>Posts a control-plane client-credentials request, one <c>resource</c> parameter per resource.</summary>
        private static async Task<HttpResponseMessage> PostTokenRequestAsync(StandaloneFactory factory, string scope, string[] resources)
        {
            List<KeyValuePair<string, string>> parameters =
            [
                new("grant_type", "client_credentials"),
                new("client_id", ControlPlaneId),
                new("client_secret", ControlPlaneSecret),
                new("scope", scope),
            ];
            parameters.AddRange(resources.Select(static resource => new KeyValuePair<string, string>("resource", resource)));

            using HttpClient client = factory.CreateClient();
            return await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);
        }

        /// <summary>A client sending a bearer token.</summary>
        private static HttpClient Authorized(StandaloneFactory factory, string token)
        {
            HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return client;
        }

        /// <summary>Calls the operator API's user lookup and returns its status.</summary>
        private static async Task<HttpStatusCode> GetUserStatusAsync(HttpClient client, string subject)
        {
            using HttpResponseMessage response = await client.GetAsync(
                new Uri("/api/identity/v1/users/" + Uri.EscapeDataString(subject), UriKind.Relative),
                TestContext.Current.CancellationToken);
            return response.StatusCode;
        }

        /// <summary>Calls the management API's delivery-status endpoint and returns its status.</summary>
        private static async Task<HttpStatusCode> PostDeliveryStatusAsync(HttpClient client)
        {
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/v1/invitations/delivery-status", UriKind.Relative),
                new { subs = UnknownSubject },
                TestContext.Current.CancellationToken);
            return response.StatusCode;
        }
    }
}
