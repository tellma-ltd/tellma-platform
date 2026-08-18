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
    ///     Where an invitation sends the user once it has been accepted.
    ///     <para>
    ///         The field was plumbed end to end but silently dropped anything absolute, so an
    ///         invited user finished setup on the authority's own account page with no route to the
    ///         product that invited them. It now works, and only for the origin the inviting client
    ///         is registered at.
    ///     </para>
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class InvitationReturnUrlTests(SqlServerFixture fixture)
    {
        [Fact]
        public async Task An_invitation_may_name_the_inviting_clients_own_origin()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idreturnok");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            JsonElement result = await InviteAsync(
                factory, distribution, "welcome@example.com", "https://acme.app.tellma.com/welcome");

            Assert.Equal("Invited", result.GetProperty("status").GetString());

            // Stored server-side, never in the emailed link, so the link cannot be edited into
            // pointing somewhere else.
            Assert.Equal(
                "https://acme.app.tellma.com/welcome",
                await StoredReturnUrlAsync(factory, "welcome@example.com"));
        }

        [Fact]
        public async Task An_invitation_naming_somewhere_else_is_refused_rather_than_quietly_dropped()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idreturnbad");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            JsonElement result = await InviteAsync(
                factory, distribution, "elsewhere@example.com", "https://evil.test/welcome");

            // A per-user error, consistent with the batch's other refusals — and emphatically not
            // the old behaviour, which accepted the invitation and discarded the destination, so a
            // caller had no way to discover its integration did not work.
            Assert.Equal(JsonValueKind.Null, result.GetProperty("status").ValueKind);
            Assert.Contains("return url", result.GetProperty("error").GetString()!, StringComparison.OrdinalIgnoreCase);

            // Refused before anything was created: no user, and so no invitation to strand.
            Assert.Empty(factory.Emails.Captured);
        }

        [Fact]
        public async Task A_local_destination_is_still_accepted()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idreturnlocal");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            JsonElement result = await InviteAsync(
                factory, distribution, "local@example.com", "/Identity/Manage/Passkeys");

            Assert.Equal("Invited", result.GetProperty("status").GetString());
        }

        [Fact]
        public async Task An_already_used_link_offers_the_way_in_rather_than_a_dead_end()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idreused");
            DistributionClientCredentials distribution = await TestData.ProvisionDistributionAsync(factory, "acme");

            await InviteAsync(factory, distribution, "returning@example.com", "https://acme.app.tellma.com/welcome");
            string link = await factory.Emails.WaitForLinkAsync("returning@example.com");

            // Accept it, then give the account a credential — the state a user is in once they have
            // finished setting up and come back to the email later for want of a bookmark.
            using HttpClient browser = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            using HttpResponseMessage first = await browser.GetAsync(Relative(link), TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, first.StatusCode);

            await GivePasswordAsync(factory, "returning@example.com");

            using HttpResponseMessage second = await browser.GetAsync(Relative(link), TestContext.Current.CancellationToken);

            // Sent on to sign in, carrying the destination, instead of the generic "invalid or
            // expired" page the same link produced before.
            Assert.Equal(HttpStatusCode.Redirect, second.StatusCode);
            string location = second.Headers.Location!.ToString();
            Assert.Contains("/Account/Login", location, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("acme.app.tellma.com", Uri.UnescapeDataString(location), StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_unknown_link_still_looks_exactly_like_an_expired_one()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idunknown");

            using HttpClient browser = factory.CreateClient(
                new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

            // A forged token: no redirect, no hint that it resolved to anything.
            using HttpResponseMessage response = await browser.GetAsync(
                new Uri("/Identity/Account/Invitation?code=" + Guid.NewGuid().ToString("N") + ".nonsense", UriKind.Relative),
                TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.DoesNotContain("Account/Login", body, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>The path and query of an absolute link, for the in-memory browser client.</summary>
        private static Uri Relative(string link)
        {
            return new Uri(new Uri(link).PathAndQuery, UriKind.Relative);
        }

        /// <summary>Gives a user a password, so the account has something to sign in with.</summary>
        private static async Task GivePasswordAsync(StandaloneFactory factory, string email)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> users =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser user = (await users.FindByEmailAsync(email))!;
            Assert.True((await users.AddPasswordAsync(user, "Str0ng!Passw0rd")).Succeeded);
        }

        /// <summary>The destination stored against a user's invitation.</summary>
        private static async Task<string?> StoredReturnUrlAsync(StandaloneFactory factory, string email)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            TellmaIdentityDbContext store = scope.ServiceProvider.GetRequiredService<TellmaIdentityDbContext>();

            string userId = await store.Set<TellmaIdentityUser>()
                .Where(u => u.Email == email)
                .Select(static u => u.Id)
                .SingleAsync(TestContext.Current.CancellationToken);

            return await store.Set<SingleUseCode>()
                .Where(c => c.UserId == userId && c.Purpose == SingleUseCodePurpose.Invitation)
                .Select(static c => c.ReturnUrl)
                .SingleAsync(TestContext.Current.CancellationToken);
        }

        /// <summary>Invites one address with a return url and returns its result element.</summary>
        private static async Task<JsonElement> InviteAsync(
            StandaloneFactory factory,
            DistributionClientCredentials distribution,
            string email,
            string returnUrl)
        {
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", await TokenAsync(factory, distribution));

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new { users = new object[] { new { email, displayName = email, locale = "en", returnUrl } } },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var parsed = JsonDocument.Parse(body);
            return parsed.RootElement.GetProperty("results")[0].Clone();
        }

        /// <summary>Obtains a management-scope token for a distribution's backend client.</summary>
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
