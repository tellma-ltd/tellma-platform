// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Tellma.Identity.Data;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     The account's external-logins page: one row per provider whether or not it is linked,
    ///     each saying which account it is and offering the one action that applies to it.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ExternalLoginsPageTests(SqlServerFixture fixture)
    {
        /// <summary>Both providers configured, so both get a row.</summary>
        private static readonly Dictionary<string, string?> ProviderSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
            ["TellmaIdentity:ExternalProviders:Microsoft:ClientId"] = "microsoft-client-id",
            ["TellmaIdentity:ExternalProviders:Microsoft:ClientSecret"] = "microsoft-client-secret",
        };

        [Fact]
        public async Task A_linked_row_names_the_account_and_an_unlinked_one_offers_to_link()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idfedpage", ProviderSeed);
            await TestData.CreateActiveUserAsync(factory, "fedpage@example.com");
            await LinkAsync(factory, "fedpage@example.com", "Google", "f.nasser@aljood.org");

            string html = await PageHtmlAsync(factory, "fedpage@example.com");

            // The linked row: which account, not just which provider — the point of the subtitle
            // is telling two Google accounts apart.
            Assert.Contains("f.nasser@aljood.org", html, StringComparison.Ordinal);
            Assert.Contains("Linked", html, StringComparison.Ordinal);
            Assert.Contains("Unlink Google", html, StringComparison.Ordinal);

            // The unlinked row is present in the same list rather than in a separate stack of
            // buttons, and offers the opposite action.
            Assert.Contains("Not linked", html, StringComparison.Ordinal);
            Assert.Contains("Link Microsoft", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_link_made_before_the_account_was_recorded_shows_no_subtitle()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idfedlegacy", ProviderSeed);
            await TestData.CreateActiveUserAsync(factory, "fedlegacy@example.com");

            // What the store holds for every link made before the address was captured: the
            // provider's own name, which the row's title already says.
            await LinkAsync(factory, "fedlegacy@example.com", "Google", "Google");

            string html = await PageHtmlAsync(factory, "fedlegacy@example.com");

            // Linked, but with nothing to add — and specifically not "Not linked", which would be
            // a lie, nor "Google" twice, which would be noise.
            Assert.Contains("Linked", html, StringComparison.Ordinal);
            Assert.Contains("Unlink Google", html, StringComparison.Ordinal);
            Assert.DoesNotContain("Link Google", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Unlinking_the_only_sign_in_method_is_refused_as_a_warning()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idfedrefuse", ProviderSeed);
            await TestData.CreateActiveUserAsync(factory, "fedrefuse@example.com");
            await LinkAsync(factory, "fedrefuse@example.com", "Google", "only@example.com");

            using OidcFlowClient flow = new(factory);
            await flow.SignInWithEmailCodeAsync("fedrefuse@example.com", "/Identity/Account/Login");

            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/ExternalLogins", UriKind.Relative), TestContext.Current.CancellationToken);
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(
                page, "form[action*='handler=Remove']");

            using HttpResponseMessage refused = await flow.PostFormAsync(action, fields);
            string html = await refused.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

            // A refusal, drawn amber. It renders at all only because the warning glyph is
            // registered — the icon helper throws on an unknown name, and until this path existed
            // nothing in the product ever asked for a warning.
            Assert.True(refused.IsSuccessStatusCode, $"The refusal did not render: {refused.StatusCode}");
            Assert.Contains("tmi-notice-warning", html, StringComparison.Ordinal);
            Assert.Contains("You cannot remove your only sign-in method", html, StringComparison.Ordinal);

            // And it is a refusal, so the link is still there.
            Assert.Contains("only@example.com", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_deployment_with_no_provider_explains_itself_instead_of_rendering_nothing()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idfedempty");
            await TestData.CreateActiveUserAsync(factory, "fedempty@example.com");

            string html = await PageHtmlAsync(factory, "fedempty@example.com");

            // The nav hides the page in this state; whoever arrives by URL still gets a sentence.
            Assert.Contains("No external sign-in providers are available", html, StringComparison.Ordinal);
        }

        /// <summary>Records an external link the way the callback does, with a chosen account name.</summary>
        private static async Task LinkAsync(StandaloneFactory factory, string email, string provider, string account)
        {
            using IServiceScope scope = factory.Services.CreateScope();
            UserManager<TellmaIdentityUser> users =
                scope.ServiceProvider.GetRequiredService<UserManager<TellmaIdentityUser>>();

            TellmaIdentityUser user = (await users.FindByEmailAsync(email))!;
            IdentityResult result = await users.AddLoginAsync(
                user, new UserLoginInfo(provider, provider.ToLowerInvariant() + "-subject", account));
            Assert.True(result.Succeeded, string.Join("; ", result.Errors.Select(static e => e.Description)));
        }

        /// <summary>Signs the user in and returns the rendered external-logins page.</summary>
        private static async Task<string> PageHtmlAsync(StandaloneFactory factory, string email)
        {
            using OidcFlowClient flow = new(factory);
            await flow.SignInWithEmailCodeAsync(email, "/Identity/Account/Login");

            using HttpResponseMessage page = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/ExternalLogins", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.True(page.IsSuccessStatusCode, $"The page failed: {page.StatusCode}");
            return await page.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }
    }
}
