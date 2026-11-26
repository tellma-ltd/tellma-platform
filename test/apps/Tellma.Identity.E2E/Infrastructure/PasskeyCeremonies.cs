// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using System.Text.RegularExpressions;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     The passkey browser flows every passkey test drives: enrolling from the passkey list,
    ///     pressing an enrollment button, and starting a passkey sign-in. Each is parameterized by
    ///     the engine's route prefix where it navigates, so the standalone (root) and in-proc
    ///     (<c>/id</c>) compositions run the identical ceremony.
    ///     <para>
    ///         Both passkey buttons, "Create a passkey" and "Sign in with a passkey", are
    ///         <c>type="button"</c> controls whose only behaviour is a listener the page's scripts
    ///         attach. The button is painted and actionable before those scripts have run, so a
    ///         press that does not first wait for them is a silent no-op that surfaces as a
    ///         navigation timeout. Every press of either button in this suite goes through this
    ///         class, which waits for the scripts first.
    ///     </para>
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

            // Sign in with an email code to reach Account & Security, then enroll a passkey.
            await SignInWithEmailCodeAsync(server, page, prefix, email);
            await EnrollPasskeyAsync(page, prefix);

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

            // Sign back in with the passkey, through whichever ceremony the login page runs.
            await StartPasskeySignInAsync(
                page, prefix + "/Identity/Account/Login?returnUrl=" + Uri.EscapeDataString(passkeysPath));
            await page.WaitForPathAsync(passkeysPath);
        }

        /// <summary>
        ///     Enrolls a passkey from the passkey list: follows "Add a passkey" to the enrollment
        ///     page, runs the ceremony there, and waits until the browser is back on the list.
        /// </summary>
        /// <param name="page">The browser page, showing the passkey list, with a virtual authenticator attached.</param>
        /// <param name="prefix">The engine route prefix ("" or "/id").</param>
        /// <returns>A task that completes once the browser is back on the passkey list.</returns>
        public static async Task EnrollPasskeyAsync(IPage page, string prefix)
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();

            // The enrollment page carries the return address in its query, so the match ends at
            // the path rather than at the end of the address. Arriving is what makes the script
            // wait below about this page: until then it would be answered by the list.
            await page.WaitForUrlAsync(
                new Regex(Regex.Escape(prefix + "/Identity/Account/RegisterPasskey") + @"(\?|$)"));
            await CreatePasskeyAsync(page);

            await page.WaitForPathAsync(prefix + "/Identity/Manage/Passkeys");
        }

        /// <summary>
        ///     Presses "Create a passkey" on the page the browser is showing, once the page's
        ///     scripts have attached the listener that runs the enrollment ceremony.
        /// </summary>
        /// <param name="page">The browser page, already at a page that offers enrollment.</param>
        /// <returns>A task that completes once the button has been pressed.</returns>
        public static async Task CreatePasskeyAsync(IPage page)
        {
            await page.WaitForScriptsAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
        }

        /// <summary>
        ///     Opens the login page, or an address that redirects to it, and starts the passkey
        ///     sign-in it offers: the conditional ceremony the page starts on load where the
        ///     browser offers conditional mediation, which a virtual authenticator answers with no
        ///     prompt, and otherwise the explicit button. Where the ceremony leads is the caller's
        ///     to wait for.
        /// </summary>
        /// <param name="page">
        ///     A settled page on the engine's origin. The browser is asked about conditional
        ///     mediation here, before the login page loads.
        /// </param>
        /// <param name="url">The address to open.</param>
        /// <returns>The response of the navigation's last hop, the login page's.</returns>
        public static async Task<IResponse?> StartPasskeySignInAsync(IPage page, string url)
        {
            // The browser is asked which ceremony the login page will run while it rests here,
            // before that page loads: there the conditional ceremony may finish and navigate away
            // at any moment, taking with it the document a question would be asked in. Deciding
            // from the answer, rather than from how long the sign-in has taken, is what keeps the
            // two ceremonies from overlapping: a slow conditional ceremony still in flight when
            // the button is pressed is aborted by the press, or submits while the explicit one
            // starts, and the sign-in fails either way.
            bool conditional = await OffersConditionalMediationAsync(page);

            // The navigation waits only until the login document commits: a conditional ceremony
            // may move the browser on before that document's events are observed, and nothing
            // here needs them. Arriving is also what makes the script wait below about the login
            // page rather than the one being left.
            IResponse? response = await page.GotoAsync(url, new() { WaitUntil = WaitUntilState.Commit });
            if (!conditional)
            {
                // No ceremony runs on its own, so the button is the only way in, and it does
                // nothing until the page's scripts have attached its listener. Nothing can move
                // the browser on meanwhile, so waiting for them is safe here, as it would not be
                // with a conditional ceremony in flight.
                await page.WaitForScriptsAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
            }

            return response;
        }

        /// <summary>
        ///     Whether the browser offers conditional mediation, which decides the passkey ceremony
        ///     the login page runs: the conditional one the page starts on load when it does, which a
        ///     virtual authenticator answers with no prompt, and otherwise none until the explicit
        ///     button is pressed. The answer belongs to the browser, not to a document, so it is
        ///     asked on a page that stays put.
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
