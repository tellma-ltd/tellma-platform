// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     Granting consent in a real browser. The server side of this is covered by the
    ///     integration suite, which cannot see the defect this guards: a browser applies the
    ///     content-security policy's <c>form-action</c> directive to every hop of the navigation a
    ///     form submission produces, so a policy that names only <c>'self'</c> lets the grant
    ///     succeed and then silently discards the redirect that carries the code back. The page
    ///     never moves, nothing is logged as an error, and "Allow" appears to do nothing.
    /// </summary>
    [Collection(E2ECollectionDefinition.Name)]
    [Trait("Category", "E2E")]
    public sealed class ConsentTests(PlaywrightFixture playwright, IdentityServerFixture server)
    {
        [Fact]
        public async Task Allowing_consent_carries_the_browser_to_the_client_callback()
        {
            const string email = "e2e-consent@example.com";
            string callbackUri = server.CrossOriginAddress + IdentityServerFixtureBase.CallbackPath;
            await server.CreateConsentClientAsync("e2e-consent", "E2E Consent App", callbackUri);
            await server.CreateActiveUserAsync(email);

            await using IBrowserContext context = await playwright.Browser.NewContextAsync(
                new BrowserNewContextOptions { BaseURL = server.BaseAddress });

            await PlaywrightTracing.RunTracedAsync(context, nameof(Allowing_consent_carries_the_browser_to_the_client_callback), async () =>
            {
                IPage page = await context.NewPageAsync();
                await PasskeyCeremonies.SignInWithEmailCodeAsync(server, page, string.Empty, email);

                // Registered only now, so it cannot perturb the sign-in this test does not test.
                // A refused form submission reports itself only here, so collect the violations
                // rather than inferring them from a navigation that did not happen.
                await page.AddInitScriptAsync(@"() => {
                    window.__cspViolations = [];
                    document.addEventListener('securitypolicyviolation',
                        e => window.__cspViolations.push(e.violatedDirective + ' :: ' + e.blockedURI));
                }");

                IResponse consentResponse = (await page.GotoAsync(AuthorizeUrl(callbackUri)))!;
                await page.GetByRole(AriaRole.Heading, new() { Name = "Authorize application" }).WaitForAsync();

                await page.GetByRole(AriaRole.Button, new() { Name = "Allow" }).ClickAsync();

                // The whole point: the browser must actually follow the grant's redirect. When it
                // does not, the interesting evidence is the policy the page was served with and
                // what the browser refused, so report both rather than a bare timeout.
                try
                {
                    await page.WaitForURLAsync(callbackUri + "*", new() { Timeout = 10000 });
                }
                catch (TimeoutException)
                {
                    consentResponse.Headers.TryGetValue("content-security-policy", out string? policy);
                    Assert.Fail(
                        "The browser did not follow the grant's redirect.\n"
                        + $"  still at: {page.Url}\n"
                        + $"  policy:   {policy ?? "(none)"}\n"
                        + $"  refused:  {string.Join(" | ", await ViolationsAsync(page))}");
                }

                Assert.Contains("code=", page.Url, StringComparison.Ordinal);
                Assert.Empty(await ViolationsAsync(page));
            });
        }

        /// <summary>Reads the policy violations the page collected.</summary>
        /// <param name="page">The page under test.</param>
        /// <returns>One entry per refused action, empty when the policy blocked nothing.</returns>
        private static async Task<string[]> ViolationsAsync(IPage page)
        {
            return await page.EvaluateAsync<string[]>("() => window.__cspViolations ?? []");
        }

        /// <summary>Builds an authorization request for the consent-requiring client.</summary>
        /// <param name="callbackUri">The client's registered callback.</param>
        /// <returns>The relative authorize URL, PKCE included (the server requires it).</returns>
        private static string AuthorizeUrl(string callbackUri)
        {
            string verifier = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
            string challenge = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            return "/connect/authorize?client_id=e2e-consent&response_type=code"
                + "&redirect_uri=" + Uri.EscapeDataString(callbackUri)
                + "&scope=" + Uri.EscapeDataString("openid profile")
                + "&code_challenge=" + challenge + "&code_challenge_method=S256";
        }
    }
}
