// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>The worker's entry point.</summary>
    internal static class Program
    {
        /// <summary>Renders one PDF's first page, as the image processor that started this process asks.</summary>
        /// <param name="args">The sandbox's arguments, from the host.</param>
        /// <returns>The exit code.</returns>
        private static int Main(string[] args)
        {
            return WorkerSession.Run(args, PdfiumRasterizer.Instance);
        }
    }
}
