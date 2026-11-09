// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Tests.Thumbnails;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     What a worker may do, probed from inside the real worker's sandbox by the stand-in: the
    ///     operating system stops it at its memory limit (native and managed memory alike) and at
    ///     its CPU limit; it cannot start a process, and on Linux cannot open a socket or write a
    ///     file; its environment is exactly the launcher's; it holds no handle the host left
    ///     inheritable; and it dies with its host.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class WorkerSandboxTests
    {
        private const int Size = 64;
        private const long Limit = 128L * 1024 * 1024;

        // How long a worker may take to start and answer on a loaded machine.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        [Theory]
        [InlineData("allocate-native")]
        [InlineData("allocate-managed")]
        public void The_operating_system_stops_a_worker_at_its_memory_limit(string mode)
        {
            using PdfWorkerClient client = TestWorkers.StandIn(mode, Limit);

            string probe = Probe(client, cpuTime: null);

            // The stand-in allocates and touches 16 MiB blocks until refused: it gets a good part
            // of the limit, and never more than the limit.
            long allocated = long.Parse(probe["allocated: ".Length..], CultureInfo.InvariantCulture);
            TestContext.Current.TestOutputHelper?.WriteLine($"{mode}: {allocated} MiB under a {Limit >> 20} MiB limit.");
            Assert.InRange(allocated, 32, Limit >> 20);
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

            Assert.StartsWith("process: refused", Probe(client, cpuTime: null), StringComparison.Ordinal);
        }

        [Fact]
        public void A_worker_cannot_open_a_socket_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Only Linux lets an unprivileged process deny another the network; the residual risk is recorded for Windows.");
            using PdfWorkerClient client = TestWorkers.StandIn("socket");

            Assert.StartsWith("socket: refused", Probe(client, cpuTime: null), StringComparison.Ordinal);
        }

        [Fact]
        public void A_worker_cannot_write_a_file_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "The file-size limit is a Linux resource limit.");
            using PdfWorkerClient client = TestWorkers.StandIn("write-file");

            // A write past the zero file-size limit raises SIGXFSZ (25), which ends the worker.
            using var budget = new CancellationTokenSource(Patience);
            PdfWorkerException exception = Assert.Throws<PdfWorkerException>(
                () => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, null, budget.Token));

            Assert.Equal(PdfWorkerFailure.Exited, exception.Failure);
            Assert.Equal(128 + 25, exception.ExitCode);
        }

        [Fact]
        public void A_worker_runs_with_exactly_the_launchers_environment()
        {
            using PdfWorkerClient client = TestWorkers.StandIn("environment");

            string[] names = Probe(client, cpuTime: null)["environment: ".Length..].Split(',');

            Assert.Equal(PdfWorkerLauncher.Environment().Keys.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
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

            Assert.ThrowsAny<OperationCanceledException>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, null, budget.Token));
            Assert.False(held ?? true, "The worker inherited the host's stray handle.");
        }

        [Fact]
        public async Task A_worker_whose_input_closes_exits_at_once_even_while_it_renders()
        {
            // The host's pipe to a busy worker breaks when the host dies; the worker must not render
            // on for nobody. Here the host closes the pipe itself mid-render of a costly document.
            var launcher = new PdfWorkerLauncher();
            using WorkerProcess worker = launcher.Start();
            Assert.Equal(OperatingSystem.IsLinux(), WorkerProtocol.ReadReady(worker.Output).HasFlag(WorkerCapabilities.SystemCallFilter));

            // A generous memory limit, so that the render is still running, not stopped by the
            // limit, when the input closes.
            worker.ApplyLimits(new WorkerLimits(1024L * 1024 * 1024, null));
            WorkerProtocol.WriteRequest(worker.Input, Size, DocumentFixtures.FormFanOut(depth: 8, breadth: 10));
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.Null(worker.ExitCode);
            var clock = Stopwatch.StartNew();

            worker.Input.Dispose();

            Assert.True(worker.WaitForExit(TimeSpan.FromSeconds(10)), "The worker rendered on after its host was gone.");
            TestContext.Current.TestOutputHelper?.WriteLine($"Exited {clock.Elapsed} after its input closed.");
            Assert.Equal(WorkerExitCodes.HostGone, worker.ExitCode);
        }

        [Fact]
        public async Task A_worker_dies_with_its_host()
        {
            // The suite's host-shaped asset starts a worker that never answers and waits; killing
            // the host, as a crash would, must take the worker with it.
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

        /// <summary>Runs a probe in the stand-in and returns its report, which arrives as a refusal's detail.</summary>
        /// <param name="client">The stand-in's client.</param>
        /// <param name="cpuTime">The worker's CPU limit.</param>
        /// <returns>The report.</returns>
        private static string Probe(PdfWorkerClient client, TimeSpan? cpuTime)
        {
            using var budget = new CancellationTokenSource(Patience);
            ImageFailure report = Assert.Throws<ImageFailure>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), Size, cpuTime, budget.Token));
            return report.Message;
        }
    }
}
