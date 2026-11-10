// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     A worker on Linux (and, without limits, macOS): started through the runtime's own
    ///     process API, which closes every close-on-exec descriptor at <c>exec</c>, and then capped
    ///     from outside with <c>prlimit</c> once it is ready.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         .NET opens every descriptor close-on-exec, so the worker inherits only its three
    ///         pipes. A descriptor some native library opened without that flag would still reach
    ///         it; the host lists those it holds when it starts a worker, and the worker closes each
    ///         before it reads anything.
    ///     </para>
    ///     <para>
    ///         The memory cap is two limits. <c>RLIMIT_DATA</c> caps the private writable memory the
    ///         worker may add beyond what its runtime holds once started, the managed heap's commits
    ///         within its reservation included: it is the limit a document's own memory meets.
    ///         <c>RLIMIT_AS</c> caps the worker's address space at what it holds once started plus
    ///         the memory limit and a slack, which bounds what the data limit does not count,
    ///         shared mappings among them. That bound is looser than the memory limit: a started
    ///         worker holds about 3.2 GiB of address space at the default limit, nearly all of it
    ///         the runtime's own reservations (just under 2 GiB for compiled code, and five times
    ///         the limit for the managed heap), and code that took over a worker could unmap
    ///         those and map as much shared memory in their place, until the worker is killed.
    ///         <c>RLIMIT_CPU</c> bounds the worker's CPU time even if the host is gone, and core
    ///         dumps and file writes are disabled.
    ///     </para>
    ///     <para>
    ///         The worker asks the kernel to kill it when the host thread that started it ends, and
    ///         its system-call filter keeps that request from being undone; so a worker is started
    ///         from a thread that outlives it.
    ///     </para>
    /// </remarks>
    [UnsupportedOSPlatform("windows")]
    internal sealed partial class UnixWorkerProcess : WorkerProcess
    {
        // Linux resource numbers (asm-generic/resource.h, shared by x64 and arm64).
        private const int CpuLimit = 0;
        private const int FileSizeLimit = 1;
        private const int DataLimit = 2;
        private const int CoreLimit = 4;
        private const int AddressSpaceLimit = 9;

        // /proc reports CPU time in clock ticks of USER_HZ, which is 100 on every Linux.
        private const long TicksPerSecond = 100;

        // O_CLOEXEC as /proc/<pid>/fdinfo shows it, in octal: 02000000.
        private const int CloseOnExec = 0x80000;

        /// <summary>
        ///     The address space a worker may map beyond its memory limit, for what the data limit
        ///     does not count: the code and read-only data of the libraries and assemblies it has yet
        ///     to load once ready (PDFium and Skia first among them), the runtime's shared mappings
        ///     for compiled code, and guard pages. A render maps about 17 MiB of it on x64; the
        ///     suite fails when one maps more than half the slack.
        /// </summary>
        internal const long AddressSpaceSlack = 128L * 1024 * 1024;

        private readonly Process _process;
        private readonly ErrorTail _error;

        /// <summary>Wraps a started worker.</summary>
        /// <param name="process">The process.</param>
        private UnixWorkerProcess(Process process)
        {
            _process = process;
            _error = new ErrorTail(process.StandardError.BaseStream);
        }

        /// <inheritdoc />
        public override int Id => _process.Id;

        /// <inheritdoc />
        public override Stream Input => _process.StandardInput.BaseStream;

        /// <inheritdoc />
        public override Stream Output => _process.StandardOutput.BaseStream;

        /// <inheritdoc />
        public override int? ExitCode => _process.HasExited ? _process.ExitCode : null;

        /// <inheritdoc />
        protected override ErrorTail Error => _error;

        /// <summary>Starts a worker, which the kernel kills when the calling thread ends.</summary>
        /// <param name="application">The executable's full path.</param>
        /// <param name="arguments">Its arguments; the inherited descriptors to close are added.</param>
        /// <param name="environment">Its whole environment.</param>
        /// <param name="workingDirectory">Its working directory.</param>
        /// <returns>The started worker.</returns>
        /// <exception cref="PdfWorkerException">The worker could not be started.</exception>
        public static UnixWorkerProcess Start(
            string application, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, string workingDirectory)
        {
            var start = new ProcessStartInfo(application)
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = workingDirectory,
            };

            // Section: the whole environment is the host's choice; nothing of its own leaks in.
            start.Environment.Clear();
            foreach (KeyValuePair<string, string> variable in environment)
            {
                start.Environment[variable.Key] = variable.Value;
            }

            // Section: the arguments, then each descriptor the worker would inherit by accident.
            foreach (string argument in arguments)
            {
                start.ArgumentList.Add(argument);
            }

            foreach ((int descriptor, string target) in InheritableDescriptors())
            {
                start.ArgumentList.Add(WorkerSandbox.CloseDescriptorArgument);
                start.ArgumentList.Add(descriptor.ToString(CultureInfo.InvariantCulture));
                start.ArgumentList.Add(target);
            }

            try
            {
                Process process = Process.Start(start) ?? throw new PdfWorkerException(PdfWorkerFailure.Start, "The worker could not be started.");
                return new UnixWorkerProcess(process);
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                throw new PdfWorkerException(PdfWorkerFailure.Start, $"The worker could not be started ({application}): {exception.Message}", exception);
            }
        }

        /// <summary>
        ///     The host's descriptors from 3 up that lack close-on-exec, each with what it refers to:
        ///     what a child started now would inherit beyond its three pipes. Empty where there is no
        ///     <c>/proc</c>.
        /// </summary>
        /// <returns>The descriptors and their link targets.</returns>
        internal static List<(int Descriptor, string Target)> InheritableDescriptors()
        {
            var inheritable = new List<(int, string)>();
            if (!OperatingSystem.IsLinux())
            {
                return inheritable;
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries("/proc/self/fdinfo"))
            {
                if (!int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out int descriptor) || descriptor < 3)
                {
                    continue;
                }

                try
                {
                    // fdinfo's "flags:" line is octal and carries O_CLOEXEC when close-on-exec is set.
                    string? flags = File.ReadLines(entry).FirstOrDefault(static line => line.StartsWith("flags:", StringComparison.Ordinal));
                    string? target = new FileInfo(string.Create(CultureInfo.InvariantCulture, $"/proc/self/fd/{descriptor}")).LinkTarget;
                    if (flags is not null && target is not null && (Convert.ToInt32(flags["flags:".Length..].Trim(), 8) & CloseOnExec) == 0)
                    {
                        inheritable.Add((descriptor, target));
                    }
                }
                catch (IOException)
                {
                    // The descriptor closed meanwhile (the enumeration's own among them).
                }
            }

            return inheritable;
        }

        /// <inheritdoc />
        public override void ApplyLimits(WorkerLimits limits)
        {
            if (!OperatingSystem.IsLinux())
            {
                // Elsewhere (macOS, for developers) only the host's wall-clock kill applies.
                return;
            }

            int id = _process.Id;
            try
            {
                // Section: what the worker holds now, its runtime started and nothing of the upload read.
                (long data, long addressSpace) = Memory(id);
                long cpuUsed = CpuTicks(id);

                // Section: the limits, soft and hard alike, so the worker cannot raise them again.
                Set(id, DataLimit, data + limits.Memory, "data");
                Set(id, AddressSpaceLimit, addressSpace + limits.Memory + AddressSpaceSlack, "address space");
                Set(id, CoreLimit, 0, "core dump");
                Set(id, FileSizeLimit, 0, "file size");

                // Whole seconds, rounded up, past what the worker has used already; with the soft and
                // hard limits equal, the kernel kills it with SIGKILL on reaching them.
                long seconds = (long)Math.Ceiling(((double)cpuUsed / TicksPerSecond) + limits.CpuTime.TotalSeconds);
                Set(id, CpuLimit, Math.Max(1, seconds), "CPU time");
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or PdfWorkerException)
            {
                // A worker that ended since its ready frame has no /proc entry or limits left to set:
                // that is the worker's end, not a failure to start one.
                throw WaitForExit(TimeSpan.FromSeconds(1))
                    ? new PdfWorkerException(PdfWorkerFailure.Exited, "The worker ended before its limits were applied.", exception) { ExitCode = ExitCode }
                    : exception as PdfWorkerException ?? new PdfWorkerException(PdfWorkerFailure.Start, $"The worker's limits could not be applied: {exception.Message}", exception);
            }
        }

        /// <inheritdoc />
        public override void Kill()
        {
            try
            {
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // It has exited, or was never started.
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // It is exiting.
            }
        }

        /// <inheritdoc />
        public override bool WaitForExit(TimeSpan timeout)
        {
            try
            {
                return _process.WaitForExit(timeout);
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            Kill();
            _process.Dispose();
        }

        /// <summary>A process's private writable memory and address space, from <c>/proc/&lt;pid&gt;/status</c>.</summary>
        /// <param name="id">The process.</param>
        /// <returns>Both, in bytes.</returns>
        private static (long Data, long AddressSpace) Memory(int id)
        {
            long data = -1, addressSpace = -1;
            foreach (string line in File.ReadLines(string.Create(CultureInfo.InvariantCulture, $"/proc/{id}/status")))
            {
                if (line.StartsWith("VmData:", StringComparison.Ordinal))
                {
                    data = Kilobytes(line);
                }
                else if (line.StartsWith("VmSize:", StringComparison.Ordinal))
                {
                    addressSpace = Kilobytes(line);
                }
            }

            return data >= 0 && addressSpace >= 0
                ? (data, addressSpace)
                : throw new PdfWorkerException(PdfWorkerFailure.Start, $"The worker's memory could not be read from /proc/{id}/status.");

            static long Kilobytes(string line)
            {
                string value = line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
                return long.Parse(value.Split(' ')[0], NumberStyles.None, CultureInfo.InvariantCulture) * 1024;
            }
        }

        /// <summary>A process's user and system CPU time so far, from <c>/proc/&lt;pid&gt;/stat</c>.</summary>
        /// <param name="id">The process.</param>
        /// <returns>The time, in clock ticks.</returns>
        private static long CpuTicks(int id)
        {
            // The command name, in parentheses, may itself hold spaces or parentheses; the fields
            // after the last ')' start with the state (field 3), so utime and stime are 11 and 12.
            string stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{id}/stat"));
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            return long.Parse(fields[11], CultureInfo.InvariantCulture) + long.Parse(fields[12], CultureInfo.InvariantCulture);
        }

        /// <summary>Sets one resource limit on another process, soft and hard, and reads it back.</summary>
        /// <param name="id">The process.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="value">The limit.</param>
        /// <param name="name">The resource's name, for the error.</param>
        private static unsafe void Set(int id, int resource, long value, string name)
        {
            var limit = new ResourceLimit { Current = (ulong)value, Maximum = (ulong)value };
            ResourceLimit applied = default;
            if (ProcessLimit(id, resource, &limit, null) != 0
                || ProcessLimit(id, resource, null, &applied) != 0
                || applied.Current != limit.Current
                || applied.Maximum != limit.Maximum)
            {
                throw new PdfWorkerException(
                    PdfWorkerFailure.Start, $"The worker's {name} limit could not be applied (errno {Marshal.GetLastPInvokeError()}).");
            }
        }

        /// <summary><c>int prlimit(pid_t, int, const struct rlimit *, struct rlimit *)</c>.</summary>
        /// <param name="id">The process.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="limit">The new limit, or null.</param>
        /// <param name="previous">Receives the current limit, or null.</param>
        /// <returns>0 on success; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "prlimit", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static unsafe partial int ProcessLimit(int id, int resource, ResourceLimit* limit, ResourceLimit* previous);

        /// <summary><c>struct rlimit</c>, whose <c>rlim_t</c> is 64-bit on 64-bit Linux.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct ResourceLimit
        {
            /// <summary>The soft limit.</summary>
            public ulong Current;

            /// <summary>The hard limit.</summary>
            public ulong Maximum;
        }
    }
}
