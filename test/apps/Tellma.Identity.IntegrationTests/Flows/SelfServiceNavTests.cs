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
        [Fact]
        public async Task The_navigation_does_not_offer_the_authenticator_app()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idnav");
            await TestData.CreateActiveUserAsync(factory, "nav@example.com");

            using OidcFlowClient flow = new(factory);
            await flow.SignInWithEmailCodeAsync("nav@example.com", "/Identity/Account/Login");

            using HttpResponseMessage profile = await flow.Browser.GetAsync(
                new Uri("/Identity/Manage/Index", UriKind.Relative), TestContext.Current.CancellationToken);
            string html = await profile.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

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
    }
}
