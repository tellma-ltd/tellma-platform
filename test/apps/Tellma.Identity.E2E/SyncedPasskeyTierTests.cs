// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

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
                    await page.Locator("li", new() { HasTextString = "Device-bound" }).WaitForAsync();
                    await page.Locator("li", new() { HasTextString = "Synced" }).WaitForAsync();

                    await page.GotoAsync("/Identity/Account/Logout");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
                    await page.WaitForURLAsync("**/Identity/Account/LoggedOut");

                    // Sign in against a request demanding aal3 — the tier the authorization
                    // endpoint puts on the login URL when it needs a device-bound credential.
                    await page.GotoAsync(
                        "/Identity/Account/Login?tier=" + Uri.EscapeDataString("urn:tellma:acr:aal3")
                        + "&returnUrl=" + Uri.EscapeDataString("/Identity/Manage/Passkeys"));

                    // The synced assertion must be refused *for being synced*. Asserting the
                    // specific message is what makes this test about the tier check: a generic
                    // ceremony failure renders into the same validation summary, so "an error
                    // appeared" would pass without the refusal ever running. The summary is the
                    // only place that string appears inside .tmi-error — the standing hint above
                    // the form is a .tmi-muted paragraph.
                    await DriveCeremonyUntilRefusedAsync(page);

                    // The user was not signed in, so the authorization endpoint is never handed a
                    // session that cannot reach the tier.
                    Assert.Contains("/Identity/Account/Login", page.Url, StringComparison.Ordinal);
                    Assert.DoesNotContain("/Identity/Manage/Passkeys", page.Url, StringComparison.Ordinal);
                });
        }

        /// <summary>
        ///     Drives the passkey ceremony on the login page until the device-bound refusal shows.
        ///     The conditional-UI ceremony may answer on load and submit by itself, so the page can
        ///     be mid-navigation at any moment: each step is therefore re-queried rather than
        ///     awaited once, and the explicit button is only used if the automatic path has not
        ///     already produced the refusal.
        /// </summary>
        private static async Task DriveCeremonyUntilRefusedAsync(IPage page)
        {
            ILocator error = page.Locator(".tmi-error li", new() { HasTextString = "device-bound passkey" });
            ILocator button = page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" });

            for (int attempt = 0; attempt < 120; attempt++)
            {
                try
                {
                    if (await error.CountAsync() > 0)
                    {
                        return;
                    }

                    if (await button.CountAsync() > 0)
                    {
                        await button.ClickAsync(new() { Timeout = 2000 });
                    }
                }
                catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
                {
                    // The page navigated under the query — the ceremony submitted itself — so the
                    // click or the count raced it. Re-query on the next pass.
                }

                await Task.Delay(250, TestContext.Current.CancellationToken);
            }

            Assert.Fail("The synced assertion was never refused with the device-bound requirement.");
        }

        /// <summary>Runs the enrollment ceremony from the passkey list and returns to it.</summary>
        private static async Task EnrollPasskeyAsync(IPage page)
        {
            await page.GotoAsync("/Identity/Manage/Passkeys");
            await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
            await page.WaitForURLAsync("**/Identity/Manage/Passkeys");
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
            await page.WaitForURLAsync("**/Identity/Manage/Passkeys");
        }

        /// <summary>Polls the captured email sink for the latest sign-in code.</summary>
        private async Task<string> WaitForCodeAsync(string email)
        {
            for (int attempt = 0; attempt < 50; attempt++)
            {
                string? code = server.Emails.LatestCodeFor(email);
                if (code is not null)
                {
                    return code;
                }

                await Task.Delay(100, TestContext.Current.CancellationToken);
            }

            throw new InvalidOperationException("No sign-in code was captured.");
        }
    }
}
