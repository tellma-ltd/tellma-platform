// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Tellma.Identity.E2E.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     An Arabic application sending its user to the identity server, in a real browser: to
    ///     sign in, with the language inside a pushed request, and to the account pages, with a
    ///     way back to the page the user left. The integration suite covers what the server
    ///     answers; what only a browser shows is that the page is laid out right to left, that
    ///     each click keeps the language, and that the links really navigate where they say.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class RequestedLanguageTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        /// <summary>The test client's secret, which exists only in this run's database.</summary>
        private const string ClientSecret = "e2e-arabic-client-secret";

        [Fact]
        public async Task A_pushed_request_for_arabic_signs_in_in_arabic_through_the_email_code()
        {
            const string email = "e2e-arabic@example.com";
            string callbackUri = server.CrossOriginAddress + IdentityServerFixtureBase.CallbackPath;
            await server.CreatePushedRequestClientAsync("e2e-arabic", ClientSecret, callbackUri);
            await server.CreateActiveUserAsync(email);

            // The language rides inside the pushed request. The browser is handed the client and
            // a reference, so nothing in its address bar says "Arabic" until the server does.
            string authorizeUrl = await PushAsync("e2e-arabic", callbackUri, uiLocales: "ar");

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress, Locale = "en-US" });

            await PlaywrightTracing.RunTracedAsync(
                context,
                nameof(A_pushed_request_for_arabic_signs_in_in_arabic_through_the_email_code),
                async () =>
                {
                    IPage page = await context.NewPageAsync();
                    await page.GotoAsync(authorizeUrl);

                    // The sign-in page: sent there with the language, and rendered in it, right
                    // to left, although the browser itself prefers English.
                    await page.WaitForUrlAsync(new Regex(@"/Identity/Account/Login\?.*ui_locales=ar"));
                    await ExpectArabicAsync(page);

                    // The page's own controls are found by their Arabic names, so this is the
                    // Arabic page and not an English one with its attributes flipped.
                    await page.GetByLabel("البريد الإلكتروني").FillAsync(email);
                    int delivered = server.Emails.CountFor(email);
                    await page.GetByRole(AriaRole.Button, new() { Name = "أرسل لي رمز تسجيل الدخول بالبريد" }).ClickAsync();

                    // The email-code step: a redirect the page made on the server, still Arabic.
                    await page.WaitForUrlAsync(new Regex(@"/Identity/Account/EmailCode\?.*ui_locales=ar"));
                    await ExpectArabicAsync(page);

                    // Entering the code returns to the client's callback, on another origin. The
                    // request the client pushed arrives there untouched by what the pages carried.
                    string code = await server.Emails.WaitForCodeAfterAsync(email, delivered);
                    await page.GetByLabel("الرمز", new() { Exact = true }).FillAsync(code);
                    await page.GetByRole(AriaRole.Button, new() { Name = "تحقق", Exact = true }).ClickAsync();

                    await page.WaitForUrlAsync(new Regex("^" + Regex.Escape(callbackUri) + @"\?"));
                    Assert.Contains("code=", page.Url, StringComparison.Ordinal);
                    Assert.DoesNotContain("ui_locales", page.Url, StringComparison.Ordinal);
                });
        }

        [Fact]
        public async Task The_account_entry_opens_in_arabic_and_its_back_link_returns_to_the_page_the_user_left()
        {
            // The application lives at the cross-origin address; the page the user left is there.
            await server.CreateOriginClientAsync("e2e-account", server.CrossOriginAddress);
            string returnUrl = server.CrossOriginAddress + IdentityServerFixtureBase.CallbackPath + "?order=42&tab=lines";
            string entry = "/account?section=sessions&client_id=e2e-account&ui_locales=ar&return_url=" + Uri.EscapeDataString(returnUrl);

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = server.BaseAddress,
                Locale = "en-US",
                StorageState = await server.SignedInStorageStateAsync(playwright.Browser),
            });

            await PlaywrightTracing.RunTracedAsync(
                context,
                nameof(The_account_entry_opens_in_arabic_and_its_back_link_returns_to_the_page_the_user_left),
                async () =>
                {
                    IPage page = await context.NewPageAsync();
                    await page.GotoAsync(entry);

                    // The section's page, in Arabic.
                    await page.WaitForUrlAsync(new Regex(@"/Identity/Manage/Sessions\?"));
                    await ExpectArabicAsync(page);

                    // Moving between the account pages keeps the language and the way back.
                    await page.Locator("nav a.tmi-nav-item[href*='/Identity/Manage/Passkeys']").ClickAsync();
                    await page.WaitForUrlAsync(new Regex(@"/Identity/Manage/Passkeys\?"));
                    await ExpectArabicAsync(page);

                    // The way back names the page the user left, and following it arrives there.
                    ILocator back = page.Locator("a.tmi-topbar-back");
                    Assert.Equal(returnUrl, await back.GetAttributeAsync("href"));
                    await back.ClickAsync();
                    await page.WaitForUrlAsync(new Regex("^" + Regex.Escape(returnUrl) + "$"));

                    // An address the client's registration does not cover is not followed: the
                    // link offered instead is the application's own front door.
                    await page.GotoAsync(
                        "/account?client_id=e2e-account&return_url=" + Uri.EscapeDataString("https://elsewhere.example/orders/42"));
                    await page.WaitForUrlAsync(new Regex(@"/Identity/Manage\?"));
                    Assert.Equal(server.CrossOriginAddress, await page.Locator("a.tmi-topbar-back").GetAttributeAsync("href"));
                });
        }

        /// <summary>Asserts that the page in the browser is Arabic and laid out right to left.</summary>
        /// <param name="page">The browser page.</param>
        /// <returns>A task that completes once the document says so.</returns>
        private static async Task ExpectArabicAsync(IPage page)
        {
            ILocator document = page.Locator("html");
            await Assertions.Expect(document).ToHaveAttributeAsync("lang", "ar");
            await Assertions.Expect(document).ToHaveAttributeAsync("dir", "rtl");

            // What the attribute asks for is what the browser did: the body really flows right
            // to left, which a stylesheet forcing a direction would contradict.
            Assert.Equal("rtl", await page.EvaluateAsync<string>("() => getComputedStyle(document.body).direction"));
        }

        /// <summary>Pushes an authorization request the way a client's backend does.</summary>
        /// <param name="clientId">The pushing client.</param>
        /// <param name="callbackUri">The client's registered callback.</param>
        /// <param name="uiLocales">The language list the request asks for.</param>
        /// <returns>The relative authorization URL the browser opens: the client and the reference only.</returns>
        private async Task<string> PushAsync(string clientId, string callbackUri, string uiLocales)
        {
            string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            using HttpClient backchannel = new() { BaseAddress = new Uri(server.BaseAddress) };
            using FormUrlEncodedContent form = new(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = ClientSecret,
                ["redirect_uri"] = callbackUri,
                ["response_type"] = "code",
                ["scope"] = "openid profile",
                ["code_challenge"] = challenge,
                ["code_challenge_method"] = "S256",
                ["ui_locales"] = uiLocales,
            });
            using HttpResponseMessage response = await backchannel.PostAsync(
                new Uri("/connect/par", UriKind.Relative), form, TestContext.Current.CancellationToken);

            string body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
            Assert.True(response.IsSuccessStatusCode, body);

            using var document = JsonDocument.Parse(body);
            string requestUri = document.RootElement.GetProperty("request_uri").GetString()!;
            return "/connect/authorize?client_id=" + clientId + "&request_uri=" + Uri.EscapeDataString(requestUri);
        }
    }
}
