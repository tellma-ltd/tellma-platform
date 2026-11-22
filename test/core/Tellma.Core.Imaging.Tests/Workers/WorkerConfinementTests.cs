// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Versioning;
using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Tests.Fixtures;
using Tellma.Core.Imaging.Tests.Infrastructure;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     What a confined worker may do with files, probed from inside the real worker's
    ///     confinement by the stand-in: it reads none of the host's files but those it runs on (not
    ///     one in the host's temporary directory, not one beside its own assembly, not the system's
    ///     own) and lists none of their directories; it changes no file at all, neither writing one
    ///     in place, through an open or a shared mapping, nor truncating, renaming, linking to or
    ///     deleting it, nor making anything beside it; it reads nothing of another process under
    ///     <c>/proc</c>, its host's, another worker's or the first process's, and lists no process,
    ///     while its own entries stay open to it; it reaches no network; it opens neither its host's
    ///     process nor another worker's; and a thread it started before its sandbox is held alike.
    ///     On Linux Landlock itself refuses each read and change it governs, before the system-call
    ///     filter is there to refuse the changes first. An unconfined worker reads what a confined
    ///     one cannot, so the probes would see a confinement that is missing. A confined worker
    ///     still renders.
    /// </summary>
    [Collection(SerialSuites.Name)]
    public sealed class WorkerConfinementTests
    {
        private const long Limit = 128L * 1024 * 1024;

        // The environment variable that, set to 1, makes a Linux host without Landlock fail these
        // tests instead of skipping them, as CI's Linux leg does.
        private const string RequireLandlockVariable = "TELLMA_REQUIRE_LANDLOCK";

        // What the host's file holds, which no probe may change.
        private const string VictimText = "The host's own file, which no worker may read or change.";

        // How long a worker may take to start and answer on a loaded machine.
        private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

        // A CPU limit no probe comes near.
        private static readonly TimeSpan CpuTime = TimeSpan.FromSeconds(60);

        // Each attempt the files probe makes, by operating system.
        private static readonly string[] LinuxAttempts =
        [
            "read-victim", "read-beside", "read-system", "list-root", "list-folder",
            "write-victim", "read-write-victim", "append-victim", "truncate-open", "truncate",
            "rename", "link", "symlink", "delete", "create", "create-temp", "mkdir", "rmdir", "mkfifo",
            "chmod", "set-times", "map-shared-write", "mprotect-shared-write",
        ];

        private static readonly string[] WindowsAttempts =
        [
            "read-victim", "read-beside", "list-folder",
            "write-victim", "read-write-victim", "append-victim", "truncate-open", "map-victim", "map-own-write",
            "rename", "link", "delete", "create", "mkdir", "rmdir", "set-attributes", "set-times",
        ];

        [Fact]
        public void A_confined_worker_reads_none_of_the_hosts_files_and_changes_none()
        {
            SkipUnlessConfined();
            using var victim = new VictimFolder();

            // Section: every attempt was made, and every one was refused: on Linux by Landlock
            // (EACCES) or by the filter before it (EPERM), on Windows by the AppContainer.
            Dictionary<string, string> outcomes = Outcomes(Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["files", victim.Folder, victim.Beside]), "files"));
            Assert.Superset((OperatingSystem.IsWindows() ? WindowsAttempts : LinuxAttempts).ToHashSet(StringComparer.Ordinal), outcomes.Keys.ToHashSet(StringComparer.Ordinal));
            Assert.All(outcomes, static outcome => Assert.True(IsRefusal(outcome.Value), $"{outcome.Key} was not refused: {outcome.Value}"));

            // Section: and nothing changed.
            victim.AssertUnchanged();
        }

        [Fact]
        [SupportedOSPlatform("linux")]
        public void Landlock_alone_refuses_every_read_and_change_it_governs_on_linux()
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "Landlock is Linux's.");
            SkipUnlessConfined();
            Assert.SkipWhen(Landlock.Version() < 3, "Landlock governs truncation from its ABI 3; before it the system-call filter alone refuses it.");
            using var victim = new VictimFolder();

            // Section: the same attempts, made once the worker has started over confined but before
            // its system-call filter is installed: each read and change Landlock governs is refused
            // by Landlock itself (EACCES), whatever the filter would refuse first. A shared writable
            // mapping of a file opened for reading the kernel refuses anyway.
            Dictionary<string, string> outcomes = Outcomes(Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["files-before-filter", victim.Folder, victim.Beside]), "files-before-filter"));
            string[] governed = [.. LinuxAttempts.Except(["chmod", "set-times"], StringComparer.Ordinal)];
            Assert.All(governed, name => Assert.Equal((name, "EACCES"), (name, outcomes[name])));

            // Section: a file's mode and times are not Landlock's to govern, which is why the filter
            // refuses their change; here, with no filter yet, they may change. Nothing else did.
            victim.AssertUnchanged(metadata: false);
        }

        [Fact]
        public void An_unconfined_worker_reads_the_files_a_confined_one_cannot()
        {
            Assert.SkipWhen(OperatingSystem.IsMacOS(), "A worker on macOS runs under no operating-system limit.");
            using var victim = new VictimFolder();

            // Section: the same probe, with the confinement off: the reads now succeed, so the
            // refusals above are the confinement's.
            Dictionary<string, string> outcomes = Outcomes(Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["files", victim.Folder, victim.Beside], confine: false), "files"));
            Assert.Equal("done", outcomes["read-victim"]);
            Assert.Equal("done", outcomes["read-beside"]);
            Assert.Equal("done", outcomes["list-folder"]);

            // Section: writes stay refused without it, by the worker's filter on Linux and its
            // integrity level on Windows.
            Assert.True(IsRefusal(outcomes["write-victim"]), outcomes["write-victim"]);
            Assert.True(IsRefusal(outcomes["read-write-victim"]), outcomes["read-write-victim"]);
            victim.AssertUnchanged();
        }

        [Theory]
        [InlineData("host")]
        [InlineData("worker")]
        [InlineData("first")]
        public void A_confined_worker_reads_nothing_of_another_process_under_proc_and_lists_none_on_linux(string process)
        {
            Assert.SkipUnless(OperatingSystem.IsLinux(), "A process's files under /proc are Linux's.");
            SkipUnlessConfined();

            // Section: the host, another worker of the host waiting for its document, or the first
            // process of the machine or container. A worker dies with the thread that started it,
            // so this test keeps its thread.
            using WorkerProcess? other = process == "worker" ? new PdfWorkerLauncher(TestWorkers.StandInPath, ["hang-after-request"]).Start(Limit) : null;
            int id = process switch
            {
                "host" => Environment.ProcessId,
                "worker" => other!.Id,
                _ => 1,
            };
            if (other is not null)
            {
                Assert.True(WorkerProtocol.ReadReady(other.Output).HasFlag(WorkerCapabilities.FilesystemConfinement));
            }

            // Section: what /proc tells of it, all of it refused, and the listing of every process;
            // the worker's own entries stay open to it.
            Dictionary<string, string> outcomes = Outcomes(Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["proc-reads", id.ToString(CultureInfo.InvariantCulture)]), "proc-reads"));
            string[] others = ["status", "stat", "cmdline", "comm", "environ", "maps", "cgroup", "task", "list-proc"];
            Assert.All(others, name => Assert.True(IsRefusal(outcomes[name]), $"{name} was not refused: {outcomes[name]}"));
            Assert.All(["self-status", "self-maps", "thread-self-status"], name => Assert.Equal((name, "opened"), (name, outcomes[name])));
        }

        [Fact]
        public void A_confined_worker_opens_neither_its_hosts_process_nor_another_workers()
        {
            SkipUnlessConfined();

            // Section: another worker of the host, waiting for its document. A worker dies with
            // the thread that started it, so this test keeps its thread.
            var launcher = new PdfWorkerLauncher(TestWorkers.StandInPath, ["hang-after-request"]);
            using WorkerProcess other = launcher.Start(Limit);
            Assert.True(WorkerProtocol.ReadReady(other.Output).HasFlag(WorkerCapabilities.FilesystemConfinement));

            // Section: on Windows every right is refused, even to ask after either process, as each
            // worker is in an AppContainer of its own; on Linux their memory files are closed.
            Assert.Equal("refused", Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["open-host"]), "open-host"));
            Assert.Equal(
                "refused",
                Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["open-process", other.Id.ToString(CultureInfo.InvariantCulture)]), "open-process"));
        }

        [Fact]
        public void A_confined_worker_reaches_no_network_the_loopback_interface_included()
        {
            SkipUnlessConfined();

            // Section: a listener of the host's on the loopback interface, which an unconfined
            // Windows process of the same account would reach.
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            try
            {
                // Section: on Linux the filter refuses the socket itself; on Windows the
                // AppContainer holds no network capability, so the connection is refused.
                string port = ((IPEndPoint)listener.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
                Assert.StartsWith("refused", Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["connect", port]), "connect"), StringComparison.Ordinal);
                Assert.False(listener.Pending(), "The worker's connection reached the listener.");

                // Section: on Windows a worker outside its AppContainer connects, so the refusal
                // above is the AppContainer's.
                if (OperatingSystem.IsWindows())
                {
                    Assert.Equal("connected", Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["connect", port], confine: false), "connect"));
                }
            }
            finally
            {
                listener.Stop();
            }
        }

        [Fact]
        public void A_thread_the_worker_started_before_its_sandbox_is_confined_alike()
        {
            SkipUnlessConfined();
            using var victim = new VictimFolder();

            // The stand-in's thread was started before the sandbox, and reads once it is in place.
            string probe = Probe(new PdfWorkerLauncher(TestWorkers.StandInPath, ["early-thread", victim.Victim]), "early-thread");
            string read = probe[(probe.IndexOf("read file=", StringComparison.Ordinal) + "read file=".Length)..];
            Assert.True(IsRefusal(read), probe);
        }

        [Fact]
        public void A_confined_worker_still_renders()
        {
            SkipUnlessConfined();
            using PdfWorkerClient client = TestWorkers.Real(Limit);
            Assert.True(client.Launcher.Confines);

            // Section: the client refuses a worker its launcher confines that does not report its
            // confinement, so a render is the worker's own word that it rendered confined.
            using var budget = new CancellationTokenSource(Patience);
            RenderedPage page = client.Render(DocumentFixtures.TextAndShapes(), 256, CpuTime, budget.Token);

            // Section: the page itself: its red rectangle, and its white paper.
            DecodedOutput output = ImageInspector.Decode(page.Png);
            Assert.Equal((page.Width, page.Height), (output.Width, output.Height));
            Assert.Contains(output.Pixels, static pixel => ImageInspector.Near(SKColors.Red, pixel));
            Assert.Contains(output.Pixels, static pixel => ImageInspector.Near(SKColors.White, pixel));
        }

        /// <summary>
        ///     Skips a test where this host's operating system cannot confine a worker; fails it
        ///     instead on a Linux host told to require Landlock.
        /// </summary>
        private static void SkipUnlessConfined()
        {
            if (OperatingSystem.IsLinux() && !PdfWorkerLauncher.CanConfine && Environment.GetEnvironmentVariable(RequireLandlockVariable) == "1")
            {
                Assert.Fail($"{RequireLandlockVariable} is 1, but this kernel offers no Landlock (or the container's system-call filter refuses it).");
            }

            Assert.SkipUnless(PdfWorkerLauncher.CanConfine, "This host's operating system cannot confine a worker's files.");
        }

        /// <summary>Whether an outcome is a refusal: <c>EACCES</c> or <c>EPERM</c> on Linux, a denial of access on Windows.</summary>
        /// <param name="outcome">The outcome.</param>
        /// <returns>True for a refusal.</returns>
        private static bool IsRefusal(string outcome)
        {
            return outcome is "EACCES" or "EPERM" or "refused";
        }

        /// <summary>Runs a probe in the stand-in and returns its report, which arrives as a refusal's detail.</summary>
        /// <param name="launcher">The stand-in's launcher.</param>
        /// <param name="mode">The probe, which the report starts with.</param>
        /// <returns>The report.</returns>
        private static string Probe(PdfWorkerLauncher launcher, string mode)
        {
            using var client = new PdfWorkerClient(launcher, Limit);
            using var budget = new CancellationTokenSource(Patience);
            ImageFailure report = Assert.Throws<ImageFailure>(() => client.Render(DocumentFixtures.HalfBlackPage(100, 100), 64, CpuTime, budget.Token));
            TestContext.Current.TestOutputHelper?.WriteLine(report.Message);
            Assert.StartsWith($"{mode}: ", report.Message, StringComparison.Ordinal);
            return report.Message[(mode.Length + 2)..];
        }

        /// <summary>Splits a report of <c>name=outcome</c> pairs.</summary>
        /// <param name="report">The report, after its mode.</param>
        /// <returns>The outcomes by name.</returns>
        private static Dictionary<string, string> Outcomes(string report)
        {
            return report.Split(',').Select(static pair => pair.Split('=')).ToDictionary(static pair => pair[0], static pair => pair[1], StringComparer.Ordinal);
        }

        /// <summary>
        ///     A directory of the host's own, in its temporary directory, holding a file and an empty
        ///     directory; and a file beside the worker's assembly that is not one the worker runs on.
        ///     Each is as it was made until the end, or the test fails.
        /// </summary>
        private sealed class VictimFolder : IDisposable
        {
            private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("tellma-confinement-");
            private readonly DateTime _written;
            private readonly FileAttributes _attributes;
            private readonly UnixFileMode? _mode;

            /// <summary>Makes the directory, its file and the file beside the worker.</summary>
            public VictimFolder()
            {
                Victim = Path.Combine(_folder.FullName, "victim.txt");
                File.WriteAllText(Victim, VictimText);
                _ = Directory.CreateDirectory(Path.Combine(_folder.FullName, "empty"));
                _written = File.GetLastWriteTimeUtc(Victim);
                _attributes = File.GetAttributes(Victim);
                _mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(Victim);
                Beside = Path.Combine(AppContext.BaseDirectory, $"tellma-beside-{Guid.NewGuid():N}.txt");
                File.WriteAllText(Beside, VictimText);
            }

            /// <summary>The directory.</summary>
            public string Folder => _folder.FullName;

            /// <summary>The file in it.</summary>
            public string Victim { get; }

            /// <summary>The file beside the worker's assembly.</summary>
            public string Beside { get; }

            /// <summary>Asserts that the directory holds exactly what it was made with, unchanged.</summary>
            /// <param name="metadata">Whether the file's attributes, mode and times must be unchanged too.</param>
            public void AssertUnchanged(bool metadata = true)
            {
                Assert.Equal(["empty", "victim.txt"], _folder.EnumerateFileSystemInfos().Select(static entry => entry.Name).Order(StringComparer.Ordinal));
                Assert.Empty(Directory.EnumerateFileSystemEntries(Path.Combine(_folder.FullName, "empty")));
                Assert.Equal(VictimText, File.ReadAllText(Victim));
                if (metadata)
                {
                    Assert.Equal(_written, File.GetLastWriteTimeUtc(Victim));
                    Assert.Equal(_attributes, File.GetAttributes(Victim));
                    Assert.Equal(_mode, OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(Victim));
                }

                Assert.Equal(VictimText, File.ReadAllText(Beside));
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _folder.Delete(recursive: true);
                File.Delete(Beside);
            }
        }
    }
}
