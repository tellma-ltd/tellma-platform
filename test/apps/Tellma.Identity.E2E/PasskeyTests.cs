// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     Register a passkey and sign in with it on the standalone host, using a CDP virtual
    ///     authenticator: email-code sign-in (code read from the in-process sink), enrollment in
    ///     Account &amp; Security — where the credential must be classified device-bound — sign
    ///     out, then sign back in with the passkey.
    ///     <para>
    ///         Every page here arrives with its scripts late, as on a loaded runner, so each press
    ///         of a passkey button lands before the button is wired unless the flow waits for the
    ///         page's scripts. The in-proc run of the same ceremony stays at full speed, so the
    ///         pair covers both paces.
    ///     </para>
    ///     <para>
    ///         This browser is also told it offers no conditional mediation, so the sign-in runs
    ///         the explicit "Sign in with a passkey" press rather than the ceremony the login page
    ///         starts on its own. The suite's Chromium offers conditional mediation, so without
    ///         this the press would run nowhere; every other passkey sign-in keeps the
    ///         conditional ceremony.
    ///     </para>
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class PasskeyTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        [Fact]
        public async Task Register_a_passkey_then_sign_in_with_it()
        {
            await server.CreateActiveUserAsync("passkey-user@example.com");

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(context, nameof(Register_a_passkey_then_sign_in_with_it), async () =>
            {
                IPage page = await context.NewPageAsync();
                await using VirtualAuthenticator _ = await VirtualAuthenticator.AttachAsync(context, page);
                await page.HoldBackScriptsAsync();

                // Installed before the first navigation, so every document, from the login page's
                // own script to the helper's probe, gets the same "unavailable" answer.
                await page.AddInitScriptAsync(
                    "if (window.PublicKeyCredential) { PublicKeyCredential.isConditionalMediationAvailable = async () => false; }");

                await PasskeyCeremonies.RegisterThenSignInAsync(server, page, prefix: string.Empty, "passkey-user@example.com");
            });
        }
    }
}
