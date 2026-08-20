// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;
using System.Text.RegularExpressions;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Waits for the browser to arrive somewhere.
    ///     <para>
    ///         Every wait here asserts one thing — the address bar — and deliberately says nothing
    ///         about the document's load state. <c>IPage.WaitForURLAsync</c> conflates the
    ///         two, and which of its two behaviours you get is a race: if the URL does not match
    ///         yet it waits for a matching navigation, but if it already matches it degenerates
    ///         into waiting for the <c>load</c> lifecycle event of the document that is already
    ///         there. That second wait can never complete, because a lifecycle event is reported
    ///         once per document and a click that returned after its navigation committed has
    ///         already passed the moment it would have been observed. Since whether a click returns
    ///         before or after its navigation commits is a matter of milliseconds, the same call
    ///         passes for months and then hangs for a full timeout on an unrelated change of pace.
    ///     </para>
    ///     <para>
    ///         Nothing in this suite needs <c>load</c> anyway: what follows a wait is a locator,
    ///         and locators wait for their own element on their own.
    ///     </para>
    /// </summary>
    internal static class PageNavigation
    {
        /// <summary>
        ///     The budget a wait gets when the caller names none. This matches the Playwright
        ///     navigation default rather than the much shorter assertion default, so moving a call
        ///     site onto these helpers cannot silently shrink the time it used to allow.
        /// </summary>
        private const float DefaultTimeout = 30_000;

        /// <summary>Waits until the browser's address ends with a path, and fails if it does not.</summary>
        /// <param name="page">The browser page.</param>
        /// <param name="path">The path, including the engine's route prefix.</param>
        /// <param name="timeout">How long to allow, in milliseconds.</param>
        /// <returns>A task that completes once the browser is there.</returns>
        public static Task WaitForPathAsync(this IPage page, string path, float timeout = DefaultTimeout)
        {
            return page.WaitForUrlAsync(PathPattern(path), timeout);
        }

        /// <summary>
        ///     Whether the browser reached a path within the budget. For a step that may or may not
        ///     happen on its own, where not arriving is a branch to take rather than a failure.
        /// </summary>
        /// <param name="page">The browser page.</param>
        /// <param name="path">The path, including the engine's route prefix.</param>
        /// <param name="timeout">How long to allow, in milliseconds.</param>
        /// <returns>True when the browser got there in time.</returns>
        public static Task<bool> ReachedPathAsync(this IPage page, string path, float timeout)
        {
            return page.ReachedUrlAsync(PathPattern(path), timeout);
        }

        /// <summary>Waits until the browser's address matches a pattern, and fails if it does not.</summary>
        /// <param name="page">The browser page.</param>
        /// <param name="pattern">The pattern the whole address is searched for.</param>
        /// <param name="timeout">How long to allow, in milliseconds.</param>
        /// <returns>A task that completes once the browser is there.</returns>
        public static Task WaitForUrlAsync(this IPage page, Regex pattern, float timeout = DefaultTimeout)
        {
            ArgumentNullException.ThrowIfNull(page);

            return Assertions.Expect(page).ToHaveURLAsync(pattern, new() { Timeout = timeout });
        }

        /// <summary>Whether the browser's address matched a pattern within the budget.</summary>
        /// <param name="page">The browser page.</param>
        /// <param name="pattern">The pattern the whole address is searched for.</param>
        /// <param name="timeout">How long to allow, in milliseconds.</param>
        /// <returns>True when the browser got there in time.</returns>
        public static async Task<bool> ReachedUrlAsync(this IPage page, Regex pattern, float timeout)
        {
            try
            {
                await page.WaitForUrlAsync(pattern, timeout);
                return true;
            }
            catch (PlaywrightException)
            {
                // A failed web-first assertion, which is this helper's "no" rather than an error.
                // It is not a TimeoutException: that is what the navigation waits throw, and these
                // are assertions.
                return false;
            }
        }

        /// <summary>
        ///     Matches an address that ends with the given path. Anchored at the end so a prefix of
        ///     a longer path cannot satisfy it, and escaped because a path is a literal.
        /// </summary>
        private static Regex PathPattern(string path)
        {
            return new Regex(Regex.Escape(path) + "$");
        }
    }
}
