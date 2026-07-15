// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Microsoft.AspNetCore.Mvc.Testing;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Tellma.Identity.IntegrationTests.Infrastructure
{
    /// <summary>
    ///     Drives OAuth/OIDC flows over raw HTTP the way a browser plus a confidential client
    ///     would: a cookie-holding "browser" client that never auto-follows redirects (so tests
    ///     assert every hop), plus back-channel form posts for PAR and the token endpoint.
    /// </summary>
    public sealed class OidcFlowClient : IDisposable
    {
        private readonly HttpClient _backchannel;
        private readonly StandaloneFactory _factory;

        /// <summary>Creates the protocol driver over a booted host.</summary>
        /// <param name="factory">The host under test.</param>
        public OidcFlowClient(StandaloneFactory factory)
        {
            _factory = factory;
            Browser = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = true,
            });
            _backchannel = factory.CreateClient(new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                HandleCookies = false,
            });
        }

        /// <summary>The cookie-holding browser-role client.</summary>
        public HttpClient Browser { get; }

        /// <summary>Generates a PKCE verifier and its S256 challenge.</summary>
        /// <returns>The pair.</returns>
        public static (string Verifier, string Challenge) CreatePkcePair()
        {
            string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            return (verifier, challenge);
        }

        /// <summary>Pushes an authorization request (PAR) and returns the <c>request_uri</c>.</summary>
        /// <param name="parameters">The full authorization parameters including client credentials.</param>
        /// <returns>The one-time request URI.</returns>
        public async Task<string> PushAuthorizationRequestAsync(Dictionary<string, string> parameters)
        {
            using HttpResponseMessage response = await _backchannel.PostAsync(
                new Uri("/connect/par", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            return document.RootElement.GetProperty("request_uri").GetString()!;
        }

        /// <summary>
        ///     Signs the browser in with an email one-time code, starting from a login-page URL
        ///     (typically the redirect target of an authorize request).
        /// </summary>
        /// <param name="email">The user's email; the code is read from the captured outbox.</param>
        /// <param name="loginUrl">The login page URL including its flow parameters.</param>
        /// <returns>The final post-sign-in redirect location (normally the authorize URL).</returns>
        public async Task<string> SignInWithEmailCodeAsync(string email, string loginUrl)
        {
            // 1. Load the login page and submit the email to request a code.
            using HttpResponseMessage loginPage = await Browser.GetAsync(
                new Uri(loginUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.True(loginPage.IsSuccessStatusCode, $"Login page failed: {loginPage.StatusCode}");

            (string action, Dictionary<string, string> fields) = await ParseFormAsync(loginPage);
            fields["Email"] = email;
            using HttpResponseMessage codeRedirect = await PostFormAsync(action, fields);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, codeRedirect.StatusCode);

            // 2. Load the code-entry page and submit the captured code.
            string codePageUrl = codeRedirect.Headers.Location!.ToString();
            using HttpResponseMessage codePage = await Browser.GetAsync(
                new Uri(codePageUrl, UriKind.RelativeOrAbsolute), TestContext.Current.CancellationToken);
            Assert.True(codePage.IsSuccessStatusCode, $"Code page failed: {codePage.StatusCode}");

            // The code is delivered by the background mail worker, so poll briefly for it.
            string? code = null;
            for (int attempt = 0; attempt < 50 && string.IsNullOrEmpty(code); attempt++)
            {
                code = _factory.Emails.LatestCodeFor(email);
                if (string.IsNullOrEmpty(code))
                {
                    await Task.Delay(20, TestContext.Current.CancellationToken);
                }
            }

            Assert.False(string.IsNullOrEmpty(code), "No sign-in code was captured.");

            (string verifyAction, Dictionary<string, string> verifyFields) = await ParseFormAsync(codePage);
            verifyFields["Code"] = code!;
            using HttpResponseMessage signedIn = await PostFormAsync(verifyAction, verifyFields);
            Assert.Equal(System.Net.HttpStatusCode.Redirect, signedIn.StatusCode);

            return signedIn.Headers.Location!.ToString();
        }

        /// <summary>Exchanges an authorization code at the token endpoint.</summary>
        /// <param name="parameters">The token request parameters.</param>
        /// <returns>The parsed token response (caller disposes).</returns>
        public async Task<JsonDocument> ExchangeAsync(Dictionary<string, string> parameters)
        {
            using HttpResponseMessage response = await _backchannel.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);
            return JsonDocument.Parse(body);
        }

        /// <summary>Posts a token request expecting a protocol error.</summary>
        /// <param name="parameters">The token request parameters.</param>
        /// <returns>The error code.</returns>
        public async Task<string?> ExchangeExpectingErrorAsync(Dictionary<string, string> parameters)
        {
            using HttpResponseMessage response = await _backchannel.PostAsync(
                new Uri("/connect/token", UriKind.Relative),
                new FormUrlEncodedContent(parameters),
                TestContext.Current.CancellationToken);

            Assert.False(response.IsSuccessStatusCode);
            using var document = JsonDocument.Parse(
                await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
            return document.RootElement.GetProperty("error").GetString();
        }

        /// <summary>Parses the first form on a page (action + fields including antiforgery).</summary>
        /// <param name="response">The page response.</param>
        /// <returns>The absolute-or-relative action URL and the form's named values.</returns>
        public static async Task<(string Action, Dictionary<string, string> Fields)> ParseFormAsync(HttpResponseMessage response)
        {
            string html = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            IBrowsingContext context = BrowsingContext.New(Configuration.Default);
            IDocument document = await context.OpenAsync(
                request => request.Content(html).Address(response.RequestMessage!.RequestUri),
                TestContext.Current.CancellationToken);

            IHtmlFormElement form = document.QuerySelector<IHtmlFormElement>("form")
                ?? throw new InvalidOperationException("The page contains no form.");

            Dictionary<string, string> fields = [];
            foreach (IHtmlInputElement input in form.QuerySelectorAll<IHtmlInputElement>("input[name]"))
            {
                // Unchecked checkboxes do not submit.
                if (string.Equals(input.Type, "checkbox", StringComparison.OrdinalIgnoreCase) && !input.IsChecked)
                {
                    continue;
                }

                fields[input.Name!] = input.Value;
            }

            return (form.Action, fields);
        }

        /// <summary>Posts a form the way a browser would.</summary>
        /// <param name="action">The form action URL.</param>
        /// <param name="fields">The form values.</param>
        /// <returns>The response.</returns>
        public async Task<HttpResponseMessage> PostFormAsync(string action, Dictionary<string, string> fields)
        {
            return await Browser.PostAsync(
                new Uri(action, UriKind.RelativeOrAbsolute),
                new FormUrlEncodedContent(fields),
                TestContext.Current.CancellationToken);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Browser.Dispose();
            _backchannel.Dispose();
        }
    }
}
