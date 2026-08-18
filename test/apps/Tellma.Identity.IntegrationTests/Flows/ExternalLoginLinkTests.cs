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
    ///     and an ordinary federated sign-in, so it has to tell the two apart: an identity that
    ///     already belongs to someone else is a conflict to report, never a session to swap into.
    ///     Swapping it silently leaves a person acting on an account they did not choose and cannot
    ///     see they are on.
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

            await CompleteExternalCallbackAsync(flow, theirs);

            // The whole claim. Before this was refused, the callback found the identity's owner,
            // signed the browser in as them, and returned to the account pages showing that
            // account's name — which reads as the link having worked.
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

            using HttpResponseMessage callback = await CompleteExternalCallbackAsync(flow, theirs);
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

            // The ordinary case, which must keep working: an identity nobody holds attaches to the
            // account that is signed in, and the session stays that account's throughout.
            await CompleteExternalCallbackAsync(flow, "someone-else@example.com");

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

            // Same account, same identity — the conflict rule must not catch this, or a user could
            // never re-present a provider they had already connected.
            await CompleteExternalCallbackAsync(flow, mine);

            Assert.Equal(mine, await SignedInEmailAsync(flow));
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
        /// <returns>The callback's response.</returns>
        private static async Task<HttpResponseMessage> CompleteExternalCallbackAsync(
            OidcFlowClient flow, string providerEmail)
        {
            using (HttpResponseMessage assertion = await flow.Browser.GetAsync(
                new Uri(ExternalProviderStub.AssertionUrl("Google", GoogleSubject, providerEmail), UriKind.Relative),
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

            Match found = Regex.Match(html, @"[\w.\-]+@example\.com");
            Assert.True(found.Success, "The account pages named no signed-in user.");
            return found.Value;
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
