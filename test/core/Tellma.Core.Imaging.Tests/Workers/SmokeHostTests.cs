// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     The processor in a host of its own, composed on the generic host as a distribution
    ///     composes it: its startup check passes, an honest PDF renders, a hostile one is stopped
    ///     with the host's memory unmoved, and PDFium is never loaded into the host.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class SmokeHostTests
    {
        [Fact]
        public async Task A_host_renders_pdf_thumbnails_without_ever_loading_pdfium()
        {
            using Process host = Process.Start(StartInfo())!;
            string output = await host.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await host.WaitForExitAsync(TestContext.Current.CancellationToken);

            TestContext.Current.TestOutputHelper?.WriteLine(output);
            var report = output.Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0 && line.Contains('=', StringComparison.Ordinal) && !line.StartsWith(' '))
                .Select(static line => line.Split('=', 2))
                .Where(static pair => pair[0].All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                .ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);

            Assert.Equal("passed", report["check.core.imaging"]);
            Assert.NotEqual("none", report["honest.bytes"]);
            Assert.Equal("none", report["hostile.thumbnail"]);
            Assert.Equal("no", report["host.pdfium-loaded"]);
            Assert.Equal("no", report["host.pdftoimage-loaded"]);
            Assert.Equal("held", report["result"]);
            Assert.Equal(0, host.ExitCode);
        }

        /// <summary>
        ///     Starts the suite's host-shaped asset as its own process, on this suite's runtime
        ///     configuration and dependency manifest, which list it and everything it needs.
        /// </summary>
        /// <param name="arguments">Its arguments.</param>
        /// <returns>The start information, with standard output redirected.</returns>
        internal static ProcessStartInfo StartInfo(params string[] arguments)
        {
            PdfWorkerFiles files = new PdfWorkerLauncher().Files;
            var start = new ProcessStartInfo(files.Muxer)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
            };

            foreach (string argument in (IEnumerable<string>)["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.SmokeHost.dll"), .. arguments])
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }
    }
}
