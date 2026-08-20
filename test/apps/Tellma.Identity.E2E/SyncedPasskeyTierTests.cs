// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     A synced (backup-eligible) passkey cannot satisfy the aal3 tier for a user who owns a
    ///     device-bound one. This is the half the inventory check at the authorization endpoint
    ///     cannot cover: that check sees the hardware key and waves the user through, so only
    ///     refusing the credential actually presented stops the synced one standing in — and stops
    ///     the browser bouncing between authorize and the login page forever.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class SyncedPasskeyTierTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        [Fact]
        public async Task A_synced_passkey_is_refused_when_the_user_owns_a_device_bound_one()
        {
            await server.CreateActiveUserAsync("mixed-keys@example.com");

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(
                context,
                nameof(A_synced_passkey_is_refused_when_the_user_owns_a_device_bound_one),
                async () =>
                {
                    IPage page = await context.NewPageAsync();

                    // The credential the user has to hand is synced, and it stays available for
                    // the whole test — removing its authenticator would destroy it, leaving the
                    // ceremony with nothing to present and the refusal untested.
                    await using VirtualAuthenticator _ =
                        await VirtualAuthenticator.AttachAsync(context, page, backupEligible: true);

                    await SignInWithEmailCodeAsync(page, "mixed-keys@example.com");
                    await EnrollPasskeyAsync(page);

                    // The list classifying it "Synced" is what proves the CDP backup-eligibility
                    // option took effect — without it every virtual credential would be
                    // device-bound and this test could not fail.
                    string afterSynced = await page.ContentAsync();
                    Assert.Contains("Synced", afterSynced, StringComparison.Ordinal);
                    Assert.DoesNotContain("Device-bound", afterSynced, StringComparison.Ordinal);

                    // The hardware key the user owns but does not have with them. Recorded in the
                    // store rather than enrolled through a second authenticator, because the two
                    // properties this case needs — owned, yet unavailable — cannot both be
                    // arranged through CDP: removing an authenticator takes its credentials with it.
                    await server.AddDeviceBoundPasskeyAsync("mixed-keys@example.com");
                    await page.GotoAsync("/Identity/Manage/Passkeys");
                    await page.Locator(".tmi-list li", new() { HasTextString = "Device-bound" }).WaitForAsync();
                    await page.Locator(".tmi-list li", new() { HasTextString = "Synced" }).WaitForAsync();

                    await page.GotoAsync("/Identity/Account/Logout");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
                    await page.WaitForPathAsync("/Identity/Account/LoggedOut");

                    await AllowOneCeremonyAsync(page);

                    // Sign in against a request demanding aal3 — the tier the authorization
                    // endpoint puts on the login URL when it needs a device-bound credential.
                    await page.GotoAsync(
                        "/Identity/Account/Login?tier=" + Uri.EscapeDataString("urn:tellma:acr:aal3")
                        + "&returnUrl=" + Uri.EscapeDataString("/Identity/Manage/Passkeys"));

                    // The synced assertion must be refused *for being synced*. Asserting the
                    // specific message is what makes this test about the tier check: a generic
                    // ceremony failure renders into the same error summary, so "an error
                    // appeared" would pass without the refusal ever running. The summary is the
                    // only place that string appears inside an error notice — the standing hint
                    // above the form is a warning notice with no list in it.
                    await DriveCeremonyUntilRefusedAsync(page);

                    // The user was not signed in, so the authorization endpoint is never handed a
                    // session that cannot reach the tier.
                    Assert.Contains("/Identity/Account/Login", page.Url, StringComparison.Ordinal);
                    Assert.DoesNotContain("/Identity/Manage/Passkeys", page.Url, StringComparison.Ordinal);
                });
        }

        /// <summary>
        ///     Lets exactly one passkey assertion be attempted from here on, by refusing every
        ///     request for assertion options after the first.
        ///     <para>
        ///         The login page offers the conditional ceremony afresh on every load, and this
        ///         authenticator is virtual, so it answers with no prompt a human would have to
        ///         satisfy. A refused attempt therefore renders the error and immediately becomes
        ///         the next attempt: the page never settles, and the refusal exists only in the
        ///         gaps between navigations. Capping the ceremonies leaves the first refusal on
        ///         screen to be read. It costs the test nothing — one synced assertion presented
        ///         once is exactly the case under test — and a real browser does not loop, because
        ///         conditional mediation there waits for the user to pick a credential.
        ///     </para>
        /// </summary>
        /// <param name="page">The page to constrain.</param>
        /// <returns>A task that completes when the route is in place.</returns>
        private static async Task AllowOneCeremonyAsync(IPage page)
        {
            int attempts = 0;
            await page.RouteAsync("**/Identity/api/passkey/assertion-options", async route =>
            {
                if (Interlocked.Increment(ref attempts) > 1)
                {
                    await route.AbortAsync();
                }
                else
                {
                    await route.ContinueAsync();
                }
            });
        }

        /// <summary>
        ///     Waits for the device-bound refusal on the login page. Whichever ceremony ran — the
        ///     conditional one the page starts on load, or the explicit button where conditional
        ///     mediation is unavailable — the single attempt allowed produces the message, and the
        ///     page then stays put.
        /// </summary>
        private static async Task DriveCeremonyUntilRefusedAsync(IPage page)
        {
            ILocator error = page
                .Locator(".tmi-notice-error li", new() { HasTextString = "device-bound passkey" })
                .First;

            try
            {
                await error.WaitForAsync(new() { Timeout = 15000 });
                return;
            }
            catch (TimeoutException)
            {
                // Conditional mediation was never offered, so the allowance is still unspent and
                // the explicit button gets it.
            }

            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
            await error.WaitForAsync(new() { Timeout = 15000 });
        }

        /// <summary>Runs the enrollment ceremony from the passkey list and returns to it.</summary>
        private static async Task EnrollPasskeyAsync(IPage page)
        {
            await page.GotoAsync("/Identity/Manage/Passkeys");
            await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
            await page.WaitForPathAsync("/Identity/Manage/Passkeys");
        }

        /// <summary>Signs the user in through the email-code flow, landing on the passkey list.</summary>
        private async Task SignInWithEmailCodeAsync(IPage page, string email)
        {
            await page.GotoAsync("/Identity/Account/Login?returnUrl=%2FIdentity%2FManage%2FPasskeys");
            await page.GetByLabel("Email").FillAsync(email);
            await page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in code" }).ClickAsync();

            string code = await WaitForCodeAsync(email);
            await page.GetByLabel("Code").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();
            await page.WaitForPathAsync("/Identity/Manage/Passkeys");
        }

        /// <summary>Waits for the sign-in code the background worker delivers.</summary>
        private Task<string> WaitForCodeAsync(string email)
        {
            return server.Emails.WaitForCodeAsync(email);
        }
    }
}
