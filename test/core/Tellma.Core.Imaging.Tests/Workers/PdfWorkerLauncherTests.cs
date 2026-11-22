// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.PdfWorker;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     How a worker is started: <c>dotnet exec</c> on the host's own runtime files, told the
    ///     host's process id, under an environment of the worker's runtime settings alone, with a
    ///     managed heap sized to its memory limit on Linux and left to the Job Object on Windows,
    ///     and with a command line that splits back into exactly its arguments; what a confined
    ///     worker may read, which is the runtime, the host's assemblies and native libraries by
    ///     name, the system's libraries and fonts, and nothing else of the host's; and how the
    ///     worker's sandbox reads the descriptors it must close, the host it must die with and the
    ///     allowance it confines itself to.
    /// </summary>
    public sealed class PdfWorkerLauncherTests
    {
        // A Linux host's files, as the composition sees them: the runtime's installation, and the
        // host's own directory, which holds its configuration beside its assemblies.
        private static readonly PdfWorkerFiles LinuxFiles = new(
            "/usr/share/dotnet", "/usr/share/dotnet/dotnet", "/app/Host.runtimeconfig.json", "/app/Host.deps.json", "/app/Tellma.Core.Imaging.PdfWorker.dll");

        [Fact]
        public void A_worker_is_started_with_dotnet_exec_on_the_hosts_runtime_files()
        {
            var launcher = new PdfWorkerLauncher(arguments: ["extra"], confine: false);
            PdfWorkerFiles files = launcher.Files;

            string host = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Assert.False(launcher.Confines);
            Assert.Equal(
                ["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, files.Worker, WorkerSandbox.HostProcessArgument, host, "extra"],
                launcher.Arguments());
        }

        [Fact]
        public void A_confined_linux_worker_is_told_its_allowance_before_the_launchers_arguments()
        {
            var launcher = new PdfWorkerLauncher(arguments: ["extra"], confine: true);
            PdfWorkerFiles files = launcher.Files;

            // Section: on Linux the worker confines itself, so its allowance is on its command
            // line; a Windows worker's AppContainer is the launcher's doing.
            string host = Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            List<string> allowance = OperatingSystem.IsLinux() ? launcher.Allowance.Arguments() : [];
            Assert.True(launcher.Confines);
            Assert.Equal(
                ["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, files.Worker, WorkerSandbox.HostProcessArgument, host, .. allowance, "extra"],
                launcher.Arguments());

            // Section: whatever the operating system, the allowance names the worker and the host's
            // runtime files, and no directory of the host's own.
            Assert.Contains(files.Worker, launcher.Allowance.Read);
            Assert.Contains(files.Dependencies, launcher.Allowance.Read);
            Assert.Contains(files.RuntimeConfiguration, launcher.Allowance.WindowsFiles);
            Assert.DoesNotContain(Path.GetDirectoryName(files.Worker)!, launcher.Allowance.ReadBeneath);
            Assert.DoesNotContain(launcher.Allowance.ReadBeneath, path => files.Worker.StartsWith(path + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void This_hosts_workers_are_confined_wherever_its_operating_system_can_confine_them()
        {
            Assert.Equal(PdfWorkerLauncher.CanConfine, new PdfWorkerLauncher().Confines);
            Assert.Equal(OperatingSystem.IsWindows() || (OperatingSystem.IsLinux() && Landlock.Version() >= 1), PdfWorkerLauncher.CanConfine);
            Assert.False(OperatingSystem.IsMacOS() && PdfWorkerLauncher.CanConfine);
        }

        [Fact]
        public void On_linux_the_allowance_reads_the_hosts_own_files_by_name_and_the_systems_beneath_their_directories()
        {
            var allowance = WorkerAllowance.Compose(
                LinuxFiles,
                trustedAssemblies: ["/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/System.Private.CoreLib.dll", "/app/Host.dll", "/app/SkiaSharp.dll"],
                nativeLibraries: ["/app/libSkiaSharp.so", "/app/libpdfium.so"],
                mappedLibraries:
                [
                    "/usr/share/dotnet/shared/Microsoft.NETCore.App/10.0.0/libcoreclr.so", "/usr/lib/x86_64-linux-gnu/libc.so.6",
                    "/usr/lib/x86_64-linux-gnu/libfontconfig.so.1", "/opt/vendor/libextra.so.2", "/app/libSkiaSharp.so",
                ],
                interpreter: "/lib64/ld-linux-x86-64.so.2",
                fontDirectories: ["/usr/share/fonts"],
                loaderFiles: ["/etc/ld.so.cache"],
                windows: false);

            // Section: the host's files one by one, any other library the host mapped, and the
            // loader's cache; never the host's directory, its configuration or its secrets.
            Assert.Equal(
                [
                    "/app/Host.deps.json", "/app/Host.dll", "/app/Host.runtimeconfig.json", "/app/SkiaSharp.dll", "/app/Tellma.Core.Imaging.PdfWorker.dll",
                    "/app/libSkiaSharp.so", "/app/libpdfium.so", "/etc/ld.so.cache", "/opt/vendor/libextra.so.2",
                ],
                allowance.Read);

            // Section: the runtime's installation, the C library's directory and the fonts, beneath
            // them; and the two programs the worker's restart executes.
            Assert.Equal(["/usr/lib/x86_64-linux-gnu", "/usr/share/dotnet/host", "/usr/share/dotnet/shared", "/usr/share/fonts"], allowance.ReadBeneath);
            Assert.Equal(["/lib64/ld-linux-x86-64.so.2", "/usr/share/dotnet/dotnet"], allowance.Execute);

            // Section: the command line the worker parses back into the same allowance.
            List<string> arguments = [.. allowance.Arguments(), "mode"];
            List<(string Path, ulong Access)> parsed = WorkerConfinement.TakeAllowance(arguments);
            Assert.Equal(["mode"], arguments);
            Assert.Equal(
                [
                    .. allowance.ReadBeneath.Select(static path => (path, Landlock.ReadFile | Landlock.ReadDirectory)),
                    .. allowance.Read.Select(static path => (path, Landlock.ReadFile)),
                    .. allowance.Execute.Select(static path => (path, Landlock.ReadFile | Landlock.Execute)),
                ],
                parsed);
        }

        [Fact]
        public void On_windows_the_allowance_grants_the_hosts_own_files_and_leaves_the_runtimes_installation_to_its_installer()
        {
            var files = new PdfWorkerFiles(
                @"C:\Program Files\dotnet", @"C:\Program Files\dotnet\dotnet.exe", @"D:\app\Host.runtimeconfig.json", @"D:\app\Host.deps.json", @"D:\app\Tellma.Core.Imaging.PdfWorker.dll");

            var allowance = WorkerAllowance.Compose(
                files,
                trustedAssemblies: [@"c:\program files\dotnet\shared\Microsoft.NETCore.App\10.0.0\System.Private.CoreLib.dll", @"D:\app\Host.dll"],
                nativeLibraries: [@"D:\app\libSkiaSharp.dll", @"D:\app\pdfium.dll"],
                mappedLibraries: [],
                interpreter: null,
                fontDirectories: [],
                loaderFiles: [],
                windows: true);

            Assert.Equal(
                [@"D:\app\Host.deps.json", @"D:\app\Host.dll", @"D:\app\Host.runtimeconfig.json", @"D:\app\libSkiaSharp.dll", @"D:\app\pdfium.dll", @"D:\app\Tellma.Core.Imaging.PdfWorker.dll"],
                allowance.WindowsFiles);
            Assert.Equal(allowance.WindowsFiles, allowance.Read);
        }

        [Fact]
        public void The_worker_takes_its_allowance_arguments_and_leaves_the_rest()
        {
            List<string> arguments =
            [
                "mode", WorkerConfinement.ConfineArgument, WorkerConfinement.ReadArgument, "/a", "other",
                WorkerConfinement.ReadBeneathArgument, "/b c", WorkerConfinement.ExecuteArgument, "/d",
            ];

            List<(string Path, ulong Access)> allowed = WorkerConfinement.TakeAllowance(arguments);

            Assert.Equal([("/a", Landlock.ReadFile), ("/b c", Landlock.ReadFile | Landlock.ReadDirectory), ("/d", Landlock.ReadFile | Landlock.Execute)], allowed);
            Assert.Equal(["mode", "other"], arguments);
        }

        [Theory]
        [InlineData("--read", "relative")]
        [InlineData("--read-beneath", "")]
        [InlineData("--execute", "C:/dotnet")]
        public void A_malformed_allowance_argument_is_refused(string argument, string path)
        {
            Assert.Throws<ArgumentException>(() => WorkerConfinement.TakeAllowance([argument, path]));
            Assert.Throws<ArgumentException>(() => WorkerConfinement.TakeAllowance([argument]));
        }

        [Fact]
        public void A_rule_carries_only_the_rights_its_kernel_knows_and_a_files_rule_only_a_files_rights()
        {
            // Section: ABI 1's thirteen rights; then refer (2), truncate (3) and device control (5).
            Assert.Equal(0x1FFFUL, Landlock.FilesystemRights(1));
            Assert.Equal(0x3FFFUL, Landlock.FilesystemRights(2));
            Assert.Equal(0x7FFFUL, Landlock.FilesystemRights(3));
            Assert.Equal(0x7FFFUL, Landlock.FilesystemRights(4));
            Assert.Equal(0xFFFFUL, Landlock.FilesystemRights(5));
            Assert.Equal(0xFFFFUL, Landlock.FilesystemRights(7));

            // Section: listing is a directory's right, which a rule on a file may not carry.
            const ulong readAll = Landlock.ReadFile | Landlock.ReadDirectory | Landlock.Execute;
            Assert.Equal(readAll, Landlock.Grantable(7, readAll, directory: true));
            Assert.Equal(Landlock.ReadFile | Landlock.Execute, Landlock.Grantable(7, readAll, directory: false));
            Assert.Throws<ArgumentOutOfRangeException>(static () => Landlock.FilesystemRights(0));
        }

        [Fact]
        public void The_environment_holds_the_workers_runtime_settings_and_nothing_of_the_hosts()
        {
            Dictionary<string, string> environment = PdfWorkerLauncher.Environment(SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit);

            Assert.Equal("0", environment["DOTNET_gcServer"]);
            Assert.Equal("0", environment["DOTNET_EnableDiagnostics"]);
            Assert.Equal("0", environment["DOTNET_DbgEnableMiniDump"]);
            Assert.Equal("1", environment["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"]);
            Assert.All(environment.Keys, static name => Assert.True(name.StartsWith("DOTNET_", StringComparison.Ordinal) || name is "SystemRoot" or "MALLOC_ARENA_MAX", name));
            Assert.Equal(OperatingSystem.IsWindows(), environment.ContainsKey("SystemRoot"));
            Assert.Equal(OperatingSystem.IsLinux(), environment.ContainsKey("MALLOC_ARENA_MAX"));
            Assert.Equal(OperatingSystem.IsLinux(), environment.ContainsKey("DOTNET_GCHeapHardLimit"));
            Assert.False(environment.ContainsKey("DOTNET_GCRegionRange"));
        }

        [Theory]
        [InlineData(256L * 1024 * 1024, "0x10000000")]
        [InlineData(32L * 1024 * 1024, "0x2000000")]
        public void On_linux_the_managed_heap_is_capped_at_the_memory_limit_and_its_address_range_is_left_to_the_runtime(long limit, string heap)
        {
            // The runtime reserves five times a heap limit. A range narrower than its own runs
            // out before the limit is reached, and the collector then crashes.
            Dictionary<string, string> environment = PdfWorkerLauncher.Environment(limit, windows: false, linux: true);

            Assert.Equal(heap, environment["DOTNET_GCHeapHardLimit"]);
            Assert.False(environment.ContainsKey("DOTNET_GCRegionRange"));
            Assert.Equal("1", environment["MALLOC_ARENA_MAX"]);
            Assert.False(environment.ContainsKey("SystemRoot"));
        }

        [Fact]
        public void On_windows_the_managed_heap_is_left_to_the_job_objects_memory_limit()
        {
            // The job caps a worker's committed memory whatever the runtime reserves, so the heap
            // keeps the runtime's defaults.
            Dictionary<string, string> environment = PdfWorkerLauncher.Environment(SkiaImageProcessorOptions.DefaultPdfWorkerMemoryLimit, windows: true, linux: false);

            Assert.False(environment.ContainsKey("DOTNET_GCHeapHardLimit"));
            Assert.False(environment.ContainsKey("DOTNET_GCRegionRange"));
            Assert.False(environment.ContainsKey("MALLOC_ARENA_MAX"));
            Assert.True(environment.ContainsKey("SystemRoot"));
        }

        [Fact]
        public void The_sandbox_takes_the_host_process_argument_and_leaves_the_rest()
        {
            List<string> arguments = ["mode", WorkerSandbox.HostProcessArgument, "4242", "other"];

            Assert.Equal(4242, WorkerSandbox.TakeHostProcess(arguments));
            Assert.Equal(["mode", "other"], arguments);
            Assert.Null(WorkerSandbox.TakeHostProcess(arguments));
        }

        [Theory]
        [InlineData("0")]
        [InlineData("-5")]
        [InlineData("+5")]
        [InlineData("host")]
        public void A_malformed_host_process_argument_is_refused(string host)
        {
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeHostProcess([WorkerSandbox.HostProcessArgument, host]));
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeHostProcess([WorkerSandbox.HostProcessArgument]));
        }

        [Theory]
        [InlineData(@"C:\Program Files\dotnet\dotnet.exe", @"""C:\Program Files\dotnet\dotnet.exe""")]
        [InlineData(@"plain", @"""plain""")]
        [InlineData(@"a""b", @"""a\""b""")]
        [InlineData(@"trailing\", @"""trailing\\""")]
        [InlineData(@"x\\""y", @"""x\\\\\""y""")]
        [InlineData(@"back\slash", @"""back\slash""")]
        [InlineData("", @"""""")]
        public void Each_argument_is_quoted_so_the_c_runtime_splits_it_back_unchanged(string argument, string quoted)
        {
            Assert.Equal($"\"app\" {quoted}", Win32Text.CommandLine("app", [argument]));
        }

        [Fact]
        public void The_environment_block_is_sorted_and_double_terminated()
        {
            string block = Win32Text.EnvironmentBlock(new Dictionary<string, string> { ["b"] = "2", ["A"] = "1" });

            Assert.Equal("A=1\0b=2\0\0", block);
        }

        [Fact]
        public void The_sandbox_takes_its_descriptor_arguments_and_leaves_the_rest()
        {
            // Only the parsing: entering the sandbox itself would filter this very process.
            List<string> arguments = ["mode", WorkerSandbox.CloseDescriptorArgument, "7", "pipe:[1]", "other", WorkerSandbox.CloseDescriptorArgument, "9", "/tmp/a b"];

            List<(int Descriptor, string Target)> descriptors = WorkerSandbox.TakeInheritedDescriptors(arguments);

            Assert.Equal([(7, "pipe:[1]"), (9, "/tmp/a b")], descriptors);
            Assert.Equal(["mode", "other"], arguments);
        }

        [Theory]
        [InlineData("2", "x")]
        [InlineData("-1", "x")]
        [InlineData("seven", "x")]
        public void A_malformed_descriptor_argument_is_refused(string descriptor, string target)
        {
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeInheritedDescriptors([WorkerSandbox.CloseDescriptorArgument, descriptor, target]));
            Assert.Throws<ArgumentException>(() => WorkerSandbox.TakeInheritedDescriptors([WorkerSandbox.CloseDescriptorArgument, "7"]));
        }
    }
}
