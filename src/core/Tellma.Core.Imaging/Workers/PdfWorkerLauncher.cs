// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.InteropServices;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>Where the files a worker is started from are, resolved once per launcher.</summary>
    /// <param name="Muxer">The <c>dotnet</c> executable of the runtime the host runs on.</param>
    /// <param name="RuntimeConfiguration">The host's <c>.runtimeconfig.json</c>.</param>
    /// <param name="Dependencies">The host's <c>.deps.json</c>.</param>
    /// <param name="Worker">The worker's assembly, beside the host's.</param>
    internal sealed record PdfWorkerFiles(string Muxer, string RuntimeConfiguration, string Dependencies, string Worker);

    /// <summary>
    ///     Starts PDF workers: <c>dotnet exec</c> on the worker assembly with the host's own
    ///     runtime configuration and dependency manifest, so the worker loads exactly the host's
    ///     framework, PDFium and Skia, from wherever the host's build or publish put them, under the
    ///     least environment that runs it.
    /// </summary>
    /// <param name="worker">The worker assembly to start; the PDF worker beside this one when null.</param>
    /// <param name="arguments">Arguments appended after the worker's own; none when null.</param>
    internal sealed class PdfWorkerLauncher(string? worker = null, IReadOnlyList<string>? arguments = null)
    {
        // The worker's runtime settings, which win over the host's runtime configuration:
        // workstation GC with no background thread for a process that renders once, no
        // diagnostics port, debugger or dump (the worker holds an upload), no culture data, and no
        // profile-guided recompilation in a process that lives under a second.
        private static readonly KeyValuePair<string, string>[] RuntimeSettings =
        [
            new("DOTNET_gcServer", "0"),
            new("DOTNET_gcConcurrent", "0"),
            new("DOTNET_EnableDiagnostics", "0"),
            new("DOTNET_DbgEnableMiniDump", "0"),
            new("DOTNET_EnableCrashReport", "0"),
            new("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1"),
            new("DOTNET_TieredPGO", "0"),
        ];

        private readonly Lazy<PdfWorkerFiles> _files = new(() => Locate(worker), LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly IReadOnlyList<string> _arguments = arguments ?? [];

        /// <summary>The files workers are started from.</summary>
        /// <exception cref="PdfWorkerException">A file is missing: the host cannot run workers.</exception>
        public PdfWorkerFiles Files => _files.Value;

        /// <summary>
        ///     The worker's whole environment: the runtime settings above, and what its operating
        ///     system needs. Nothing of the host's own environment, its secrets included, reaches a
        ///     worker.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         On Linux, the managed heap is capped at the memory limit, and the C library keeps
        ///         one malloc arena, which reserves no address space per thread. Without a heap
        ///         limit the runtime reserves tens of gigabytes of address space for the heap when
        ///         it starts, room that an address-space limit set above it would hand to whatever
        ///         a compromised worker mapped in its place. With one it reserves five times the
        ///         limit, its own choice of range, which is left to it: a narrower range runs out
        ///         before the heap limit is reached, and the collector then crashes where an
        ///         allocation failure was due. The runtime's reservation for compiled code, just
        ///         under 2 GiB, has no setting that applies to a process started without an
        ///         address-space limit, so it stays.
        ///     </para>
        ///     <para>
        ///         On Windows, the Job Object caps the worker's committed memory whatever the runtime
        ///         reserves, so the heap is left alone; the worker gets the system root, which system
        ///         libraries look up.
        ///     </para>
        /// </remarks>
        /// <param name="memoryLimit">The worker's memory limit, in bytes.</param>
        /// <returns>The variables.</returns>
        internal static Dictionary<string, string> Environment(long memoryLimit)
        {
            return Environment(memoryLimit, OperatingSystem.IsWindows(), OperatingSystem.IsLinux());
        }

        /// <summary>The worker's whole environment on a given operating system.</summary>
        /// <param name="memoryLimit">The worker's memory limit, in bytes.</param>
        /// <param name="windows">Whether the worker runs on Windows.</param>
        /// <param name="linux">Whether the worker runs on Linux.</param>
        /// <returns>The variables.</returns>
        internal static Dictionary<string, string> Environment(long memoryLimit, bool windows, bool linux)
        {
            var environment = new Dictionary<string, string>(RuntimeSettings, StringComparer.Ordinal);
            if (linux)
            {
                environment["DOTNET_GCHeapHardLimit"] = Hexadecimal(memoryLimit);
                environment["MALLOC_ARENA_MAX"] = "1";
            }

            if (windows)
            {
                environment["SystemRoot"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
            }

            return environment;
        }

        /// <summary>
        ///     The command line after the muxer: the host's runtime files, the worker, the host's
        ///     process id (which the worker checks is its parent's), and the launcher's own arguments.
        /// </summary>
        /// <returns>The arguments.</returns>
        internal List<string> Arguments()
        {
            PdfWorkerFiles files = Files;
            string host = System.Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            return ["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, files.Worker, WorkerSandbox.HostProcessArgument, host, .. _arguments];
        }

        /// <summary>
        ///     Starts a worker, under its operating system's controls. On Linux the worker dies when the
        ///     calling thread ends, so the caller keeps that thread until the worker is gone.
        /// </summary>
        /// <param name="memoryLimit">The memory limit the worker will be held to, in bytes; on Linux its managed heap is sized for it.</param>
        /// <returns>The started worker, not yet ready.</returns>
        /// <exception cref="PdfWorkerException">The worker could not be started.</exception>
        public WorkerProcess Start(long memoryLimit)
        {
            PdfWorkerFiles files = Files;
            List<string> arguments = Arguments();
            Dictionary<string, string> environment = Environment(memoryLimit);

            // The working directory is a system one, never the host's: nothing there is the worker's
            // to read, and a planted library in it could not be found by the loader.
            return OperatingSystem.IsWindows()
                ? WindowsWorkerProcess.Start(files.Muxer, arguments, environment, System.Environment.SystemDirectory)
                : UnixWorkerProcess.Start(files.Muxer, arguments, environment, "/");
        }

        /// <summary>Finds the muxer, the host's runtime files and the worker assembly.</summary>
        /// <param name="worker">The worker assembly; the PDF worker when null.</param>
        /// <returns>The files.</returns>
        /// <exception cref="PdfWorkerException">One is missing.</exception>
        private static PdfWorkerFiles Locate(string? worker)
        {
            // Section: the installation this process runs on: the shared framework's directory is
            // <root>/shared/Microsoft.NETCore.App/<version>.
            string root = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", ".."));

            // Section: the host's dependency manifest, which the host process was started with. The
            // application's own is listed first; when it has none, the first is a framework's.
            string dependencies = (AppContext.GetData("APP_CONTEXT_DEPS_FILES") as string ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault() ?? string.Empty;
            const string depsSuffix = ".deps.json";
            if (!dependencies.EndsWith(depsSuffix, StringComparison.OrdinalIgnoreCase) || !File.Exists(dependencies)
                || IsUnder(dependencies, Path.Combine(root, "shared")))
            {
                throw Missing(
                    "the host's dependency manifest (.deps.json)",
                    "Run the host framework-dependent, as `dotnet build` or `dotnet publish` (not as a single file) lays it out.");
            }

            // Section: the host's runtime configuration, which the SDK names after the same application.
            string runtimeConfiguration = string.Concat(dependencies.AsSpan(0, dependencies.Length - depsSuffix.Length), ".runtimeconfig.json");
            if (!File.Exists(runtimeConfiguration))
            {
                throw Missing($"the host's runtime configuration ({Path.GetFileName(runtimeConfiguration)})", "Ship it beside the host, as `dotnet publish` does.");
            }

            // Section: the muxer of that installation; or, under the SDK, the one it names.
            string muxer = Path.Combine(root, OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet");
            if (!File.Exists(muxer))
            {
                muxer = System.Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? string.Empty;
            }

            if (!File.Exists(muxer))
            {
                throw Missing("the dotnet executable of the runtime the host runs on", "Run the host framework-dependent on an installed .NET runtime, not self-contained.");
            }

            // Section: the worker, beside the host's assemblies.
            string path = worker ?? typeof(WorkerProtocol).Assembly.Location;
            return string.IsNullOrEmpty(path) || !File.Exists(path)
                ? throw Missing("the PDF worker assembly (Tellma.Core.Imaging.PdfWorker.dll)", "Ship it beside the host's assemblies; it comes with the Tellma.Core.Imaging package.")
                : new PdfWorkerFiles(muxer, runtimeConfiguration, dependencies, path);
        }

        /// <summary>A number as the runtime's configuration reads it: hexadecimal.</summary>
        /// <param name="value">The number.</param>
        /// <returns>The text.</returns>
        private static string Hexadecimal(long value)
        {
            return string.Create(CultureInfo.InvariantCulture, $"0x{value:X}");
        }

        /// <summary>Whether a file lies in a directory.</summary>
        /// <param name="file">The file.</param>
        /// <param name="directory">The directory.</param>
        /// <returns>True when it does.</returns>
        private static bool IsUnder(string file, string directory)
        {
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            string folder = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(file).StartsWith(folder, comparison);
        }

        /// <summary>A start failure naming the missing file and the fix.</summary>
        /// <param name="what">What is missing.</param>
        /// <param name="fix">How to provide it.</param>
        /// <returns>The exception.</returns>
        private static PdfWorkerException Missing(string what, string fix)
        {
            return new PdfWorkerException(PdfWorkerFailure.Start, $"PDF workers cannot start: {what} was not found. {fix}");
        }
    }
}
