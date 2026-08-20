// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Playwright;

namespace Tellma.Identity.E2E.Infrastructure
{
    /// <summary>
    ///     Records a Playwright trace around a test body and keeps it only when the body throws,
    ///     so CI can upload a screenshots-and-DOM timeline to debug a flake. Every browser test
    ///     runs through this helper; a test outside it would leave CI's trace upload empty when it
    ///     is the one that fails.
    /// </summary>
    public static class PlaywrightTracing
    {
        /// <summary>Runs a test body with tracing, saving the trace only on failure.</summary>
        /// <param name="context">The browser context to trace.</param>
        /// <param name="testName">The trace file name (without extension).</param>
        /// <param name="test">The test body.</param>
        /// <returns>A task that completes when the body and trace handling finish.</returns>
        public static async Task RunTracedAsync(IBrowserContext context, string testName, Func<Task> test)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentException.ThrowIfNullOrWhiteSpace(testName);
            ArgumentNullException.ThrowIfNull(test);

            await context.Tracing.StartAsync(new TracingStartOptions { Screenshots = true, Snapshots = true, Sources = true });
            bool failed = true;
            try
            {
                await test();
                failed = false;
            }
            finally
            {
                string traceDirectory = Path.Combine(AppContext.BaseDirectory, "playwright-traces");
                Directory.CreateDirectory(traceDirectory);
                await context.Tracing.StopAsync(new TracingStopOptions
                {
                    Path = failed ? Path.Combine(traceDirectory, testName + ".zip") : null,
                });
            }
        }
    }
}
