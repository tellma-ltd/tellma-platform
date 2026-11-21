// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     What a Linux host does to itself before it starts a worker, every time: it stops being
    ///     dumpable, which closes its entries under <c>/proc</c> to its account, and it removes the
    ///     pipes its runtime listens on for a debugger, its own and no other process's, and starts
    ///     no worker beside one it cannot remove. What a worker then finds of its host is the
    ///     sandbox suite's; that the runtime names its pipes as the host expects is the smoke
    ///     host's.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed partial class HostIsolationTests
    {
        private const long Limit = 128L * 1024 * 1024;

        // prctl's PR_SET_DUMPABLE.
        private const int SetDumpable = 4;

        [Fact]
        [SupportedOSPlatform("linux")]
        public void Every_worker_start_closes_the_host_to_its_own_account_again_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "A host closes itself to its workers on Linux.");

            // Section: a host that is open again, as a debugger that detaches leaves it: dumpable,
            // with a pipe-shaped file of its own under a key its runtime never made, which only a
            // listing finds. Beside it lies one of a process whose id merely starts with this one's.
            string directory = Path.GetTempPath();
            string own = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{HostIsolation.DebuggerPipePrefix}{Environment.ProcessId}-424242-in"));
            string other = Path.Combine(directory, string.Create(CultureInfo.InvariantCulture, $"{HostIsolation.DebuggerPipePrefix}{Environment.ProcessId}0-424242-in"));
            Assert.Equal(0, Prctl(SetDumpable, 1, 0, 0, 0));
            Assert.True(HostIsolation.IsDumpable, "The host could not be opened again.");
            File.WriteAllBytes(own, []);
            File.WriteAllBytes(other, []);
            try
            {
                // Section: one worker start. A worker dies with the thread that started it, so this
                // test keeps its thread.
                var launcher = new PdfWorkerLauncher(TestWorkers.StandInPath, ["hang-after-request"]);
                using WorkerProcess worker = launcher.Start(Limit);

                // Section: the host is closed again, its own pipes are gone, and the other
                // process's is where it was.
                Assert.False(HostIsolation.IsDumpable, "The host is still dumpable.");
                Assert.False(File.Exists(own), "The host left a debugger pipe of its own.");
                Assert.DoesNotContain(HostIsolation.DebuggerPipes(Environment.ProcessId), File.Exists);
                Assert.True(File.Exists(other), "The host removed another process's debugger pipe.");
            }
            finally
            {
                File.Delete(own);
                File.Delete(other);
            }
        }

        [Fact]
        [SupportedOSPlatform("linux")]
        public void A_debugger_pipe_that_cannot_be_removed_keeps_workers_from_starting_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "A host closes itself to its workers on Linux.");

            // Section: a folder cannot be removed as a file is, and this account can open it: it
            // stands for a pipe the host cannot remove. It lies in a folder of this test's own, so
            // that no other suite's worker meets it.
            DirectoryInfo folder = Directory.CreateTempSubdirectory("tellma-host-isolation-");
            try
            {
                string stuck = Path.Combine(folder.FullName, HostIsolation.DebuggerPipePrefix + "1-1-in");
                string absent = Path.Combine(folder.FullName, HostIsolation.DebuggerPipePrefix + "1-1-out");
                _ = Directory.CreateDirectory(stuck);

                PdfWorkerException exception = Assert.Throws<PdfWorkerException>(() => HostIsolation.RemoveDebuggerPipes([absent, stuck]));

                // Section: the failure is a worker that could not start, and it names the pipe and
                // the way out.
                Assert.Equal(PdfWorkerFailure.Start, exception.Failure);
                Assert.Contains(stuck, exception.Message, StringComparison.Ordinal);
                Assert.Contains("DOTNET_EnableDiagnostics_Debugger=0", exception.Message, StringComparison.Ordinal);
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }

        /// <summary><c>int prctl(int option, unsigned long, unsigned long, unsigned long, unsigned long)</c>.</summary>
        /// <param name="option">The operation.</param>
        /// <param name="argument2">Its first argument.</param>
        /// <param name="argument3">Its second argument.</param>
        /// <param name="argument4">Its third argument.</param>
        /// <param name="argument5">Its fourth argument.</param>
        /// <returns>0 on success; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int Prctl(int option, ulong argument2, ulong argument3, ulong argument4, ulong argument5);
    }
}
