// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.IntegrationTests.Flows;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     In-proc, the issuer shares the distribution's origin, so the management API's audience
    ///     is a fixed URN rather than an origin: a token minted for the distribution is refused at
    ///     the management API even though both live on one origin, and a management token is
    ///     accepted there.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InProcManagementAudienceTests(SqlServerFixture fixture)
    {
        private const string Origin = "http://localhost";
        private const string ServiceSecret = "acme-svc-secret-0123456789abcdef";

        /// <summary>The distribution the in-proc engine serves, from its seed entry.</summary>
        private static readonly Dictionary<string, string?> DistributionSeed = new()
        {
            ["TellmaIdentity:Seed:ApplyMigrations"] = "true",
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = "acme",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Distribution",
            ["TellmaIdentity:Seed:Clients:0:Origin"] = Origin,
            ["TellmaIdentity:Seed:Clients:0:ClientSecret"] = "acme-bff-secret-0123456789abcdef",
            ["TellmaIdentity:Seed:Clients:0:ServiceClientSecret"] = ServiceSecret,
        };

        /// <summary>A delivery-status body naming a subject nobody invited.</summary>
        private static readonly string[] UnknownSubject = ["unknown-sub"];

        [Fact]
        public async Task A_distribution_token_is_refused_and_a_management_token_accepted_in_proc()
        {
            using InProcFactory factory = DatabaseBackedFactory.CreateInProc(fixture, "idipaud", DistributionSeed);

            // The backend's token for its distribution carries both scopes and the origin alone.
            string distributionToken = await RequestTokenAsync(factory, "tellma_api tellma_identity", Origin);
            using (JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(distributionToken))
            {
                Assert.Equal([Origin], ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud"));
            }

            Assert.Equal(HttpStatusCode.Forbidden, await PostDeliveryStatusAsync(factory, distributionToken));

            // Its management token, asked for without a resource, carries the management audience.
            string managementToken = await RequestTokenAsync(factory, "tellma_identity", resource: null);
            using (JsonDocument payload = ClientCredentialsTests.DecodeJwtPayload(managementToken))
            {
                Assert.Equal(["urn:tellma:identity"], ClientCredentialsTests.ReadSingleOrArray(payload.RootElement, "aud"));
            }

            Assert.Equal(HttpStatusCode.OK, await PostDeliveryStatusAsync(factory, managementToken));
        }

        /// <summary>Requests a client-credentials token for the distribution's backend client.</summary>
        private static async Task<string> RequestTokenAsync(InProcFactory factory, string scope, string? resource)
        {
            Dictionary<string, string> parameters = new()
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "acme-svc",
                ["client_secret"] = ServiceSecret,
                ["scope"] = scope,
            };
            if (resource is not null)
            {
                parameters["resource"] = resource;
            }

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/id/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("access_token").GetString()!;
        }

        /// <summary>Calls the in-proc management API's delivery-status endpoint and returns its status.</summary>
        private static async Task<HttpStatusCode> PostDeliveryStatusAsync(InProcFactory factory, string token)
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/id/api/identity/v1/invitations/delivery-status", UriKind.Relative),
                new { subs = UnknownSubject },
                TestContext.Current.CancellationToken);
            return response.StatusCode;
        }
    }
}
