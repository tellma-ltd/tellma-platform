// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     Gender set at invitation reaches the store, and reaches the Arabic email as a different
    ///     verb form — the whole reason the field exists.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationGenderTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Gender_is_optional_and_stored_when_supplied()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idgender");
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new
                {
                    users = new object[]
                    {
                        new { email = "hana@example.com", displayName = "Hana", locale = "ar", gender = "female" },
                        new { email = "sam@example.com", displayName = "Sam", locale = "ar" },
                        new { email = "alex@example.com", displayName = "Alex", locale = "ar", gender = "nonsense" },
                    },
                },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            Assert.Equal(UserGender.Female, (await users.FindByEmailAsync("hana@example.com"))!.Gender);

            // Omitted stays null rather than defaulting to a guess.
            Assert.Null((await users.FindByEmailAsync("sam@example.com"))!.Gender);

            // An unrecognized value is treated as unstated: a caller sending something we do not
            // model gets a neutrally addressed user, not a failed invitation.
            Assert.Null((await users.FindByEmailAsync("alex@example.com"))!.Gender);
        }

        [Fact]
        public async Task The_arabic_invitation_addresses_a_woman_in_the_feminine()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idgendermail");
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await GetIdentityScopeTokenAsync(factory));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new
                {
                    users = new object[]
                    {
                        new { email = "hana@example.com", displayName = "Hana", locale = "ar", gender = "female" },
                        new { email = "sam@example.com", displayName = "Sam", locale = "ar", gender = "male" },
                    },
                },
                TestContext.Current.CancellationToken);

            Assert.True(response.IsSuccessStatusCode);

            string feminine = await WaitForBodyAsync(factory, "hana@example.com");
            string neutral = await WaitForBodyAsync(factory, "sam@example.com");

            // Nothing in the invitation body is gendered yet, so both read the same today. What
            // this pins is that the rendering path resolves the select rather than leaking ICU
            // syntax into a delivered email — the failure this feature could plausibly ship with.
            Assert.DoesNotContain("{gender", feminine, StringComparison.Ordinal);
            Assert.DoesNotContain("{gender", neutral, StringComparison.Ordinal);
            Assert.DoesNotContain("select,", feminine, StringComparison.Ordinal);
        }

        /// <summary>Waits for the queued invitation to be delivered and returns its body.</summary>
        private static async Task<string> WaitForBodyAsync(StandaloneFactory factory, string email)
        {
            // Delivery is queued on a background worker, so the API response arrives first.
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (factory.Emails.LatestFor(email) is { } message)
                {
                    return message.TextBody;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            throw new InvalidOperationException($"No invitation was delivered to {email}.");
        }

        /// <summary>Obtains a management-scope token for the invitation API.</summary>
        private static async Task<string> GetIdentityScopeTokenAsync(StandaloneFactory factory)
        {
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory);
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
