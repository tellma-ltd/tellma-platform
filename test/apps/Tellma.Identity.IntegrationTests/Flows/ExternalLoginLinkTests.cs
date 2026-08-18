// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text.RegularExpressions;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Connecting a provider to the account you are signed in as. The same callback serves this
    ///     and an ordinary federated sign-in, so the challenge says which it is: a link attempt
    ///     carries the account it is for, and an identity that already belongs to someone else is
    ///     then a conflict to report, never a session to swap into. Swapping it silently leaves a
    ///     person acting on an account they did not choose and cannot see they are on.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ExternalLoginLinkTests(SqlServerFixture fixture)
    {
        /// <summary>Client ids are all it takes to register a provider and offer its button.</summary>
        private static readonly Dictionary<string, string?> ProviderSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
        };

        /// <summary>The Google identity under test.</summary>
        private const string GoogleSubject = "google-subject-108154";

        [Fact]
        public async Task An_identity_owned_by_another_account_does_not_replace_the_session()
        {
            const string mine = "mine@example.com";
            const string theirs = "theirs@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinkswap");
            await TestData.CreateActiveUserAsync(factory, mine);
            await TestData.CreateActiveUserAsync(factory, theirs);

            // The Google identity already belongs to the other account, and its address is that
            // account's — so nothing about it points at the session below.
            await TestData.AddExternalLoginAsync(factory, theirs, "Google", GoogleSubject);

            using OidcFlowClient flow = new(factory);
            await SignInAsync(flow, mine);
            Assert.Equal(mine, await SignedInEmailAsync(flow));

            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, theirs, linkForEmail: mine);

            // The whole claim, pinned from both sides: the callback rendered the refusal rather
            // than redirecting into a sign-in — so a flow that broke before the conflict check
            // cannot pass here by accident — and the session is still the one that started the
            // link. Before this was refused, the callback found the identity's owner, signed the
            // browser in as them, and returned to the account pages showing that account's name —
            // which reads as the link having worked.
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            string html = await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("already connected to a different", html, StringComparison.Ordinal);
            Assert.Equal(mine, await SignedInEmailAsync(flow));
        }

        [Fact]
        public async Task An_identity_owned_by_another_account_says_so()
        {
            const string mine = "conflict-mine@example.com";
            const string theirs = "conflict-theirs@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinkconflict");
            await TestData.CreateActiveUserAsync(factory, mine);
            await TestData.CreateActiveUserAsync(factory, theirs);
            await TestData.AddExternalLoginAsync(factory, theirs, "Google", GoogleSubject);

            using OidcFlowClient flow = new(factory);
            await SignInAsync(flow, mine);

            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, theirs, linkForEmail: mine);
            string html = await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Told what happened, because the reader is the one person who can act on it — they
            // authenticated as that provider identity a moment ago. The other account is never
            // named: which account holds it is not theirs to learn.
            Assert.Contains("already connected to a different", html, StringComparison.Ordinal);
            Assert.DoesNotContain(theirs, html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Connecting_an_unclaimed_identity_keeps_the_session_and_links_it()
        {
            const string mine = "unclaimed@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinkfresh");
            await TestData.CreateActiveUserAsync(factory, mine);

            using OidcFlowClient flow = new(factory);
            await SignInAsync(flow, mine);

            // The ordinary case, which must keep working: an identity nobody holds attaches to
            // the account whose link attempt this is, and the session stays that account's.
            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(
                flow, "someone-else@example.com", linkForEmail: mine);

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal(mine, await SignedInEmailAsync(flow));
            Assert.True(await HasGoogleLinkAsync(factory, mine));
        }

        [Fact]
        public async Task Reconnecting_your_own_identity_is_harmless()
        {
            const string mine = "reconnect@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinkagain");
            await TestData.CreateActiveUserAsync(factory, mine);
            await TestData.AddExternalLoginAsync(factory, mine, "Google", GoogleSubject);

            using OidcFlowClient flow = new(factory);
            await SignInAsync(flow, mine);

            // Same account, same identity — the conflict rule must not catch this, or a user
            // could never re-present a provider they had already connected. The redirect is the
            // discriminating half: a wrongful refusal would render an error page while leaving
            // the session just as intact.
            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, mine, linkForEmail: mine);

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal(mine, await SignedInEmailAsync(flow));
        }

        [Fact]
        public async Task A_sign_in_with_a_linked_identity_signs_in_its_owner()
        {
            const string owner = "signin-owner@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinksignin");
            await TestData.CreateActiveUserAsync(factory, owner);
            await TestData.AddExternalLoginAsync(factory, owner, "Google", GoogleSubject);

            using OidcFlowClient flow = new(factory);

            // No session and no declared link: the ordinary federated sign-in, which the conflict
            // rule must leave exactly as it was — a redirect onward, signed in as the owner.
            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, owner);

            Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
            Assert.Equal("/Identity/Manage/ExternalLogins", callback.Headers.Location?.ToString());
            Assert.Equal(owner, await SignedInEmailAsync(flow));
        }

        [Fact]
        public async Task A_disabled_owners_identity_does_not_sign_in()
        {
            const string owner = "signin-disabled@example.com";

            using StandaloneFactory factory = await CreateFactoryAsync("idlinkdisabled");
            TellmaIdentityUser user = await TestData.CreateActiveUserAsync(factory, owner);
            await TestData.AddExternalLoginAsync(factory, owner, "Google", GoogleSubject);
            await TestData.SetLifecycleStateAsync(factory, user, UserLifecycleState.Disabled);

            using OidcFlowClient flow = new(factory);
            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, owner);

            // Refused with the generic failure: an account that cannot sign in does not sign in
            // through a provider either, and the page says no more than that.
            Assert.Equal(HttpStatusCode.OK, callback.StatusCode);
            string html = await callback.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.Contains("could not be completed", html, StringComparison.Ordinal);
        }

        /// <summary>Boots a host with Google configured.</summary>
        private async Task<StandaloneFactory> CreateFactoryAsync(string databasePrefix)
        {
            StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, databasePrefix, ProviderSeed);

            // Mints the provider assertion the callback reads, since the round trip to Google
            // itself is the one part of this flow a test cannot make.
            factory.ServiceOverrides.Add(static services =>
                services.AddSingleton<IStartupFilter, ExternalProviderStub>());

            return factory;
        }

        /// <summary>Establishes a session for a user through the email-code flow.</summary>
        private static async Task SignInAsync(OidcFlowClient flow, string email)
        {
            await flow.SignInWithEmailCodeAsync(
                email, "/Identity/Account/Login?returnUrl=%2FIdentity%2FManage%2FIndex");
        }

        /// <summary>Asserts a provider identity, then completes the callback that acts on it.</summary>
        /// <param name="flow">The browser-role client.</param>
        /// <param name="providerEmail">The address the provider asserts.</param>
        /// <param name="linkForEmail">The account linking, when the challenge declared a link.</param>
        /// <returns>The callback's response.</returns>
        private static async Task<HttpResponseMessage> CompleteExternalCallbackAsync(
            OidcFlowClient flow, string providerEmail, string? linkForEmail = null)
        {
            using (HttpResponseMessage assertion = await flow.Browser.GetAsync(
                new Uri(
                    ExternalProviderStub.AssertionUrl("Google", GoogleSubject, providerEmail, linkForEmail),
                    UriKind.Relative),
                TestContext.Current.CancellationToken))
            {
                Assert.Equal(HttpStatusCode.NoContent, assertion.StatusCode);
            }

            return await flow.Browser.GetAsync(
                new Uri(
                    "/Identity/Account/ExternalLogin?handler=Callback&returnUrl="
                    + Uri.EscapeDataString("/Identity/Manage/ExternalLogins"),
                    UriKind.Relative),
                TestContext.Current.CancellationToken);
        }

        /// <summary>The address the account pages show for the current session.</summary>
        private static async Task<string> SignedInEmailAsync(OidcFlowClient flow)
        {
            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/Index", UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.True(page.IsSuccessStatusCode, $"The profile page answered {page.StatusCode}.");
            string html = await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // Read from the element the layout dedicates to the session's user, not from the
            // first address-shaped string anywhere in the page: other addresses — a linked
            // provider's label, say — may render before it.
            Match found = Regex.Match(html, "tmi-nav-user-email[^>]*>(?<email>[^<]+)<");
            Assert.True(found.Success, "The account pages named no signed-in user.");
            return found.Groups["email"].Value.Trim();
        }

        /// <summary>Whether a user holds a Google link.</summary>
        private static async Task<bool> HasGoogleLinkAsync(StandaloneFactory factory, string email)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> userManager =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser user = (await userManager.FindByEmailAsync(email))!;
            return (await userManager.GetLoginsAsync(user))
                .Any(static login => string.Equals(login.LoginProvider, "Google", StringComparison.Ordinal));
        }
    }
}
