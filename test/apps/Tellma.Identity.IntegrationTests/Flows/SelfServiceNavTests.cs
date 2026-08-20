// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     What the account pages offer. The navigation is the whole of the self-service surface —
    ///     nothing else in the product links into these pages — so an entry appearing there is the
    ///     decision to ship the feature behind it.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SelfServiceNavTests(SqlServerFixture fixture)
    {
        /// <summary>Client ids are all it takes to register a provider and offer its entry.</summary>
        private static readonly Dictionary<string, string?> ProviderSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
        };

        [Fact]
        public async Task The_navigation_does_not_offer_the_authenticator_app()
        {
            string html = await NavigationHtmlAsync("idnav", "nav@example.com", ProviderSeed);

            // Enrolling an authenticator stores a secret nothing ever asks for: no sign-in path
            // challenges for a time-based code, and the enrollment reissues recovery codes, so a
            // return visit costs the user the ones they saved. Delete this test in the change that
            // adds the challenge — failing here is the reminder that the entry can come back.
            Assert.DoesNotContain("/Identity/Manage/EnableAuthenticator", html, StringComparison.Ordinal);

            // The rest of the navigation is unaffected, so this pins an omission rather than a
            // page that stopped rendering its nav at all.
            Assert.Contains("/Identity/Manage/Passkeys", html, StringComparison.Ordinal);
            Assert.Contains("/Identity/Manage/ExternalLogins", html, StringComparison.Ordinal);
            Assert.Contains("/Identity/Manage/Sessions", html, StringComparison.Ordinal);
        }

        [Fact]
        public async Task External_logins_are_offered_only_where_a_provider_is_configured()
        {
            string configured = await NavigationHtmlAsync("idnavfed", "navfed@example.com", ProviderSeed);
            string none = await NavigationHtmlAsync("idnavnofed", "navnofed@example.com", overrides: null);

            // The page is one row per configured provider, so with none configured there is nothing
            // on it. An entry that leads to an empty page is an offer the deployment cannot keep.
            Assert.Contains("/Identity/Manage/ExternalLogins", configured, StringComparison.Ordinal);
            Assert.DoesNotContain("/Identity/Manage/ExternalLogins", none, StringComparison.Ordinal);

            // Both navs are otherwise whole — this is one conditional entry, not a broken render.
            Assert.Contains("/Identity/Manage/Sessions", none, StringComparison.Ordinal);
        }

        /// <summary>Signs a fresh user in and returns the account pages' rendered navigation.</summary>
        private async Task<string> NavigationHtmlAsync(
            string prefix, string email, IReadOnlyDictionary<string, string?>? overrides)
        {
            using StandaloneFactory factory =
                await DatabaseBackedFactory.CreateStandaloneAsync(fixture, prefix, overrides);
            await TestData.CreateActiveUserAsync(factory, email);

            using OidcFlowClient flow = new(factory);
            await flow.SignInWithEmailCodeAsync(email, "/Identity/Account/Login");

            using HttpResponseMessage profile = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/Index", UriKind.Relative), TestContext.Current.CancellationToken);
            return await profile.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        }
    }
}
