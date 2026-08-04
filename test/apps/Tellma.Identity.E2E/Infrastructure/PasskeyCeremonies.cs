// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     The passkey register-then-sign-in browser flow, parameterized by the engine's route
    ///     prefix so the standalone (root) and in-proc (<c>/id</c>) compositions run the identical
    ///     ceremony.
    /// </summary>
    internal static class PasskeyCeremonies
    {
        /// <summary>
        ///     Signs in with an email code, enrolls a passkey, signs out, and signs back in with
        ///     the passkey — asserting the credential appears and is classified device-bound.
        /// </summary>
        /// <param name="server">The running host (email sink).</param>
        /// <param name="page">The browser page (with a virtual authenticator attached).</param>
        /// <param name="prefix">The engine route prefix ("" or "/id").</param>
        /// <param name="email">The signing-in user.</param>
        /// <returns>A task that completes when the passkey sign-in lands back on the passkey list.</returns>
        public static async Task RegisterThenSignInAsync(
            IdentityServerFixtureBase server, IPage page, string prefix, string email)
        {
            string passkeysPath = prefix + "/Identity/Manage/Passkeys";

            // Sign in with an email code to reach Account & Security.
            await SignInWithEmailCodeAsync(server, page, prefix, email);

            // Enroll a passkey.
            await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
            await page.WaitForURLAsync("**" + passkeysPath);

            // The credential enrolled and appears in the list — and the virtual authenticator's
            // credential is not backup-eligible, so it must be classified "Device-bound": this
            // label is the observable end of the device-bound signal the aal3 tier hangs on.
            string content = await page.ContentAsync();
            Assert.DoesNotContain("You have no passkeys yet", content, StringComparison.Ordinal);
            Assert.Contains("Device-bound", content, StringComparison.Ordinal);

            // Sign out.
            await page.GotoAsync(prefix + "/Identity/Account/Logout");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
            await page.WaitForURLAsync("**" + prefix + "/Identity/Account/LoggedOut");

            // Sign back in with the passkey. On the login page the conditional-UI ceremony the
            // virtual authenticator satisfies automatically completes the sign-in; if it does
            // not fire, the explicit button drives the same ceremony.
            await page.GotoAsync(prefix + "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(passkeysPath));
            try
            {
                await page.WaitForURLAsync("**" + passkeysPath, new() { Timeout = 5000 });
            }
            catch (TimeoutException)
            {
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
                await page.WaitForURLAsync("**" + passkeysPath, new() { Timeout = 15000 });
            }
        }

        /// <summary>Signs the user in through the email-code flow, landing on the passkey list.</summary>
        private static async Task SignInWithEmailCodeAsync(
            IdentityServerFixtureBase server, IPage page, string prefix, string email)
        {
            string passkeysPath = prefix + "/Identity/Manage/Passkeys";
            await page.GotoAsync(prefix + "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(passkeysPath));
            await page.GetByLabel("Email").FillAsync(email);
            await page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in code" }).ClickAsync();

            string code = await WaitForCodeAsync(server, email);
            await page.GetByLabel("Code").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();
            await page.WaitForURLAsync("**" + passkeysPath);
        }

        /// <summary>Polls the captured email sink for the latest sign-in code.</summary>
        private static async Task<string> WaitForCodeAsync(IdentityServerFixtureBase server, string email)
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
