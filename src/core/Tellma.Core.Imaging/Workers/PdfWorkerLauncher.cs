// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

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
        ///     The worker's whole environment: the runtime settings above and, on Windows, the
        ///     system root, which system libraries look up. Nothing of the host's own environment,
        ///     its secrets included, reaches a worker.
        /// </summary>
        /// <returns>The variables.</returns>
        internal static Dictionary<string, string> Environment()
        {
            var environment = new Dictionary<string, string>(RuntimeSettings, StringComparer.Ordinal);
            if (OperatingSystem.IsWindows())
            {
                environment["SystemRoot"] = System.Environment.GetFolderPath(System.Environment.SpecialFolder.Windows);
            }

            return environment;
        }

        /// <summary>The command line after the muxer.</summary>
        /// <returns>The arguments.</returns>
        internal List<string> Arguments()
        {
            PdfWorkerFiles files = Files;
            return ["exec", "--runtimeconfig", files.RuntimeConfiguration, "--depsfile", files.Dependencies, files.Worker, .. _arguments];
        }

        /// <summary>Starts a worker, under its operating system's controls.</summary>
        /// <returns>The started worker, not yet ready.</returns>
        /// <exception cref="PdfWorkerException">The worker could not be started.</exception>
        public WorkerProcess Start()
        {
            PdfWorkerFiles files = Files;
            List<string> arguments = Arguments();
            Dictionary<string, string> environment = Environment();

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
