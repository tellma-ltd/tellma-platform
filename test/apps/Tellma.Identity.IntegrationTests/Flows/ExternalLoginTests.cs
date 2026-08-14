// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     Starting a federated sign-in. The button is a form and the server answers it with a
    ///     redirect off this origin, so the page holding the button has to name the provider in its
    ///     own <c>form-action</c>: a browser applies that directive to every hop of a submission's
    ///     navigation, and refuses the redirect otherwise — with the server having issued it
    ///     perfectly happily, and the page simply not moving.
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class ExternalLoginTests(SqlServerFixture fixture)
    {
        /// <summary>Client ids are all it takes to register a provider and offer its button.</summary>
        private static readonly Dictionary<string, string?> ProviderSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
            ["TellmaIdentity:ExternalProviders:Microsoft:ClientId"] = "microsoft-client-id",
            ["TellmaIdentity:ExternalProviders:Microsoft:ClientSecret"] = "microsoft-client-secret",
        };

        [Theory]
        [InlineData("Google")]
        [InlineData("Microsoft")]
        public async Task The_login_page_permits_the_redirect_its_provider_button_produces(string provider)
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idfed" + provider.ToLowerInvariant(), ProviderSeed);

            using OidcFlowClient flow = new(factory);

            using HttpResponseMessage loginPage = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Login", UriKind.Relative), TestContext.Current.CancellationToken);
            Assert.True(loginPage.IsSuccessStatusCode);
            string policy = Assert.Single(loginPage.Headers.GetValues("Content-Security-Policy"));

            // Drive the provider's own form, not the page's first: the email-code form is above it.
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(
                loginPage, $"form[action*='provider={provider}']");

            using HttpResponseMessage challenge = await flow.PostFormAsync(action, fields);
            Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);

            // The invariant, tied to the redirect the server actually issues rather than to a
            // hard-coded provider URL: wherever the challenge sends the browser has to be a place
            // the originating page's policy allows it to go. A provider that moved its
            // authorization endpoint would fail here instead of silently on someone's screen.
            string destination = new Uri(challenge.Headers.Location!.ToString()).GetLeftPart(UriPartial.Authority);
            Assert.Contains("form-action ", policy, StringComparison.Ordinal);
            Assert.Contains(destination, policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_page_offering_no_provider_keeps_the_bare_directive()
        {
            // The widening is per-response and earned. With nothing configured there is no button,
            // so there is nothing to permit.
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(fixture, "idfednone");

            using OidcFlowClient flow = new(factory);
            using HttpResponseMessage loginPage = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Login", UriKind.Relative), TestContext.Current.CancellationToken);

            string policy = Assert.Single(loginPage.Headers.GetValues("Content-Security-Policy"));
            Assert.Contains("form-action 'self';", policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_request_that_disallows_federation_offers_no_provider_button()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idfedfiltered", ProviderSeed);

            using OidcFlowClient flow = new(factory);

            // The allow-list on the authorize redirect names only the email code, so the providers
            // are configured but not offerable. The button and the policy have to agree about
            // that — they are computed from one set precisely so they cannot disagree.
            using HttpResponseMessage loginPage = await flow.Browser.GetAsync(
                new Uri("/Identity/Account/Login?methods=email_code", UriKind.Relative),
                TestContext.Current.CancellationToken);

            string html = await loginPage.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            string policy = Assert.Single(loginPage.Headers.GetValues("Content-Security-Policy"));

            Assert.DoesNotContain("provider=Google", html, StringComparison.Ordinal);
            Assert.Contains("form-action 'self';", policy, StringComparison.Ordinal);
        }
    }
}
