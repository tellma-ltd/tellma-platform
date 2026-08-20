// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>Owns the Playwright + Chromium lifecycle shared across the E2E assembly.</summary>
    public sealed class PlaywrightFixture : IAsyncLifetime
    {
        private IPlaywright? _playwright;

        /// <summary>The launched Chromium browser.</summary>
        public IBrowser Browser { get; private set; } = null!;

        /// <inheritdoc />
        public async ValueTask InitializeAsync()
        {
            _playwright = await Playwright.CreateAsync();
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await Browser.DisposeAsync();
            _playwright?.Dispose();
        }
    }
}
