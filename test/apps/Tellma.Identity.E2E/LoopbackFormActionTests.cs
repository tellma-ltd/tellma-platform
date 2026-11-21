// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using Tellma.Identity.E2E.Infrastructure;

namespace Tellma.Identity.E2E
{
    /// <summary>
    ///     What a browser does with the <c>form-action</c> sources the consent page names for a
    ///     native client's loopback callback. The page and the grant are answered by request
    ///     routing inside the browser, so no identity host is involved: the page posts a form to
    ///     its own origin, is answered with a redirect to the callback on an ephemeral loopback
    ///     port, and the test records whether the browser issued the callback request (nothing
    ///     listens there, so an issued request is refused by the loopback interface and never
    ///     leaves the machine) or blocked the redirect under the policy.
    /// </summary>
    [Trait("Category", "E2E")]
    public sealed class LoopbackFormActionTests(PlaywrightFixture playwright) : IClassFixture<PlaywrightFixture>
    {
        /// <summary>The origin the routed consent page is served from.</summary>
        private const string PageOrigin = "http://identity.test";

        /// <summary>How long the browser has to either issue the callback request or block it.</summary>
        private static readonly TimeSpan Budget = TimeSpan.FromSeconds(10);

        [Theory]
        [InlineData("http://127.0.0.1:*/callback", "http://127.0.0.1:50123/callback", true)]
        [InlineData("http://localhost:*/callback", "http://localhost:50123/callback", true)]
        [InlineData("http://[::1]:*/callback", "http://[::1]:50123/callback", false)]
        public async Task A_loopback_source_with_any_port_lets_the_redirect_through_only_when_the_browser_parses_it(
            string source, string callback, bool followed)
        {
            await using IBrowserContext context = await playwright.Browser.NewContextAsync();
            IPage page = await context.NewPageAsync();

            // The outcome: the first of the callback request being issued (true) or the policy
            // blocking the redirect (false), which the browser reports on the console. The warning
            // about a source the browser cannot parse names the directive too, but arrives when
            // the page loads, before any form is sent, so it is not a block.
            TaskCompletionSource<bool> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
            List<string> console = [];
            page.Console += (_, message) =>
            {
                console.Add(message.Text);
                if (message.Text.Contains("form-action", StringComparison.Ordinal)
                    && !message.Text.Contains("invalid source", StringComparison.Ordinal))
                {
                    _ = outcome.TrySetResult(false);
                }
            };
            List<string> requests = [];
            page.Request += (_, request) =>
            {
                requests.Add(request.Method + " " + request.Url);
                if (request.Url.StartsWith(callback, StringComparison.Ordinal))
                {
                    _ = outcome.TrySetResult(true);
                }
            };

            // The consent page: its policy names the source, and its form posts back to itself.
            await page.RouteAsync(PageOrigin + "/consent", route => route.FulfillAsync(new RouteFulfillOptions
            {
                ContentType = "text/html",
                Headers = new Dictionary<string, string> { ["Content-Security-Policy"] = "default-src 'self'; form-action 'self' " + source },
                Body = "<form method=\"post\" action=\"/grant\"><button>Allow</button></form>",
            }));

            // The grant: a redirect to the client's callback, as the authorization endpoint answers.
            await page.RouteAsync(PageOrigin + "/grant", route => route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 302,
                Headers = new Dictionary<string, string> { ["Location"] = callback + "?code=e2e" },
            }));

            await page.GotoAsync(PageOrigin + "/consent");
            await page.GetByRole(AriaRole.Button, new() { Name = "Allow" }).ClickAsync();

            // Neither signal within the budget is a broken harness, not an answer.
            bool issued = await outcome.Task.WaitAsync(Budget, TestContext.Current.CancellationToken);

            Assert.True(
                followed == issued,
                $"Source '{source}': expected the redirect to be {(followed ? "followed" : "blocked")}, but it was {(issued ? "followed" : "blocked")}.\n"
                + $"  requests: {string.Join(" | ", requests)}\n"
                + $"  console: {string.Join(" | ", console)}");
        }
    }
}
