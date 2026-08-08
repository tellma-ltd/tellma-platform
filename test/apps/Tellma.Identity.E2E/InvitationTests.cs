// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     Accepting an invitation. Redeeming the link issues the credential-flow cookie the
    ///     enrollment ceremony runs against, so the landing page can run that ceremony itself —
    ///     it used to link on to a second page whose button carried the identical label, which
    ///     read as the first button having done nothing.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class InvitationTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        [Fact]
        public async Task Accepting_an_invitation_enrolls_a_passkey_without_a_second_page()
        {
            const string email = "e2e-invitation@example.com";
            await server.CreateActiveUserAsync(email);
            string token = await server.IssueInvitationTokenAsync(email);

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(context, nameof(Accepting_an_invitation_enrolls_a_passkey_without_a_second_page), async () =>
            {
                IPage page = await context.NewPageAsync();
                await using VirtualAuthenticator authenticator = await VirtualAuthenticator.AttachAsync(context, page);

                await page.GotoAsync("/Identity/Account/Invitation?code=" + Uri.EscapeDataString(token));
                await page.GetByRole(AriaRole.Heading, new() { Name = "Welcome" }).WaitForAsync();

                // One button, and pressing it runs the ceremony rather than navigating to another
                // page offering the same button again.
                await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();

                // Enrollment signs the user in, so it lands on the signed-in passkey list.
                await page.WaitForURLAsync("**/Identity/Manage/Passkeys", new() { Timeout = 15000 });
                string content = await page.ContentAsync();
                Assert.DoesNotContain("You have no passkeys yet", content, StringComparison.Ordinal);
            });
        }
    }
}
