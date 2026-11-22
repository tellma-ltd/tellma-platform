// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Net;
using Tellma.Identity.TestSupport;

namespace Tellma.Distro.Acme.TestSupport
{
    /// <summary>Signs a browser in through Acme's BFF, with the identity engine in process.</summary>
    internal static class AcmeSignIn
    {
        /// <summary>The administrator Acme's Development configuration seeds in the identity engine.</summary>
        public const string Administrator = "admin@localhost";

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>
        ///     Signs a user the engine holds in through the BFF, the administrator by default: the
        ///     login's redirect to the engine, the engine's email-code sign-in, and the authorization
        ///     response delivered to the host's callback, which starts the session.
        /// </summary>
        /// <param name="flow">The browser.</param>
        /// <param name="email">The user's email address; the administrator's when null.</param>
        /// <returns>A task that completes when the browser holds a session.</returns>
        public static async Task SignInAsync(OidcFlowClient flow, string? email = null)
        {
            ArgumentNullException.ThrowIfNull(flow);

            // --- The BFF's login pushes the request and sends the browser to the engine ---
            string authorize;
            using (HttpResponseMessage login = await flow.Browser.GetAsync(new Uri("/bff/login", UriKind.Relative), Token))
            {
                Assert.True(login.StatusCode == HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync(Token));
                authorize = login.Headers.Location!.ToString();
            }

            string loginPage;
            using (HttpResponseMessage challenge = await flow.Browser.GetAsync(new Uri(authorize, UriKind.RelativeOrAbsolute), Token))
            {
                Assert.True(challenge.StatusCode == HttpStatusCode.Redirect, await challenge.Content.ReadAsStringAsync(Token));
                loginPage = challenge.Headers.Location!.ToString();
            }

            // --- The engine signs the user in, then answers the authorization request ---
            string returned = await flow.SignInWithEmailCodeAsync(email ?? Administrator, loginPage);
            using HttpResponseMessage answer = await flow.Browser.GetAsync(new Uri(returned, UriKind.RelativeOrAbsolute), Token);

            // --- The callback: a redirect to it, or a form the browser posts to it ---
            using HttpResponseMessage callback = answer.StatusCode == HttpStatusCode.Redirect
                ? await flow.Browser.GetAsync(answer.Headers.Location!, Token)
                : await PostAsync(flow, answer);
            Assert.True(callback.StatusCode == HttpStatusCode.Redirect, await callback.Content.ReadAsStringAsync(Token));
            Assert.DoesNotContain("/bff/login", callback.Headers.Location!.ToString(), StringComparison.Ordinal);
        }

        /// <summary>Posts the form a page carries, as a browser posts an authorization response.</summary>
        /// <param name="flow">The browser.</param>
        /// <param name="page">The page.</param>
        /// <returns>The response.</returns>
        private static async Task<HttpResponseMessage> PostAsync(OidcFlowClient flow, HttpResponseMessage page)
        {
            Assert.True(page.StatusCode == HttpStatusCode.OK, await page.Content.ReadAsStringAsync(Token));
            (string action, Dictionary<string, string> fields) = await OidcFlowClient.ParseFormAsync(page, "form");
            return await flow.PostFormAsync(action, fields);
        }
    }
}
