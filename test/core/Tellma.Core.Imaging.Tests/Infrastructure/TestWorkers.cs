// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>The PDF workers the suites run: the real one, and the stand-in that misbehaves on purpose.</summary>
    internal static class TestWorkers
    {
        /// <summary>The stand-in's assembly, beside the suite's.</summary>
        public static string StandInPath => Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.TestWorker.dll");

        /// <summary>The real PDF worker under a memory limit.</summary>
        /// <param name="memoryLimit">The limit; the default one when null.</param>
        /// <returns>The client.</returns>
        public static PdfWorkerClient Real(long? memoryLimit = null)
        {
            return new PdfWorkerClient(new PdfWorkerLauncher(), memoryLimit ?? SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);
        }

        /// <summary>The stand-in, misbehaving in one way, under a memory limit.</summary>
        /// <param name="mode">The misbehaviour or probe.</param>
        /// <param name="memoryLimit">The limit; the default one when null.</param>
        /// <returns>The client.</returns>
        public static PdfWorkerClient StandIn(string mode, long? memoryLimit = null)
        {
            return new PdfWorkerClient(new PdfWorkerLauncher(StandInPath, [mode]), memoryLimit ?? SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);
        }

        /// <summary>Polls until <paramref name="condition" /> holds, failing after <paramref name="patience" />.</summary>
        /// <param name="condition">The condition.</param>
        /// <param name="patience">How long to wait; 30 seconds when null.</param>
        /// <returns>A task that completes when it holds.</returns>
        public static async Task WaitUntilAsync(Func<bool> condition, TimeSpan? patience = null)
        {
            DateTime deadline = DateTime.UtcNow + (patience ?? TimeSpan.FromSeconds(30));
            while (!condition())
            {
                Assert.True(DateTime.UtcNow < deadline, "The condition did not hold in time.");
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }
        }
    }
}
