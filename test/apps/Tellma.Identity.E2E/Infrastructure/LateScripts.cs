// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Makes a page's scripts arrive late, the way they do on a loaded runner, so a step that
    ///     presses a script-wired control before its script has run fails every time instead of
    ///     once in a while.
    ///     <para>
    ///         Each identity page that runs a passkey ceremony renders its button before the
    ///         scripts at the end of its body have attached the button's listener. On a fast
    ///         machine that window is a few milliseconds and a premature press is almost never
    ///         caught; holding every script back opens the window wide enough that the press lands
    ///         inside it whenever the test does not wait for the scripts.
    ///     </para>
    /// </summary>
    internal static class LateScripts
    {
        /// <summary>
        ///     How long each script response is held back. Far longer than a click takes to become
        ///     actionable on a painted button, and short enough that a test held back on every page
        ///     it visits stays within a few seconds of its plain run.
        /// </summary>
        private static readonly TimeSpan Hold = TimeSpan.FromSeconds(1);

        /// <summary>
        ///     Holds back every script response the page receives from here on. Install it before
        ///     the first navigation the test wants slowed.
        /// </summary>
        /// <param name="page">The browser page.</param>
        /// <returns>A task that completes when the route is in place.</returns>
        public static Task HoldBackScriptsAsync(this IPage page)
        {
            ArgumentNullException.ThrowIfNull(page);

            // Matched on the path, not a glob, so a versioned or query-carrying script address is
            // held back like any other.
            return page.RouteAsync(
                url => new Uri(url).AbsolutePath.EndsWith(".js", StringComparison.OrdinalIgnoreCase),
                async route =>
                {
                    await Task.Delay(Hold);
                    await route.ContinueAsync();
                });
        }
    }
}
