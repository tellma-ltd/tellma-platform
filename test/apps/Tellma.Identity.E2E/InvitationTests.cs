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
                Assert.Contains(email, content, StringComparison.Ordinal);
            });
        }

        [Fact]
        public async Task An_invitation_opened_in_another_users_session_enrolls_for_the_invited_user()
        {
            const string invited = "e2e-invited-elsewhere@example.com";
            await server.CreateActiveUserAsync(invited);
            string token = await server.IssueInvitationTokenAsync(invited);

            // A browser that is already somebody else — a shared machine, or an administrator
            // opening a link to see what it looks like.
            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions
                {
                    BaseURL = server.BaseAddress,
                    StorageState = await server.SignedInStorageStateAsync(playwright.Browser),
                });

            await PlaywrightTracing.RunTracedAsync(context, nameof(An_invitation_opened_in_another_users_session_enrolls_for_the_invited_user), async () =>
            {
                IPage page = await context.NewPageAsync();
                await using VirtualAuthenticator authenticator = await VirtualAuthenticator.AttachAsync(context, page);

                await page.GotoAsync("/Identity/Manage/Index");
                Assert.Contains(
                    IdentityServerFixtureBase.SharedSessionEmail,
                    await page.ContentAsync(),
                    StringComparison.Ordinal);

                // Give the ambient account a passkey on this authenticator first. Without one the
                // scenario proves less than it looks: the enrollment's exclude list is built from
                // whichever user the options endpoint picked, and an empty list matches nothing,
                // so options built for the wrong user still complete. With one, resolving the
                // wrong user makes the authenticator refuse the ceremony outright — which is the
                // shape the failure actually takes in a browser.
                await page.GotoAsync("/Identity/Manage/Passkeys");
                await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
                await page.WaitForURLAsync("**/Identity/Manage/Passkeys");

                await page.GotoAsync("/Identity/Account/Invitation?code=" + Uri.EscapeDataString(token));
                await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
                await page.WaitForURLAsync("**/Identity/Manage/Passkeys", new() { Timeout = 15000 });

                // The link named one account and the session named another. The credential — and
                // the session it leaves behind — must belong to the account the link named, or an
                // invitation opened on the wrong machine hands the invited user's enrolment to
                // whoever happened to be signed in, having burned their one-time link to do it.
                string content = await page.ContentAsync();
                Assert.Contains(invited, content, StringComparison.Ordinal);
                Assert.DoesNotContain(IdentityServerFixtureBase.SharedSessionEmail, content, StringComparison.Ordinal);
            });
        }
    }
}
