// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     The same passkey register-then-sign-in ceremony on the in-proc composition, where every
    ///     engine route (pages, ceremony endpoints, protocol endpoints) lives under the reserved
    ///     <c>/id</c> path base — proving no browser step depends on the engine owning the origin
    ///     root.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class InProcPasskeyTests(PlaywrightFixture playwright, InProcIdentityServerFixture server)
    {
        [Fact]
        public async Task Register_a_passkey_then_sign_in_with_it_under_the_path_base()
        {
            await server.CreateActiveUserAsync("inproc-passkey-user@example.com");

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(
                context,
                nameof(Register_a_passkey_then_sign_in_with_it_under_the_path_base),
                async () =>
                {
                    IPage page = await context.NewPageAsync();
                    await using VirtualAuthenticator _ = await VirtualAuthenticator.AttachAsync(context, page);

                    await PasskeyCeremonies.RegisterThenSignInAsync(server, page, prefix: "/id", "inproc-passkey-user@example.com");
                });
        }
    }
}
