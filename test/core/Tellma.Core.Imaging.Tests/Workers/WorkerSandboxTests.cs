// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Tests.Thumbnails;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     What a worker may do, probed from inside the real worker's sandbox by the stand-in: the
    ///     operating system stops it at its memory limit (native and managed memory alike, and on
    ///     Linux shared memory too, with room to spare for what an honest render maps) and at its
    ///     CPU limit; it cannot start a process, write a file, or open the host's process to read
    ///     or change it, and on Linux cannot open a socket, signal the host, change the host's
    ///     limits or scheduling, create SysV objects or hold a capability, from any of its threads;
    ///     its environment is exactly the launcher's; it holds no handle the host left inheritable;
    ///     it dies with its host without its own cooperation; and on Windows a killed worker ends
    ///     its call even while another process holds its output open.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class WorkerSandboxTests
    {
        private const int Size = 64;
        private const long Limit = 128L * 1024 * 1024;

        // How long a worker may take to start and answer on a loaded machine.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        // A CPU limit no probe comes near, where the limit itself is not under test.
        private static readonly TimeSpan CpuTime = TimeSpan.FromSeconds(60);

        [Theory]
        [InlineData("allocate-native")]
        [InlineData("allocate-managed")]
        public void The_operating_system_stops_a_worker_at_its_memory_limit(string mode)
        {
            using PdfWorkerClient client = TestWorkers.StandIn(mode, Limit);

            string probe = Probe(client);

            // The stand-in allocates and touches 16 MiB blocks until refused: it gets a good part
            // of the limit, and never more than the limit.
            long allocated = long.Parse(probe["allocated: ".Length..], CultureInfo.InvariantCulture);
            TestContext.Current.TestOutputHelper?.WriteLine($"{mode}: {allocated} MiB under a {Limit >> 20} MiB limit.");
            Assert.InRange(allocated, 32, Limit >> 20);
        }

        [Fact]
        [SupportedOSPlatform("linux")]
        public void The_operating_system_stops_a_worker_at_its_address_space_limit_on_linux_shared_memory_included()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "The address-space limit is a Linux resource limit.");
            using PdfWorkerClient client = TestWorkers.StandIn("map-shared", Limit);

            // Shared mappings are not data, so only the address-space limit stops them: at the
            // memory limit and the slack for what a worker has yet to load.
            long allocated = long.Parse(Probe(client)["allocated: ".Length..], CultureInfo.InvariantCulture);
            TestContext.Current.TestOutputHelper?.WriteLine($"map-shared: {allocated} MiB under a {Limit >> 20} MiB limit.");
            Assert.InRange(allocated, 32, (Limit + UnixWorkerProcess.AddressSpaceSlack) >> 20);
        }

        [Theory]
        [InlineData("page")]
        [InlineData("text")]
        [SupportedOSPlatform("linux")]
        public void An_honest_render_maps_less_than_half_the_address_space_slack_on_linux(string document)
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "The address-space limit is a Linux resource limit.");
            byte[] pdf = document == "text" ? DocumentFixtures.TextAndShapes() : DocumentFixtures.HalfBlackPage(612, 792);
            using PdfWorkerClient client = TestWorkers.StandIn("footprint");

            // Section: what the worker holds when its limits are set, read as the host reads it, and
            // what it holds once it has rendered as the real worker renders, read by itself.
            (long Size, long Data) ready = default;
            client.Ready += worker => ready = Memory(worker.Id);
            using var budget = new CancellationTokenSource(Patience);
            ImageFailure report = Assert.Throws<ImageFailure>(() => client.Render(pdf, 256, CpuTime, budget.Token));
            var rendered = report.Message["footprint: ".Length..]
                .Split(',')
                .Select(static pair => pair.Split('='))
                .ToDictionary(static pair => pair[0], static pair => long.Parse(pair[1], CultureInfo.InvariantCulture), StringComparer.Ordinal);

            // Section: the address space the data limit does not count: libraries' code, shared
            // mappings, guard pages. A worker that used its whole memory limit would need this much
            // of the slack; under half of it leaves room for a larger PDFium or runtime.
            long uncounted = rendered["size"] - rendered["data"] - (ready.Size - ready.Data);
            TestContext.Current.TestOutputHelper?.WriteLine(
                $"{document}: ready {ready.Size} kB of address space, {ready.Data} kB of data; rendered {rendered["size"]} kB, {rendered["data"]} kB, peak {rendered["peak"]} kB; {uncounted} kB outside the data limit.");
            Assert.True(ready.Size > 0, "The worker's memory was not read when it was ready.");
            Assert.InRange(uncounted * 1024, 0, UnixWorkerProcess.AddressSpaceSlack / 2);
        }

        [Fact]
        public void A_worker_cannot_act_on_its_host_or_create_sysv_objects_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "The system-call filter is Linux's.");
            using PdfWorkerClient client = TestWorkers.StandIn("host-access");

            var outcomes = Probe(client)["host-access: ".Length..]
                .Split(',')
                .Select(static pair => pair.Split('='))
                .ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);

            // Section: nothing reaches the host, its process group or the SysV namespace.
            string[] refused = ["kill", "kill-group", "tgkill", "tkill", "pidfd_open", "prlimit", "setpriority", "sched_setaffinity", "prctl-pdeathsig", "shmget", "msgget", "semget"];
            Assert.All(refused, name => Assert.Equal((name, "EPERM"), (name, outcomes[name])));

            // Section: the same calls on the worker itself still work, as the runtime needs them.
            Assert.All(["kill-self", "prlimit-self", "prlimit-own"], name => Assert.Equal((name, "allowed"), (name, outcomes[name])));
        }

        [Fact]
        public void A_worker_holds_no_capability_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Capabilities are Linux's.");
            using PdfWorkerClient client = TestWorkers.StandIn("capabilities");

            // Even a worker of a host that runs as root: nothing permitted, effective, inheritable
            // or ambient.
            string[] sets = Probe(client)["capabilities: ".Length..].Split(',');
            Assert.Equal(["CapInh", "CapPrm", "CapEff", "CapAmb"], sets.Select(static set => set.Split('=')[0]));
            Assert.All(sets, static set => Assert.Equal(0UL, ulong.Parse(set.Split('=')[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
        }

        [Fact]
        public void The_filter_covers_threads_that_existed_before_it_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "The system-call filter is Linux's.");
            using PdfWorkerClient client = TestWorkers.StandIn("early-thread");

            // The stand-in's thread was started before the sandbox and probes once it is in place.
            string probe = Probe(client);
            Assert.Contains("socket refused", probe, StringComparison.Ordinal);
            Assert.Contains("process refused", probe, StringComparison.Ordinal);
        }

        [Fact]
        public void A_worker_cannot_open_its_hosts_process_to_read_or_change_it()
        {
            if (OperatingSystem.IsLinux())
            {
                // On Linux the host's memory file is closed to the worker by Yama's ptrace scope,
                // which lets a process reach only its own descendants.
                string scope = File.Exists(ImagingStartupCheck.PtraceScopePath) ? File.ReadAllText(ImagingStartupCheck.PtraceScopePath).Trim() : "absent";
                Assert.SkipWhen(scope is "absent" or "0", $"Yama's ptrace scope is {scope}: the kernel lets the worker open its host's memory; the host warns of it at startup.");
            }

            using PdfWorkerClient client = TestWorkers.StandIn("open-host");

            // On Windows the worker's low integrity level denies it every right that reads the
            // host's memory, writes it, starts a thread in it, takes its handles or changes it. The
            // right to end the host is one the integrity policy does not withhold, which is why a
            // Windows host renders PDF thumbnails only in Development.
            string probe = Probe(client);
            Assert.True(
                probe == "open-host: refused" || (OperatingSystem.IsWindows() && probe == "open-host: opened (terminate)"),
                probe);
        }

        [Fact]
        public void The_operating_system_ends_a_worker_at_its_cpu_limit_even_when_the_host_waits_forever()
        {
            using PdfWorkerClient client = TestWorkers.StandIn("spin");
            var clock = Stopwatch.StartNew();

            PdfWorkerException exception = Assert.Throws<PdfWorkerException>(
                () => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, TimeSpan.FromSeconds(1), CancellationToken.None));

            TestContext.Current.TestOutputHelper?.WriteLine($"Ended after {clock.Elapsed} with exit code {exception.ExitCode}.");
            Assert.Equal(PdfWorkerFailure.Exited, exception.Failure);
            Assert.True(clock.Elapsed < Patience, $"The spinning worker lived {clock.Elapsed}.");
        }

        [Fact]
        public void A_worker_cannot_start_a_process()
        {
            using PdfWorkerClient client = TestWorkers.StandIn("process");

            Assert.StartsWith("process: refused", Probe(client), StringComparison.Ordinal);
        }

        [Fact]
        public void A_worker_cannot_open_a_socket_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux lets an unprivileged process deny another the network; the residual risk is recorded for Windows.");
            using PdfWorkerClient client = TestWorkers.StandIn("socket");

            Assert.StartsWith("socket: refused", Probe(client), StringComparison.Ordinal);
        }

        [Fact]
        public void A_file_write_by_a_worker_is_refused_on_windows_and_ends_the_worker_on_linux()
        {
            Assert.SkipWhen(OperatingSystem.IsMacOS(), "A worker on macOS runs under no operating-system limit.");

            // A folder of the host's own, which the host itself writes to.
            DirectoryInfo folder = Directory.CreateTempSubdirectory("tellma-worker-write-");
            try
            {
                using var client = new PdfWorkerClient(new PdfWorkerLauncher(TestWorkers.StandInPath, ["write-file", folder.FullName]), Limit);
                using var budget = new CancellationTokenSource(Patience);
                if (OperatingSystem.IsWindows())
                {
                    // The worker's low integrity level denies it a write to the host's files.
                    ImageFailure report = Assert.Throws<ImageFailure>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, CpuTime, budget.Token));
                    Assert.Equal("write-file: refused (access)", report.Message);
                }
                else
                {
                    // A write past the zero file-size limit raises SIGXFSZ (25), which ends the worker.
                    PdfWorkerException exception = Assert.Throws<PdfWorkerException>(
                        () => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, CpuTime, budget.Token));
                    Assert.Equal(PdfWorkerFailure.Exited, exception.Failure);
                    Assert.Equal(128 + 25, exception.ExitCode);
                }

                // Nothing was written; on Linux the worker may have created the file, empty.
                Assert.All(folder.GetFiles(), static file => Assert.Equal(0, file.Length));
            }
            finally
            {
                folder.Delete(recursive: true);
            }
        }

        [Fact]
        public void A_worker_runs_with_exactly_the_launchers_environment()
        {
            using PdfWorkerClient client = TestWorkers.StandIn("environment");

            string[] names = Probe(client)["environment: ".Length..].Split(',');

            Assert.Equal(PdfWorkerLauncher.Environment(SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit).Keys.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
            Assert.DoesNotContain(names, static name => name.Contains("PATH", StringComparison.OrdinalIgnoreCase) || name.Contains("HOME", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void A_worker_holds_no_handle_the_host_left_inheritable()
        {
            using var marker = new InheritableMarker();
            if (OperatingSystem.IsLinux())
            {
                // The host sees the stray descriptor, and the worker is told to close it.
                Assert.Contains(UnixWorkerProcess.InheritableDescriptors(), d => d.Descriptor == marker.Value);
            }

            using PdfWorkerClient client = TestWorkers.StandIn("hang-after-request");
            bool? held = null;
            client.Ready += worker => held = marker.IsHeldBy(worker.Id);
            using var budget = new CancellationTokenSource();
            client.Ready += _ => budget.CancelAfter(TimeSpan.FromMilliseconds(200));

            Assert.ThrowsAny<OperationCanceledException>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, CpuTime, budget.Token));
            Assert.False(held ?? true, "The worker inherited the host's stray handle.");
        }

        [Fact]
        public void A_worker_whose_input_closes_exits_at_once_even_while_it_renders()
        {
            // The host's pipe to a busy worker breaks when the host dies; the worker must not render
            // on for nobody. Here the host closes the pipe itself mid-render of a costly document.
            // On Linux a worker dies with the thread that started it, so this test keeps its
            // thread: it sleeps and awaits nothing.
            // A generous memory limit, so that the render is still running, not stopped by the
            // limit, when the input closes.
            const long generous = 1024L * 1024 * 1024;
            var launcher = new PdfWorkerLauncher();
            using WorkerProcess worker = launcher.Start(generous);
            Assert.Equal(OperatingSystem.IsLinux(), WorkerProtocol.ReadReady(worker.Output).HasFlag(WorkerCapabilities.SystemCallFilter));
            worker.ApplyLimits(new WorkerLimits(generous, CpuTime));
            WorkerProtocol.WriteRequest(worker.Input, Size, DocumentFixtures.FormFanOut(depth: 8, breadth: 10));
            Thread.Sleep(300);
            Assert.Null(worker.ExitCode);
            var clock = Stopwatch.StartNew();

            worker.Input.Dispose();

            Assert.True(worker.WaitForExit(TimeSpan.FromSeconds(10)), "The worker rendered on after its host was gone.");
            TestContext.Current.TestOutputHelper?.WriteLine($"Exited {clock.Elapsed} after its input closed.");
            Assert.Equal(WorkerExitCodes.HostGone, worker.ExitCode);
        }

        [Fact]
        public void A_killed_worker_ends_its_call_even_while_another_process_holds_its_output_open()
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows lets another child inherit a worker's pipe end.");
            using PdfWorkerClient client = TestWorkers.StandIn("hang-after-request");
            SafeFileHandle? held = null;
            using var budget = new CancellationTokenSource();
            client.Ready += worker =>
            {
                // A copy of the worker's output, as a child started elsewhere at the wrong moment
                // would hold it: killing the worker breaks no pipe.
                held = OperatingSystem.IsWindows() ? ((WindowsWorkerProcess)worker).DuplicateChildOutput() : null;
                budget.CancelAfter(TimeSpan.FromMilliseconds(200));
            };

            try
            {
                Task call = Task.Factory.StartNew(
                    () => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, CpuTime, budget.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);

                Assert.True(((IAsyncResult)call).AsyncWaitHandle.WaitOne(Patience), "The call outlived its killed worker.");
                Assert.NotNull(held);
                Assert.IsAssignableFrom<OperationCanceledException>(call.Exception?.InnerException ?? new InvalidOperationException("The call completed."));
            }
            finally
            {
                // A call stuck on the read ends when the last writer goes.
                held?.Dispose();
            }
        }

        [Fact]
        public async Task A_worker_dies_with_its_host()
        {
            // The suite's host-shaped asset starts a worker that never answers, ignores its input,
            // and waits; killing the host, as a crash would, must take the worker with it, by the
            // operating system's doing alone.
            using Process host = Process.Start(SmokeHostTests.StartInfo("--orphan", TestWorkers.StandInPath))!;
            int worker = -1;
            string? line;
            while ((line = await host.StandardOutput.ReadLineAsync(TestContext.Current.CancellationToken)) is not null)
            {
                if (line.StartsWith("worker.pid=", StringComparison.Ordinal))
                {
                    worker = int.Parse(line["worker.pid=".Length..], CultureInfo.InvariantCulture);
                    break;
                }
            }

            Assert.True(worker > 0, "The host reported no worker.");
            Assert.False(PdfWorkerBudgetTests.IsGone(worker));

            host.Kill();
            await host.WaitForExitAsync(TestContext.Current.CancellationToken);

            await TestWorkers.WaitUntilAsync(() => PdfWorkerBudgetTests.IsGone(worker), TimeSpan.FromSeconds(15));
        }

        /// <summary>A process's address space and private writable memory, from <c>/proc/&lt;pid&gt;/status</c>.</summary>
        /// <param name="id">The process.</param>
        /// <returns>Both, in kB.</returns>
        private static (long Size, long Data) Memory(int id)
        {
            var status = File.ReadAllLines(string.Create(CultureInfo.InvariantCulture, $"/proc/{id}/status"))
                .Select(static line => line.Split(':', StringSplitOptions.TrimEntries))
                .Where(static pair => pair.Length == 2 && pair[0] is "VmSize" or "VmData")
                .ToDictionary(static pair => pair[0], static pair => long.Parse(pair[1].Split(' ')[0], CultureInfo.InvariantCulture), StringComparer.Ordinal);
            return (status["VmSize"], status["VmData"]);
        }

        /// <summary>Runs a probe in the stand-in and returns its report, which arrives as a refusal's detail.</summary>
        /// <param name="client">The stand-in's client.</param>
        /// <returns>The report.</returns>
        private static string Probe(PdfWorkerClient client)
        {
            using var budget = new CancellationTokenSource(Patience);
            ImageFailure report = Assert.Throws<ImageFailure>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, CpuTime, budget.Token));
            return report.Message;
        }
    }
}
