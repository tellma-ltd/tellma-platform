// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Identity.IntegrationTests.Infrastructure;

namespace Tellma.Identity.IntegrationTests.Flows
{
    /// <summary>
    ///     What a sign-in page permits its own forms to reach. Signing in is the last step of a
    ///     pending authorization, and the redirect that completes it runs through the authorization
    ///     endpoint and on to the client's callback — another origin. A browser applies
    ///     <c>form-action</c> to every hop of the navigation a submission produces, judged against
    ///     the document holding the form, so a page that fails to name that callback lets the user
    ///     sign in and then goes nowhere: a code is issued, the session is stamped, no error is
    ///     logged, and the page does not move.
    ///     <para>
    ///         The widening is earned per response and comes only from the registry: an ordinary
    ///         destination, a decoy path, and an unknown client each leave the bare directive
    ///         standing.
    ///     </para>
    /// </summary>
    [Collection(SqlServerCollectionDefinition.Name)]
    [Trait("Category", "Integration")]
    public sealed class SignInFormActionTests(SqlServerFixture fixture)
    {
        /// <summary>The callback the seeded client is registered with.</summary>
        private const string CallbackUri = "http://127.0.0.1/callback";

        /// <summary>The seeded client's id.</summary>
        private const string ClientId = "browser-app";

        /// <summary>The bare directive, as served to a page that named nothing.</summary>
        private const string BareDirective = "form-action 'self';";

        /// <summary>A client registered with a callback, and no consent screen in the way.</summary>
        private static readonly Dictionary<string, string?> ClientSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:Seed:Clients:0:ClientId"] = ClientId,
            ["TellmaIdentity:Seed:Clients:0:DisplayName"] = "Browser App",
            ["TellmaIdentity:Seed:Clients:0:Kind"] = "Native",
            ["TellmaIdentity:Seed:Clients:0:RedirectUris:0"] = CallbackUri,
        };

        /// <summary>Client ids are all it takes to register a provider and offer its button.</summary>
        private static readonly Dictionary<string, string?> ProviderSeed = new(StringComparer.Ordinal)
        {
            ["TellmaIdentity:ExternalProviders:Google:ClientId"] = "google-client-id",
            ["TellmaIdentity:ExternalProviders:Google:ClientSecret"] = "google-client-secret",
        };

        [Theory]
        [InlineData("/Identity/Account/Login")]
        [InlineData("/Identity/Account/EmailCode?email=someone%40example.com")]
        public async Task A_page_resuming_an_authorization_permits_that_clients_callback(string page)
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idsignincsp" + (page.Contains("EmailCode", StringComparison.Ordinal) ? "code" : "login"), ClientSeed);

            string policy = await PolicyOfAsync(factory, page + Separator(page) + "returnUrl=" + Uri.EscapeDataString(AuthorizeUrl(ClientId)));

            Assert.Contains("form-action 'self' " + CallbackUri + ";", policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_ordinary_local_destination_widens_nothing()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idsignincsplocal", ClientSeed);

            string policy = await PolicyOfAsync(
                factory, "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString("/Identity/Manage/Passkeys"));

            Assert.Contains(BareDirective, policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_path_that_merely_ends_in_the_authorization_endpoint_widens_nothing()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idsignincspdecoy", ClientSeed);

            // The endpoint is matched whole, not by suffix. A route that happens to end in the same
            // text is not the authorization endpoint and must not borrow its widening.
            string policy = await PolicyOfAsync(
                factory,
                "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString("/decoy" + AuthorizeUrl(ClientId)));

            Assert.Contains(BareDirective, policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_unregistered_client_widens_nothing()
        {
            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idsignincspunknown", ClientSeed);

            // Nothing comes off the URL itself: the callbacks are read from the registration, so a
            // client that has none has nothing to contribute.
            string policy = await PolicyOfAsync(
                factory,
                "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(AuthorizeUrl("no-such-client")));

            Assert.Contains(BareDirective, policy, StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_provider_origins_and_the_callback_are_both_permitted()
        {
            Dictionary<string, string?> seed = new(ClientSeed, StringComparer.Ordinal);
            foreach ((string key, string? value) in ProviderSeed)
            {
                seed[key] = value;
            }

            using StandaloneFactory factory = await DatabaseBackedFactory.CreateStandaloneAsync(
                fixture, "idsignincspboth", seed);

            // One page, two widenings: the federated buttons it renders and the callback it is
            // signing in for. Whichever were applied second replacing the first would leave the
            // other silently unreachable, which is a button that does nothing over a clean log.
            string policy = await PolicyOfAsync(
                factory, "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(AuthorizeUrl(ClientId)));

            Assert.Contains("https://accounts.google.com", policy, StringComparison.Ordinal);
            Assert.Contains(CallbackUri, policy, StringComparison.Ordinal);
        }

        /// <summary>Fetches a page and returns the policy it was served with.</summary>
        /// <param name="factory">The running host.</param>
        /// <param name="url">The relative page URL.</param>
        /// <returns>The <c>Content-Security-Policy</c> header value.</returns>
        private static async Task<string> PolicyOfAsync(StandaloneFactory factory, string url)
        {
            using OidcFlowClient flow = new(factory);
            using HttpResponseMessage response = await flow.Browser.GetAsync(
                new Uri(url, UriKind.Relative), TestContext.Current.CancellationToken);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return Assert.Single(response.Headers.GetValues("Content-Security-Policy"));
        }

        /// <summary>An authorization request for a client, as a return URL would carry it.</summary>
        /// <param name="clientId">The client the request names.</param>
        /// <returns>The relative authorize URL.</returns>
        private static string AuthorizeUrl(string clientId)
        {
            return "/connect/authorize?client_id=" + clientId + "&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(CallbackUri)
                + "&scope=" + Uri.EscapeDataString("openid profile");
        }

        /// <summary>The character that starts a query, or continues one already begun.</summary>
        private static string Separator(string url)
        {
            return url.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        }
    }
}
