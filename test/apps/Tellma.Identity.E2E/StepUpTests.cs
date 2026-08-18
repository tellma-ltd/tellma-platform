// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Tellma.Identity.E2E.Infrastructure;
using Tellma.Identity.TestSupport;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     Signing in — and stepping an existing session up — for a client that needs no consent
    ///     screen, which is the shape almost every real client has. What makes it worth a browser
    ///     is that the sign-in form's own submission is then the navigation that has to carry the
    ///     browser all the way to the client's callback: the page posts, the authorization endpoint
    ///     answers with a redirect off this origin, and a content-security policy naming only
    ///     <c>'self'</c> refuses that hop. The grant succeeds, the code is issued, nothing is logged
    ///     as an error, and the user sits on the sign-in page watching it do nothing. Only a browser
    ///     enforces that directive, so only a browser can see it.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class StepUpTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        /// <summary>The tier only a passkey reaches, so reaching it always costs an interaction.</summary>
        private const string Aal2 = "urn:tellma:acr:aal2";

        [Fact]
        public async Task Stepping_up_with_a_passkey_carries_the_browser_to_the_client_callback()
        {
            const string email = "e2e-stepup@example.com";
            string callbackUri = server.CrossOriginAddress + IdentityServerFixtureBase.CallbackPath;
            await server.CreateFirstPartyClientAsync("e2e-stepup", callbackUri);
            await server.CreateActiveUserAsync(email);

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(
                context,
                nameof(Stepping_up_with_a_passkey_carries_the_browser_to_the_client_callback),
                async () =>
                {
                    IPage page = await context.NewPageAsync();
                    await using VirtualAuthenticator _ = await VirtualAuthenticator.AttachAsync(context, page);

                    // An email code establishes the session, then a passkey is enrolled on it. That
                    // ordering is the point: the session reaches the base tier only, so the request
                    // below genuinely has to step it up rather than being satisfied on arrival.
                    await PasskeyCeremonies.SignInWithEmailCodeAsync(server, page, string.Empty, email);
                    await page.GetByRole(AriaRole.Link, new() { Name = "Add a passkey" }).ClickAsync();
                    await page.GetByRole(AriaRole.Button, new() { Name = "Create a passkey" }).ClickAsync();
                    await page.WaitForPathAsync("/Identity/Manage/Passkeys");

                    // Enrolling signs the passkey into the session, which would satisfy the tier
                    // without any step-up at all. Signing out and back in with the email code alone
                    // puts the session back where a user who enrolled days ago would be.
                    await page.GotoAsync("/Identity/Account/Logout");
                    await page.GetByRole(AriaRole.Button, new() { Name = "Sign out" }).ClickAsync();
                    await page.WaitForPathAsync("/Identity/Account/LoggedOut");
                    await SignInWithEmailCodeOnlyAsync(page, email);

                    // A refused form submission announces itself only in the page, so collect the
                    // violations rather than inferring them from a navigation that never happened.
                    await page.AddInitScriptAsync(@"() => {
                        window.__cspViolations = [];
                        document.addEventListener('securitypolicyviolation',
                            e => window.__cspViolations.push(e.violatedDirective + ' :: ' + e.blockedURI));
                    }");

                    // The response the assertion below is served from is the document carrying the
                    // form, and a browser checks form-action against that document's policy — so
                    // this is the policy that decides whether the sign-in can complete.
                    IResponse stepUpResponse = (await page.GotoAsync(AuthorizeUrl(callbackUri)))!;

                    // Read off the response, not the live address. The conditional ceremony starts
                    // on load and a virtual authenticator answers it instantly, so once the policy
                    // permits the submission the browser can be at the callback before the next
                    // line runs — and the claim here is about where the authorization endpoint sent
                    // the browser, which the response records for good.
                    Assert.Contains("/Identity/Account/Login", stepUpResponse.Url, StringComparison.Ordinal);
                    Assert.Contains("stepUp=true", stepUpResponse.Url, StringComparison.Ordinal);

                    await DriveCeremonyAsync(page);

                    if (!await page.ReachedUrlAsync(new Regex("^" + Regex.Escape(callbackUri)), 10000))
                    {
                        stepUpResponse.Headers.TryGetValue("content-security-policy", out string? policy);
                        Assert.Fail(
                            "The browser did not follow the step-up's redirect to the client.\n"
                            + $"  still at: {page.Url}\n"
                            + $"  policy:   {policy ?? "(none)"}\n"
                            + $"  refused:  {string.Join(" | ", await ViolationsAsync(page))}");
                    }

                    Assert.Contains("code=", page.Url, StringComparison.Ordinal);
                    Assert.Empty(await ViolationsAsync(page));
                });
        }

        /// <summary>
        ///     Runs whichever passkey ceremony the page offers. The conditional one starts on load
        ///     and a virtual authenticator answers it with no prompt, so it usually wins; where
        ///     conditional mediation is unavailable the explicit button drives the same ceremony.
        /// </summary>
        /// <param name="page">The sign-in page.</param>
        /// <returns>A task that completes once an assertion has been submitted.</returns>
        private static async Task DriveCeremonyAsync(IPage page)
        {
            // The submission navigates away, so leaving the page is itself the signal that the
            // conditional ceremony ran. Staying put means it never fired — or that the policy
            // refused the navigation, which the caller reports either way.
            if (await page.ReachedUrlAsync(new Regex("/connect/authorize|/e2e/callback"), 5000))
            {
                return;
            }

            await page.GetByRole(AriaRole.Button, new() { Name = "Sign in with a passkey" }).ClickAsync();
        }

        /// <summary>Signs in with an email code and nothing else, leaving the session at the base tier.</summary>
        private async Task SignInWithEmailCodeOnlyAsync(IPage page, string email)
        {
            // Installed before the navigation, not after: the page offers the passkey ceremony on
            // load and a virtual authenticator answers it with no prompt, so by the time a route
            // added afterwards took effect the sign-in would already have happened — with the
            // passkey, leaving nothing to step up. Refusing the options request confines this
            // sign-in to the email code.
            await page.RouteAsync("**/Identity/api/passkey/assertion-options", static route => route.AbortAsync());
            await page.GotoAsync("/Identity/Account/Login?returnUrl=%2FIdentity%2FManage%2FPasskeys");

            await page.GetByLabel("Email").FillAsync(email);
            await page.GetByRole(AriaRole.Button, new() { Name = "Email me a sign-in code" }).ClickAsync();

            string code = await server.Emails.WaitForCodeAsync(email);
            await page.GetByLabel("Code").FillAsync(code);
            await page.GetByRole(AriaRole.Button, new() { Name = "Verify" }).ClickAsync();
            await page.WaitForPathAsync("/Identity/Manage/Passkeys");

            await page.UnrouteAsync("**/Identity/api/passkey/assertion-options");
        }

        /// <summary>Reads the policy violations the page collected.</summary>
        /// <param name="page">The page under test.</param>
        /// <returns>One entry per refused action, empty when the policy blocked nothing.</returns>
        private static async Task<string[]> ViolationsAsync(IPage page)
        {
            return await page.EvaluateAsync<string[]>("() => window.__cspViolations ?? []");
        }

        /// <summary>Builds an authorization request demanding the tier only a passkey reaches.</summary>
        /// <param name="callbackUri">The client's registered callback.</param>
        /// <returns>The relative authorize URL, PKCE included (the server requires it).</returns>
        private static string AuthorizeUrl(string callbackUri)
        {
            string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            return "/connect/authorize?client_id=e2e-stepup&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(callbackUri)
                + "&scope=" + Uri.EscapeDataString("openid profile")
                + "&acr_values=" + Uri.EscapeDataString(Aal2)
                + "&code_challenge=" + challenge + "&code_challenge_method=S256";
        }
    }
}
