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
    ///     with the host's memory unmoved, and PDFium is never loaded into the host; and a Production
    ///     host renders PDF thumbnails on Linux only, saying so in its log elsewhere.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class SmokeHostTests
    {
        [Fact]
        public async Task A_host_renders_pdf_thumbnails_without_ever_loading_pdfium()
        {
            // A Development host, which renders PDF thumbnails on every operating system.
            (Dictionary<string, string> report, _, int exitCode) = await RunAsync(StartInfo());

            Assert.Equal("passed", report["check.core.imaging"]);
            Assert.Equal("Enabled", report["pdf.thumbnails"]);
            Assert.NotEqual("none", report["honest.bytes"]);
            Assert.Equal("none", report["hostile.thumbnail"]);
            Assert.Equal("no", report["host.pdfium-loaded"]);
            Assert.Equal("no", report["host.pdftoimage-loaded"]);
            Assert.Equal("held", report["result"]);
            Assert.Equal(0, exitCode);
        }

        [Fact]
        public async Task A_production_host_renders_pdf_thumbnails_on_linux_only_and_says_so_elsewhere()
        {
            ProcessStartInfo start = StartInfo();
            start.Environment["DOTNET_ENVIRONMENT"] = "Production";

            (Dictionary<string, string> report, string output, int exitCode) = await RunAsync(start);

            Assert.Equal("passed", report["check.core.imaging"]);
            Assert.Equal("no", report["host.pdfium-loaded"]);
            Assert.Equal("held", report["result"]);
            Assert.Equal(0, exitCode);
            if (OperatingSystem.IsLinux())
            {
                // The production shape: thumbnails render, and the hostile document is stopped.
                Assert.Equal("Enabled", report["pdf.thumbnails"]);
                Assert.NotEqual("none", report["honest.bytes"]);
                Assert.Equal("none", report["hostile.thumbnail"]);
            }
            else
            {
                // No thumbnail, no worker, and the reason in the host's log, once.
                Assert.Equal("DisabledOutsideDevelopment", report["pdf.thumbnails"]);
                Assert.Equal("none", report["honest.bytes"]);
                Assert.Equal("0", report["workers.started"]);
                Assert.Equal(output.IndexOf("PDF thumbnails are disabled on this host", StringComparison.Ordinal), output.LastIndexOf("PDF thumbnails are disabled on this host", StringComparison.Ordinal));
                Assert.Contains("PDF thumbnails are disabled on this host", output, StringComparison.Ordinal);
            }
        }

        /// <summary>Runs the host to its end and reads its report.</summary>
        /// <param name="start">The host's start information.</param>
        /// <returns>The report's <c>key=value</c> lines, everything the host wrote, and its exit code.</returns>
        private static async Task<(Dictionary<string, string> Report, string Output, int ExitCode)> RunAsync(ProcessStartInfo start)
        {
            using Process host = Process.Start(start)!;
            string output = await host.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            await host.WaitForExitAsync(TestContext.Current.CancellationToken);

            // The host's log shares its standard output; a report line is a bare key, an equals
            // sign and a value.
            TestContext.Current.TestOutputHelper?.WriteLine(output);
            var report = output.Split('\n')
                .Select(static line => line.Trim())
                .Where(static line => line.Length > 0 && line.Contains('=', StringComparison.Ordinal) && !line.StartsWith(' '))
                .Select(static line => line.Split('=', 2))
                .Where(static pair => pair[0].All(static c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))
                .ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);
            return (report, output, host.ExitCode);
        }

        /// <summary>
        ///     Starts the suite's host-shaped asset as its own process, on this suite's runtime
        ///     configuration and dependency manifest, which list it and everything it needs, in the
        ///     Development environment, where a host renders PDF thumbnails on every operating system.
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
            start.Environment["DOTNET_ENVIRONMENT"] = "Development";

            foreach (string argument in (IEnumerable<string>)["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, Path.Combine(AppContext.BaseDirectory, "Tellma.Core.Imaging.SmokeHost.dll"), .. arguments])
            {
                start.ArgumentList.Add(argument);
            }

            return start;
        }
    }
}
