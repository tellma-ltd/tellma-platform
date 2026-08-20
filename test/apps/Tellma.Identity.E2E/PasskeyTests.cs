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

                await PasskeyCeremonies.RegisterThenSignInAsync(server, page, prefix: string.Empty, "passkey-user@example.com");
            });
        }
    }
}
