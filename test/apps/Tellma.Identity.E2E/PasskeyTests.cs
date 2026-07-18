// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     Register a passkey and sign in with it, using a CDP virtual authenticator. The dev
    ///     admin signs in first with an email code (read from the in-process sink), enrolls a
    ///     passkey in Account &amp; Security, signs out, then signs back in with the passkey.
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

            // Record a Playwright trace and keep it only when the test fails, so CI can upload a
            // screenshots-and-DOM timeline to debug a flake.
            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
            bool failed = true;
            try
            {
                IPage page = await context.NewPageAsync();
                await using VirtualAuthenticator _ = await VirtualAuthenticator.AttachAsync(context, page);

                // Sign in with an email code to reach Account & Security.
                await SignInWithEmailCodeAsync(page, "passkey-user@example.com");

                // Enroll a passkey.
                await page.GotoAsync("/Identity/Manage/Passkeys");
                await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
                await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
                await page.WaitForURLAsync("**/Identity/Manage/Passkeys");

                // The credential enrolled and appears in the list (the virtual authenticator
                // produces a device-bound, non-synced credential).
                Assert.DoesNotContain("You have no passkeys yet", await page.ContentAsync(), StringComparison.Ordinal);

                // Sign out.
                await page.GotoAsync("/Identity/Account/Logout");
                await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
                await page.WaitForURLAsync("**/Identity/Account/LoggedOut");

                // Sign back in with the passkey. On the login page the conditional-UI ceremony the
                // virtual authenticator satisfies automatically completes the sign-in; if it does
                // not fire, the explicit button drives the same ceremony.
                await page.GotoAsync("/Identity/Account/Login?returnUrl=%2FIdentity%2FManage%2FPasskeys");
                try
                {
                    await page.WaitForURLAsync("**/Identity/Manage/Passkeys", new() { Timeout = 5000 });
                }
                catch (TimeoutException)
                {
                    await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
                    await page.WaitForURLAsync("**/Identity/Manage/Passkeys", new() { Timeout = 15000 });
                }

                failed = false;
            }
            finally
            {
                string traceDirectory = Path.Combine(AppContext.BaseDirectory, "playwright-traces");
                Directory.CreateDirectory(traceDirectory);
                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = failed
                        ? Path.Combine(traceDirectory, "Register_a_passkey_then_sign_in_with_it.zip")
                        : null,
                });
            }
        }

        /// <summary>Signs the user in through the email-code flow.</summary>
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
