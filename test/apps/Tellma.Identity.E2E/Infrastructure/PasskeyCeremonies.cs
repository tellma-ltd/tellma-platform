// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.TestSupport;

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
            await page.WaitForPathAsync(passkeysPath);

            // The credential enrolled and appears in the list — and the virtual authenticator's
            // credential is not backup-eligible, so it must be classified "Device-bound": this
            // label is the observable end of the device-bound signal the aal3 tier hangs on.
            string content = await page.ContentAsync();
            Assert.DoesNotContain("You have no passkeys yet", content, StringComparison.Ordinal);
            Assert.Contains("Device-bound", content, StringComparison.Ordinal);

            // Sign out.
            await page.GotoAsync(prefix + "/Identity/Account/Logout");
            await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
            await page.WaitForPathAsync(prefix + "/Identity/Account/LoggedOut");

            // Sign back in with the passkey, through whichever ceremony the login page runs. The
            // browser is asked which one that will be while it rests here, before the login page
            // loads: there the conditional ceremony may finish and navigate away at any moment,
            // taking with it the document a question would be asked in. For the same reason the
            // navigation waits only until it commits: the login document's load may never be
            // observed once the ceremony has moved the browser on, and nothing below needs it.
            bool conditional = await OffersConditionalMediationAsync(page);
            await page.GotoAsync(
                prefix + "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(passkeysPath),
                new() { WaitUntil = WaitUntilState.Commit });
            if (!conditional)
            {
                // No ceremony runs on its own, so the button is the only way in. The locator
                // waits for the button to be actionable before pressing it.
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
            }

            await page.WaitForPathAsync(passkeysPath);
        }

        /// <summary>
        ///     Whether the browser offers conditional mediation, which decides the passkey ceremony
        ///     the login page runs: the conditional one the page starts on load when it does, which a
        ///     virtual authenticator answers with no prompt, and otherwise none until the explicit
        ///     button is pressed.
        ///     <para>
        ///         The answer belongs to the browser, not to a document, so it is asked on a page
        ///         that stays put. Deciding from the browser's answer, rather than from how long the
        ///         sign-in has taken, is what keeps the two ceremonies from overlapping: a slow
        ///         conditional ceremony still in flight when the button is pressed is aborted by the
        ///         press, or submits while the explicit one starts, and the sign-in fails either way.
        ///     </para>
        /// </summary>
        /// <param name="page">A settled page on the engine's origin.</param>
        /// <returns>True when the login page will run the conditional ceremony on its own.</returns>
        internal static Task<bool> OffersConditionalMediationAsync(IPage page)
        {
            // The same question, asked the same way, as the login page's script asks it on load.
            return page.EvaluateAsync<bool>(
                """
                async () => !!(window.PublicKeyCredential
                    && PublicKeyCredential.isConditionalMediationAvailable
                    && await PublicKeyCredential.isConditionalMediationAvailable())
                """);
        }

        /// <summary>Signs the user in through the email-code flow, landing on the passkey list.</summary>
        /// <param name="server">The running host (email sink).</param>
        /// <param name="page">The browser page.</param>
        /// <param name="prefix">The engine route prefix ("" or "/id").</param>
        /// <param name="email">The signing-in user.</param>
        /// <returns>A task that completes once the session is established.</returns>
        internal static async Task SignInWithEmailCodeAsync(
            IdentityServerFixtureBase server, IPage page, string prefix, string email)
        {
            string passkeysPath = prefix + "/Identity/Manage/Passkeys";
            await page.GotoAsync(prefix + "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(passkeysPath));
            await page.GetByLabel("Email").FillAsync(email);

            // The baseline is taken before the code is requested, so a second sign-in of the same
            // address waits for its own code rather than reading the first, already redeemed one.
            int delivered = server.Emails.CountFor(email);
            await page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in code" }).ClickAsync();

            string code = await server.Emails.WaitForCodeAfterAsync(email, delivered);
            await page.GetByLabel("Code").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();
            await page.WaitForPathAsync(passkeysPath);
        }
    }
}
