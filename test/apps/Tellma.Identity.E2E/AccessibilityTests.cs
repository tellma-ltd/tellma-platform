// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     The accessibility floor for every page a user can reach, at each width the layout
    ///     changes and in both text directions. These pages are seen rarely, briefly, and often
    ///     under stress — "I cannot get in" — which is exactly when an inaccessible page stops
    ///     being an inconvenience and starts being a lockout.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class AccessibilityTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        /// <summary>The widths that put one viewport in each tier, and on both sides of the seam.</summary>
        private static readonly (string Name, int Width, int Height)[] Viewports =
        [
            ("desktop", 1440, 900),
            ("split", 768, 900),
            ("mobile", 375, 812),
        ];

        /// <summary>Pages a visitor reaches without a session, by URL alone.</summary>
        private static readonly (string Label, string Url)[] Anonymous =
        [
            ("sign in", "/Identity/Account/Login"),
            ("sign in, step up", "/Identity/Account/Login?stepUp=true&tier=urn%3Atellma%3Aacr%3Aaal3"),
            ("sign in, no method available", "/Identity/Account/Login?methods=password"),
            ("sign out", "/Identity/Account/Logout"),
            ("signed out", "/Identity/Account/LoggedOut"),
            ("access denied", "/Identity/Account/AccessDenied"),
            ("device approved", "/Identity/Account/DeviceApproved"),
            ("forgot password", "/Identity/Account/ForgotPassword"),
            ("reset password, expired link", "/Identity/Account/ResetPassword?code=expired"),
            ("invitation, expired link", "/Identity/Account/Invitation?code=expired"),
            ("account recovery", "/Identity/Account/Tap"),
            ("administrator setup", "/Identity/Account/Setup"),
            ("error, nothing to report", "/Identity/Account/NoSuchPage"),
            ("error, protocol", "/connect/authorize?client_id=unregistered"),
        ];

        /// <summary>Pages behind a session.</summary>
        private static readonly (string Label, string Url)[] SignedIn =
        [
            ("profile", "/Identity/Manage/Index"),
            ("passkeys", "/Identity/Manage/Passkeys"),
            ("sessions", "/Identity/Manage/Sessions"),
            ("authenticator app", "/Identity/Manage/EnableAuthenticator"),
            ("device verification", "/connect/verify"),
            ("device verification, bad code", "/connect/verify?user_code=NOSUCHCODE"),
        ];

        /// <summary>Pages whose right-to-left rendering mixes in left-to-right content.</summary>
        private static readonly string[] ArabicPages =
        [
            "/Identity/Manage/Sessions",
            "/Identity/Manage/EnableAuthenticator",
        ];

        /// <summary>The anonymous pages, as theory rows.</summary>
        public static TheoryData<string, string> AnonymousPages => Rows(Anonymous);

        /// <summary>The signed-in pages, as theory rows.</summary>
        public static TheoryData<string, string> SignedInPages => Rows(SignedIn);

        [Theory]
        [MemberData(nameof(AnonymousPages))]
        public async Task An_anonymous_page_is_accessible(string label, string url)
        {
            await ScanAsync(label, url, signedIn: false);
        }

        [Theory]
        [MemberData(nameof(SignedInPages))]
        public async Task A_signed_in_page_is_accessible(string label, string url)
        {
            await ScanAsync(label, url, signedIn: true);
        }

        [Fact]
        public async Task The_code_entry_page_is_accessible_including_its_error()
        {
            const string email = "a11y-code@example.com";
            await server.CreateActiveUserAsync(email);

            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn: false);
            await PlaywrightTracing.RunTracedAsync(context, nameof(The_code_entry_page_is_accessible_including_its_error), async () =>
            {
                IPage page = await NewPageAsync(context);
                await page.GotoAsync("/Identity/Account/Login");
                await page.GetByLabel("Email").FillAsync(email);
                await page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in code" }).ClickAsync();
                await AxeAssertions.AssertNoViolationsAsync(page, "code entry");

                // The rejected state is the one carrying the summary, the focus move and the
                // invalid field — none of which the clean page exercises.
                await page.GetByLabel("Code").FillAsync("00000000");
                await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();
                await AxeAssertions.AssertNoViolationsAsync(page, "code entry, rejected");
            });
        }

        [Fact]
        public async Task The_consent_screen_is_accessible()
        {
            string callbackUri = server.CrossOriginAddress + IdentityServerFixtureBase.CallbackPath;
            await server.CreateConsentClientAsync("a11y-consent", "Accessibility Consent App", callbackUri);

            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn: true);
            await PlaywrightTracing.RunTracedAsync(context, nameof(The_consent_screen_is_accessible), async () =>
            {
                IPage page = await NewPageAsync(context);
                await page.GotoAsync(
                    "/connect/authorize?client_id=a11y-consent&response_type=code"
                    + "&redirect_uri=" + Uri.EscapeDataString(callbackUri)
                    + "&scope=" + Uri.EscapeDataString("openid profile email tellma_api")
                    + "&code_challenge=E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM&code_challenge_method=S256");
                await page.GetByRole(AriaRole.Heading, new() { Name = "Authorize application" }).WaitForAsync();
                await AxeAssertions.AssertNoViolationsAsync(page, "consent");
            });
        }

        [Fact]
        public async Task Arabic_renders_right_to_left_and_stays_accessible()
        {
            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn: true);
            await PlaywrightTracing.RunTracedAsync(context, nameof(Arabic_renders_right_to_left_and_stays_accessible), async () =>
            {
                IPage page = await NewPageAsync(context);

                // Sessions and the authenticator page are the ones mixing left-to-right content —
                // user agents, a base32 key — into right-to-left prose.
                foreach (string url in ArabicPages)
                {
                    await AssertArabicAsync(page, url);
                }
            });
        }

        [Fact]
        public async Task The_language_picker_does_not_move_the_page()
        {
            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn: false);
            await PlaywrightTracing.RunTracedAsync(context, nameof(The_language_picker_does_not_move_the_page), async () =>
            {
                IPage page = await NewPageAsync(context);
                await page.GotoAsync("/Identity/Account/Login");

                // The panel is taken out of flow precisely so opening it cannot push the form
                // the user is reading. Measured rather than eyeballed, at every tier.
                foreach ((string name, int width, int height) in Viewports)
                {
                    await page.SetViewportSizeAsync(width, height);
                    string moved = await page.EvaluateAsync<string>("""
                        () => {
                            const card = document.querySelector('.tmi-card');
                            const picker = document.querySelector('.tmi-lang');
                            const before = card.getBoundingClientRect();
                            picker.open = true;
                            const after = card.getBoundingClientRect();
                            picker.open = false;
                            return [after.x - before.x, after.y - before.y,
                                    after.width - before.width, after.height - before.height].join(',');
                        }
                        """);
                    Assert.Equal("0,0,0,0", moved);
                }
            });
        }

        [Fact]
        public async Task Every_page_reflows_without_scrolling_sideways()
        {
            // 320px is the width 1.4.10 actually names, narrower than the smallest tier's design.
            await using IBrowserContext context = await NewContextAsync(320, 640, signedIn: false);
            await PlaywrightTracing.RunTracedAsync(context, nameof(Every_page_reflows_without_scrolling_sideways), async () =>
            {
                IPage page = await NewPageAsync(context);
                foreach ((string label, string url) in Anonymous)
                {
                    await page.GotoAsync(url);
                    await AxeAssertions.AssertNoSidewaysScrollAsync(page, label);
                }
            });
        }

        [Fact]
        public async Task Inline_row_actions_are_large_enough_to_hit()
        {
            // The shared session's user, given something to remove.
            await server.AddDeviceBoundPasskeyAsync(IdentityServerFixtureBase.SharedSessionEmail);

            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn: true);
            await PlaywrightTracing.RunTracedAsync(context, nameof(Inline_row_actions_are_large_enough_to_hit), async () =>
            {
                IPage page = await NewPageAsync(context);
                await page.GotoAsync("/Identity/Manage/Passkeys");

                // axe's target-size rule skips widgets sitting inline within a block of text,
                // which is exactly what these are — so the measurement has to be made here.
                ILocator remove = page.Locator(".tmi-link-danger").First;
                await remove.WaitForAsync();
                LocatorBoundingBoxResult box = (await remove.BoundingBoxAsync())!;
                Assert.True(box.Height >= 24, $"A row action is only {box.Height}px tall.");
            });
        }

        /// <summary>Loads a page in Arabic and asserts it mirrors and stays accessible.</summary>
        /// <param name="page">The page to drive.</param>
        /// <param name="url">The page's URL, without a culture.</param>
        /// <returns>A task that completes when the assertions pass.</returns>
        private static async Task AssertArabicAsync(IPage page, string url)
        {
            await page.GotoAsync(url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?")
                + "culture=ar&ui-culture=ar");

            // Nothing in the axe rule set checks direction, and it is the one attribute the whole
            // right-to-left rendering hangs on.
            string direction = await page.EvaluateAsync<string>(
                "() => document.documentElement.getAttribute('dir')");
            Assert.Equal("rtl", direction);

            await AxeAssertions.AssertNoViolationsAsync(page, url + " in Arabic");
        }

        /// <summary>Projects a page table into xUnit theory rows.</summary>
        /// <param name="pages">The label and URL of each page.</param>
        /// <returns>The rows.</returns>
        private static TheoryData<string, string> Rows((string Label, string Url)[] pages)
        {
            TheoryData<string, string> rows = [];
            foreach ((string label, string url) in pages)
            {
                rows.Add(label, url);
            }

            return rows;
        }

        /// <summary>Scans one page at every viewport tier.</summary>
        private async Task ScanAsync(string label, string url, bool signedIn)
        {
            await using IBrowserContext context = await NewContextAsync(1440, 900, signedIn);
            await PlaywrightTracing.RunTracedAsync(context, $"a11y-{label.Replace(' ', '-')}", async () =>
            {
                IPage page = await NewPageAsync(context);

                foreach ((string name, int width, int height) in Viewports)
                {
                    await page.SetViewportSizeAsync(width, height);
                    await page.GotoAsync(url);
                    await AxeAssertions.AssertNoViolationsAsync(page, $"{label} at {name}");
                }
            });
        }

        /// <summary>Creates a browser context at a given viewport, with the policy left in force.</summary>
        /// <param name="width">Viewport width.</param>
        /// <param name="height">Viewport height.</param>
        /// <param name="signedIn">Whether to start from the suite's shared session.</param>
        /// <returns>The context.</returns>
        private async Task<IBrowserContext> NewContextAsync(int width, int height, bool signedIn)
        {
            return await playwright.Browser.NewContextAsync(new BrowserNewContextOptions
            {
                BaseURL = server.BaseAddress,
                ViewportSize = new ViewportSize { Width = width, Height = height },

                // One sign-in for the whole suite. Codes are rate-limited per IP address and every
                // test here comes from the same one, so signing in per test would run the budget
                // out partway through and leave the rest waiting for mail that never arrives.
                StorageState = signedIn ? await server.SignedInStorageStateAsync(playwright.Browser) : null,
            });
        }

        /// <summary>Creates a page already armed to record policy refusals.</summary>
        private static async Task<IPage> NewPageAsync(IBrowserContext context)
        {
            IPage page = await context.NewPageAsync();
            await AxeAssertions.CollectPolicyViolationsAsync(page);
            return page;
        }
    }
}
