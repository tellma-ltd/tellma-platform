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
using Tellma.Identity.Data.Entities;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The distribution-facing read of what became of the invitations it raised.
    ///     <para>
    ///         The server maps no user to a tenant, so the only thing separating one distribution's
    ///         invitations from another's is which client raised them. That scoping is the whole
    ///         security model of this endpoint, and it is what these tests are mostly about.
    ///     </para>
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationDeliveryStatusApiTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task A_delivered_invitation_reports_what_the_provider_said()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idstatus");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            string sub = await InviteAsync(factory, distribution, "reader@example.com");
            await RecordDeliveredAsync(factory, sub);

            JsonElement result = Assert.Single(await ReadStatusAsync(factory, distribution, [sub]));

            Assert.Equal("Delivered", result.GetProperty("state").GetString());
            Assert.True(result.GetProperty("expectsDeliveryEvents").GetBoolean());
            Assert.NotEqual(JsonValueKind.Null, result.GetProperty("sentUtc").ValueKind);
        }

        [Fact]
        public async Task An_invitation_sent_by_a_transport_that_reports_nothing_says_so()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idnoevents");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            // The capturing sender in these tests reports no delivery events, exactly as an
            // on-premise SMTP relay does not.
            string sub = await InviteAsync(factory, distribution, "quiet@example.com");

            JsonElement result = Assert.Single(await ReadStatusAsync(factory, distribution, [sub]));

            // Sent is the end of the story here, and the flag is how a caller knows not to render
            // it as "waiting for confirmation" forever.
            Assert.Equal("Sent", result.GetProperty("state").GetString());
            Assert.False(result.GetProperty("expectsDeliveryEvents").GetBoolean());
        }

        [Fact]
        public async Task One_distribution_cannot_read_anothers_invitations()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idscoped");
            // Two genuinely different distributions: the default slug is shared, so provisioning
            // twice without naming them would make "theirs" the same client as "mine" and the
            // test would pass without ever exercising the scoping.
            DistributionClientCredentials mine = await TestData.ProvisionDistributionAsync(factory, "mine");
            DistributionClientCredentials theirs = await TestData.ProvisionDistributionAsync(factory, "theirs");

            string sub = await InviteAsync(factory, mine, "theirs@example.com");

            // The subject exists and holds a live invitation — just not one this caller raised.
            JsonElement result = Assert.Single(await ReadStatusAsync(factory, theirs, [sub]));

            // Identical to a subject that does not exist at all. Anything else would turn this
            // endpoint into a way to probe the global directory one subject at a time.
            Assert.Equal("NotFound", result.GetProperty("state").GetString());
            Assert.Equal(JsonValueKind.Null, result.GetProperty("sentUtc").ValueKind);

            JsonElement unknown = Assert.Single(
                await ReadStatusAsync(factory, theirs, [Guid.NewGuid().ToString("N")]));
            Assert.Equal("NotFound", unknown.GetProperty("state").GetString());
        }

        [Fact]
        public async Task Results_come_back_one_per_request_position_and_in_order()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idstatusbulk");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            string first = await InviteAsync(factory, distribution, "first@example.com");
            string second = await InviteAsync(factory, distribution, "second@example.com");
            string missing = Guid.NewGuid().ToString("N");

            // A repeat is answered twice, so the two lists line up positionally the way the invite
            // API's do rather than the caller having to match them up by subject.
            IReadOnlyList<JsonElement> results =
                await ReadStatusAsync(factory, distribution, [second, missing, first, second]);

            Assert.Equal(4, results.Count);
            Assert.Equal(second, results[0].GetProperty("sub").GetString());
            Assert.Equal(missing, results[1].GetProperty("sub").GetString());
            Assert.Equal(first, results[2].GetProperty("sub").GetString());
            Assert.Equal(second, results[3].GetProperty("sub").GetString());
            Assert.Equal("NotFound", results[1].GetProperty("state").GetString());
        }

        [Fact]
        public async Task An_accepted_invitation_reports_accepted_whatever_the_provider_said()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idaccepted");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);

            string sub = await InviteAsync(factory, distribution, "opened@example.com");
            await ConsumeAsync(factory, sub);

            JsonElement result = Assert.Single(await ReadStatusAsync(factory, distribution, [sub]));

            // The link was opened. What a provider reported about the mail carrying it is history.
            Assert.Equal("Accepted", result.GetProperty("state").GetString());
        }

        [Fact]
        public async Task The_endpoint_refuses_a_caller_it_cannot_scope_to()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idstatusauth");

            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage anonymous = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations/delivery-status", UriKind.Relative),
                new { subs = AnonymousProbe },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        }

        /// <summary>A throwaway subject list for the call that never gets past authentication.</summary>
        private static readonly string[] AnonymousProbe = ["whoever"];

        /// <summary>Invites one address and returns the subject the API reported.</summary>
        private static async Task<string> InviteAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution, string email)
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TokenAsync(factory, distribution));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new { users = new object[] { new { email, displayName = email, locale = "en" } } },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            await factory.Emails.WaitForBodyAsync(email);

            using var parsed = JsonDocument.Parse(body);
            return parsed.RootElement.GetProperty("results")[0].GetProperty("sub").GetString()!;
        }

        /// <summary>Reads delivery status for a set of subjects as one distribution.</summary>
        private static async Task<IReadOnlyList<JsonElement>> ReadStatusAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution, string[] subs)
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TokenAsync(factory, distribution));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations/delivery-status", UriKind.Relative),
                new { subs },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var parsed = JsonDocument.Parse(body);
            return [.. parsed.RootElement.GetProperty("results").EnumerateArray().Select(static e => e.Clone())];
        }

        /// <summary>Records a provider's delivery report against a user's invitation.</summary>
        private static Task RecordDeliveredAsync(StandaloneFactory factory, string subject)
        {
            return MutateAsync(factory, subject, row =>
            {
                row.ExpectsDeliveryEvents = true;
                row.DeliveryStatus = EmailDeliveryStatus.Delivered;
                row.DeliveryUpdatedUtc = DateTimeOffset.UtcNow;
                row.LastProviderEventId = "evt-1";
            });
        }

        /// <summary>Marks a user's invitation as redeemed.</summary>
        private static Task ConsumeAsync(StandaloneFactory factory, string subject)
        {
            return MutateAsync(factory, subject, static row => row.ConsumedUtc = DateTimeOffset.UtcNow);
        }

        /// <summary>Applies a change to a user's invitation row.</summary>
        private static async Task MutateAsync(
            StandaloneFactory factory, string subject, Action<SingleUseCode> mutate)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            SingleUseCode row = await store.Set<SingleUseCode>()
                .SingleAsync(
                    c => c.UserId == subject && c.Purpose == SingleUseCodePurpose.Invitation,
                    TestContext.Current.CancellationToken);

            mutate(row);
            await store.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Obtains a management-scope token for one distribution's backend client.</summary>
        private static async Task<string> TokenAsync(
            StandaloneFactory factory, DistributionClientCredentials distribution)
        {
            using HttpClient client = factory.CreateClient();
            using HttpResponseMessage response = await client.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = distribution.ServiceClientId,
                    ["client_secret"] = distribution.ServiceClientSecret,
                    ["scope"] = "tellma_identity",
                }),
                TestContext.Current.CancellationToken);

            using var token = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return token.RootElement.GetProperty("access_token").GetString()!;
        }
    }
}
