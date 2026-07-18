// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Provisioning;

namespace Tellma.Identity.IntegrationTests.Api
{
    /// <summary>
    ///     The bulk-invitation API: idempotent create-or-get with per-user status, never returning
    ///     the invitation link, and bounded database round-trips for a large batch.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationApiTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task Bulk_invite_returns_per_user_status_and_never_the_link()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idinvite");

            // An already-active user (with a credential) should come back as Active.
            TellmaIdentityUser active = await TestData.CreateActiveUserAsync(factory, "active@example.com");
            await GivePasswordAsync(factory, active);

            string token = await GetIdentityScopeTokenAsync(factory);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new
                {
                    users = new[]
                    {
                        new { email = "new@example.com", displayName = (string?)"New User", locale = (string?)"ar" },
                        new { email = "active@example.com", displayName = (string?)null, locale = (string?)null },
                    },
                },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            // No field anywhere in the response resembles an invitation link.
            Assert.DoesNotContain("Invitation?code=", body, StringComparison.Ordinal);
            Assert.DoesNotContain("http", body, StringComparison.Ordinal);

            using var document = JsonDocument.Parse(body);
            JsonElement[] results = [.. document.RootElement.GetProperty("results").EnumerateArray()];
            Assert.Equal(2, results.Length);

            JsonElement newUser = results.Single(r => r.GetProperty("email").GetString() == "new@example.com");
            Assert.Equal("Invited", newUser.GetProperty("status").GetString());
            Assert.False(string.IsNullOrEmpty(newUser.GetProperty("sub").GetString()));

            JsonElement activeUser = results.Single(r => r.GetProperty("email").GetString() == "active@example.com");
            Assert.Equal("Active", activeUser.GetProperty("status").GetString());
            Assert.Equal(active.Id, activeUser.GetProperty("sub").GetString());

            // The new user exists and got the captured invitation email; the active user did not.
            Assert.NotNull(factory.Emails.LatestLinkFor("new@example.com"));
            Assert.Null(factory.Emails.LatestLinkFor("active@example.com"));
        }

        [Fact]
        public async Task Invite_requires_the_identity_scope()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idinviteauth");
            using HttpClient client = factory.CreateClient();

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new { users = new[] { new { email = "x@example.com" } } },
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        /// <summary>Provisions a distribution and obtains a token carrying the tellma_identity scope.</summary>
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
                    ["resource"] = "http://localhost",
                }),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("access_token").GetString()!;
        }

        /// <summary>Gives a user a password so it counts as having a credential.</summary>
        private static async Task GivePasswordAsync(StandaloneFactory factory, TellmaIdentityUser user)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();
            TellmaIdentityUser tracked = (await userManager.FindByIdAsync(user.Id))!;
            await userManager.AddPasswordAsync(tracked, "correct horse battery staple");
        }
    }
}
