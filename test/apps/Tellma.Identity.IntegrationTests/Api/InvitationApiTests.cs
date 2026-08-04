// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;
using Tellma.Identity.Services.Invitations;
using Tellma.Identity.Services.Provisioning;
using Tellma.Identity.Services.Tokens;

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
            await WaitForLinkAsync(factory, "new@example.com");
            Assert.Null(factory.Emails.LatestLinkFor("active@example.com"));
        }

        [Fact]
        public async Task A_refused_user_gets_a_per_user_error_and_the_rest_of_the_batch_proceeds()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idinviteskip");

            // A disabled user sits in the middle of the batch; reactivating it must be a
            // deliberate operator action, never a side effect of a bulk invite.
            TellmaIdentityUser disabled = await TestData.CreateActiveUserAsync(factory, "disabled@example.com");
            await SetLifecycleStateAsync(factory, disabled, UserLifecycleState.Disabled);

            string token = await GetIdentityScopeTokenAsync(factory);
            using HttpClient client = factory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            using HttpResponseMessage response = await client.PostAsJsonAsync(
                new Uri("/api/identity/invitations", UriKind.Relative),
                new
                {
                    users = new[]
                    {
                        new { email = "before@example.com" },
                        new { email = "disabled@example.com" },
                        new { email = "after@example.com" },
                    },
                },
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            JsonElement[] results = [.. document.RootElement.GetProperty("results").EnumerateArray()];
            Assert.Equal(3, results.Length);

            // The refused user carries an error and neither a status nor a subject; the error does
            // not disclose the account's administrative state.
            JsonElement refused = results.Single(r => r.GetProperty("email").GetString() == "disabled@example.com");
            Assert.Equal(JsonValueKind.Null, refused.GetProperty("status").ValueKind);
            Assert.Equal(JsonValueKind.Null, refused.GetProperty("sub").ValueKind);
            string error = refused.GetProperty("error").GetString()!;
            Assert.Contains("operator", error, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Disabled", error, StringComparison.Ordinal);

            // Users before AND after the refusal were invited and their emails went out.
            foreach (string email in (string[])["before@example.com", "after@example.com"])
            {
                JsonElement invited = results.Single(r => r.GetProperty("email").GetString() == email);
                Assert.Equal("Invited", invited.GetProperty("status").GetString());
                Assert.Equal(JsonValueKind.Null, invited.GetProperty("error").ValueKind);
                await WaitForLinkAsync(factory, email);
            }

            // The disabled user stayed disabled.
            using IServiceScope scope = factory.Services.CreateScope();
            Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();
            Assert.Equal(
                UserLifecycleState.Disabled,
                (await userManager.FindByIdAsync(disabled.Id))!.LifecycleState);
        }

        [Fact]
        public async Task An_aborted_batch_still_delivers_the_links_it_already_issued()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idinviteabort");

            // The batch aborts partway exactly as a caller timeout or disconnect makes it: the
            // request's own token is cancelled while the second user is being processed. Users
            // already created hold live invitation links, so those links must still go out — the
            // original D2 trap was users created with no email ever sent. The service is driven
            // directly because the cancellation has to be the one it was handed, which is what
            // distinguishes a caller disconnect from any other fault.
            using CancellationTokenSource caller = new();
            factory.ServiceOverrides.Add(services =>
            {
                services.RemoveAll<IOneTimeTokenService>();
                services.AddScoped<IOneTimeTokenService>(provider =>
                    new AbortingTokenService(
                        ActivatorUtilities.CreateInstance<OneTimeTokenService>(provider), abortOnCall: 2, caller));
            });

            using (factory.CreateClient())
            {
                using IServiceScope scope = factory.Services.CreateScope();
                InvitationService invitations = scope.ServiceProvider.GetRequiredService<InvitationService>();

                IReadOnlyList<InvitationResultItem> results = await invitations.InviteAsync(
                    [
                        new InvitationRequestItem("first@example.com", null, null, null),
                        new InvitationRequestItem("second@example.com", null, null, null),
                        new InvitationRequestItem("third@example.com", null, null, null),
                    ],
                    createdByClientId: "acme",
                    caller.Token);

                // The user created before the abort got its link — the batch stopped taking on new
                // work but still delivered what it had already issued.
                await WaitForLinkAsync(factory, "first@example.com");

                // The aborted user is reported as an error, never as Invited: a success row for a
                // user whose link was never queued is the trap this path exists to avoid, and the
                // caller would record a membership no invitation can complete.
                InvitationResultItem aborted = results.Single(r => r.Email == "second@example.com");
                Assert.Null(aborted.Status);
                Assert.False(string.IsNullOrEmpty(aborted.Error));
                Assert.Null(factory.Emails.LatestLinkFor("second@example.com"));

                // The abort stopped the batch: the user after it was never processed.
                Assert.DoesNotContain(results, r => r.Email == "third@example.com");
                Assert.Null(factory.Emails.LatestLinkFor("third@example.com"));
            }
        }

        /// <summary>Cancels the caller's token on the n-th issuance, standing in for a disconnect.</summary>
        private sealed class AbortingTokenService(
            IOneTimeTokenService inner, int abortOnCall, CancellationTokenSource caller) : IOneTimeTokenService
        {
            private int _calls;

            public Task<string> IssueAsync(
                string userId,
                Data.Entities.SingleUseCodePurpose purpose,
                TimeSpan lifetime,
                string? returnUrl,
                string? createdByClientId,
                CancellationToken cancellationToken)
            {
                if (++_calls != abortOnCall)
                {
                    return inner.IssueAsync(userId, purpose, lifetime, returnUrl, createdByClientId, cancellationToken);
                }

                caller.Cancel();
                throw new OperationCanceledException(caller.Token);
            }

            public Task<OneTimeTokenContext?> RedeemAsync(
                string token,
                Data.Entities.SingleUseCodePurpose purpose,
                CancellationToken cancellationToken)
            {
                return inner.RedeemAsync(token, purpose, cancellationToken);
            }
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

        /// <summary>
        ///     Waits for a user's invitation link to be delivered. Invitations are handed to the
        ///     background dispatcher rather than sent inline, so delivery lands shortly after the
        ///     response — asserting immediately would be a race, not a check.
        /// </summary>
        private static async Task<string> WaitForLinkAsync(StandaloneFactory factory, string email)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                if (factory.Emails.LatestLinkFor(email) is { } link)
                {
                    return link;
                }

                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            throw new InvalidOperationException($"No invitation link was delivered to {email}.");
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

        /// <summary>Moves a user to the given lifecycle state directly in the store.</summary>
        private static async Task SetLifecycleStateAsync(
            StandaloneFactory factory, TellmaIdentityUser user, UserLifecycleState state)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<TellmaIdentityUser>>();
            TellmaIdentityUser tracked = (await userManager.FindByIdAsync(user.Id))!;
            tracked.LifecycleState = state;
            await userManager.UpdateAsync(tracked);
        }
    }
}
