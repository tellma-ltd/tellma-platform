// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>The worker's exit codes, beside 0 for a result written.</summary>
    internal static class WorkerExitCodes
    {
        /// <summary>The command line named an argument the worker does not know.</summary>
        public const int BadArguments = 64;

        /// <summary>The worker could not put its sandbox in place, and refused to read the upload.</summary>
        public const int SandboxFailed = 65;

        /// <summary>The host's request was not a valid request frame.</summary>
        public const int BadRequest = 66;

        /// <summary>The host closed the worker's input while it worked: the host is gone or gave up.</summary>
        public const int HostGone = 67;

        /// <summary>The worker failed in a way it could not even report.</summary>
        public const int Unreported = 68;
    }

    /// <summary>
    ///     What the worker does to itself before it reads anything of the upload, while its own code
    ///     is still trustworthy: it closes descriptors it inherited by accident, keeps its protocol
    ///     output away from native code, and on Linux asks the kernel to kill it when its host dies,
    ///     leaves its host's session and with it the host's terminal, closes itself to the other
    ///     processes of its account, drops every capability, and installs its system-call filter,
    ///     which keeps the worker from undoing what protects its host. Limits on memory and CPU are
    ///     not here: the host applies those from outside.
    /// </summary>
    internal static partial class WorkerSandbox
    {
        /// <summary>The argument naming an inherited descriptor to close: <c>--close-fd &lt;number&gt; &lt;target&gt;</c>.</summary>
        public const string CloseDescriptorArgument = "--close-fd";

        /// <summary>The argument naming the host's process id: <c>--host-pid &lt;pid&gt;</c>.</summary>
        public const string HostProcessArgument = "--host-pid";

        // prctl options (linux/prctl.h) and the capability ABI (linux/capability.h).
        private const int SetParentDeathSignal = 1;
        private const int SetDumpable = 4;
        private const int AmbientCapabilities = 47;
        private const ulong ClearAllAmbient = 4;
        private const uint CapabilityVersion3 = 0x2008_0522;
        private const ulong KillSignal = 9;

        /// <summary>
        ///     Enters the sandbox: consumes the arguments it knows from <paramref name="arguments" />
        ///     and returns what it enforces, with the protocol's output stream.
        /// </summary>
        /// <param name="arguments">The command line; the sandbox's own arguments are removed from it.</param>
        /// <param name="output">The stream the worker writes its frames to.</param>
        /// <returns>What the worker enforces on itself from now on.</returns>
        /// <exception cref="ArgumentException">A sandbox argument is malformed.</exception>
        /// <exception cref="InvalidOperationException">The sandbox could not be put in place.</exception>
        /// <exception cref="PlatformNotSupportedException">Linux on an architecture the filter is not written for.</exception>
        public static WorkerCapabilities Enter(List<string> arguments, out Stream output)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            List<(int Descriptor, string Target)> inherited = TakeInheritedDescriptors(arguments);
            int? host = TakeHostProcess(arguments);

            if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            {
                // Section: descriptors the host held open without close-on-exec reached this process
                // by accident; each is closed if it still refers to what the host saw.
                foreach ((int descriptor, string target) in inherited)
                {
                    UnixDescriptors.CloseIfStill(descriptor, target);
                }

                // Section: frames go to a private duplicate of standard output, and standard output
                // itself is pointed at standard error, so a native library that prints cannot
                // corrupt a frame.
                output = UnixDescriptors.TakeStandardOutput();
            }
            else
            {
                // Windows hands the worker exactly its three pipes (a handle list), so nothing
                // arrives by accident; native code there has no console to print to.
                output = Console.OpenStandardOutput();
            }

            // Section: on Linux, the kernel's own guarantees. The parent-death signal kills the
            // worker the moment its host dies, with no cooperation of the worker's code; the filter
            // below forbids clearing it. In a session of its own the worker has no controlling
            // terminal, so a host started from a terminal does not lend it that terminal's job
            // control or input. The worker stops being dumpable, as its host has, so the host's
            // other workers cannot open the upload on its input; it does so before it gives up its
            // capabilities, which until then keep a worker without any out of it. With no
            // capability left, a worker of a host that runs as root cannot raise its own limits or
            // override file permissions, nor open a process that is not dumpable. The filter itself
            // is what keeps a compromised renderer off the network and away from the host; without
            // it the worker does not run there.
            if (OperatingSystem.IsLinux())
            {
                if (host is not int parent)
                {
                    throw new ArgumentException($"{HostProcessArgument} is required on Linux.", nameof(arguments));
                }

                DieWithHost(parent);
                LeaveSession();
                CloseToAccount();
                DropCapabilities();
                SeccompFilter.Install();
                return WorkerCapabilities.SystemCallFilter;
            }

            return WorkerCapabilities.None;
        }

        /// <summary>Removes the <c>--host-pid</c> pair from the arguments.</summary>
        /// <param name="arguments">The command line.</param>
        /// <returns>The host's process id, or null when the pair is absent.</returns>
        internal static int? TakeHostProcess(List<string> arguments)
        {
            int index = arguments.IndexOf(HostProcessArgument);
            if (index < 0)
            {
                return null;
            }

            if (index + 1 >= arguments.Count
                || !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int host)
                || host < 1)
            {
                throw new ArgumentException($"{HostProcessArgument} takes the host's process id.", nameof(arguments));
            }

            arguments.RemoveRange(index, 2);
            return host;
        }

        /// <summary>
        ///     Asks the kernel to send SIGKILL to this process when its parent dies (strictly, when the
        ///     parent's thread that started it ends), and stops at once when the parent is already
        ///     gone, or is not the host it should be.
        /// </summary>
        /// <param name="host">The host's process id.</param>
        [SupportedOSPlatform("linux")]
        private static void DieWithHost(int host)
        {
            if (SeccompFilter.Prctl(SetParentDeathSignal, KillSignal, 0, 0, 0) != 0)
            {
                throw new InvalidOperationException($"The kernel refused the parent-death signal (errno {Marshal.GetLastPInvokeError()}).");
            }

            // The host may have died between starting this process and the call above, in which case
            // no signal will come: this process now has another parent.
            if (ParentProcessId() != host)
            {
                throw new InvalidOperationException("The host is gone.");
            }
        }

        /// <summary>
        ///     Starts a session of this process's own. A session starts without a controlling
        ///     terminal, and this process cannot take its host's: the kernel then refuses it the
        ///     calls that act on a controlling terminal, which change the terminal's foreground job
        ///     or, on kernels that still allow it, push input into the terminal for the host's shell
        ///     to run.
        /// </summary>
        [SupportedOSPlatform("linux")]
        private static void LeaveSession()
        {
            // The call is refused only to the leader of a process group, which a process its host
            // has just started is not, unless it already leads a session of its own.
            if (StartSession() < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (SessionOf(0) != Environment.ProcessId)
                {
                    throw new InvalidOperationException($"The process could not leave its host's session (errno {error}).");
                }
            }
        }

        /// <summary>Empties this process's effective, permitted, inheritable and ambient capabilities.</summary>
        [SupportedOSPlatform("linux")]
        private static unsafe void DropCapabilities()
        {
            var header = new CapabilityHeader { Version = CapabilityVersion3, ProcessId = 0 };
            CapabilitySet* sets = stackalloc CapabilitySet[2];
            sets[0] = default;
            sets[1] = default;
            if (CapSet(&header, sets) != 0)
            {
                throw new InvalidOperationException($"The capabilities could not be dropped (errno {Marshal.GetLastPInvokeError()}).");
            }

            // Ambient capabilities need a permitted one to survive, which is gone; clearing them
            // anyway costs nothing, and an old kernel without them refuses harmlessly.
            _ = SeccompFilter.Prctl(AmbientCapabilities, ClearAllAmbient, 0, 0, 0);
        }

        /// <summary>
        ///     Makes this process non-dumpable: its entries under <c>/proc</c> (its descriptors, the
        ///     upload on its input among them, its environment and its memory) are then closed to
        ///     every process of its account that lacks <c>CAP_SYS_PTRACE</c>, as another worker of
        ///     the same host does. From its start until this call a worker is dumpable, as every
        ///     newly started program is: the kernel keeps no such setting across the start.
        /// </summary>
        [SupportedOSPlatform("linux")]
        private static void CloseToAccount()
        {
            if (SeccompFilter.Prctl(SetDumpable, 0, 0, 0, 0) != 0)
            {
                throw new InvalidOperationException($"The process could not be made non-dumpable (errno {Marshal.GetLastPInvokeError()}).");
            }
        }

        /// <summary>
        ///     Ends the process as soon as its input yields anything, or ends: after the request, the
        ///     host writes nothing more, so either means the host is gone or has given up, and a
        ///     render still running is of use to nobody.
        /// </summary>
        /// <param name="input">The worker's input, after the request was read.</param>
        public static void ExitWhenInputEnds(Stream input)
        {
            ArgumentNullException.ThrowIfNull(input);
            var watcher = new Thread(() =>
            {
                try
                {
                    _ = input.ReadByte();
                }
#pragma warning disable CA1031 // A failed read means the pipe is gone, which is the signal itself.
                catch (Exception)
#pragma warning restore CA1031
                {
                }

                ExitNow(WorkerExitCodes.HostGone);
            })
            {
                IsBackground = true,
                Name = "Tellma PDF worker: host watch",
            };
            watcher.Start();
        }

        /// <summary>
        ///     Ends the process at once, running no exit handlers: another thread may be inside
        ///     PDFium, holding locks an orderly exit would wait on.
        /// </summary>
        /// <param name="exitCode">The exit code.</param>
        public static void ExitNow(int exitCode)
        {
            if (OperatingSystem.IsWindows())
            {
                _ = TerminateProcess(-1, (uint)exitCode);
            }
            else
            {
                ImmediateExit(exitCode);
            }

            // Not reached; keeps the compiler and the reader certain the method does not return.
            Environment.FailFast("The worker could not end itself.");
        }

        /// <summary>Removes every <c>--close-fd</c> triple from the arguments.</summary>
        /// <param name="arguments">The command line.</param>
        /// <returns>The descriptors and what the host saw each refer to.</returns>
        internal static List<(int Descriptor, string Target)> TakeInheritedDescriptors(List<string> arguments)
        {
            var inherited = new List<(int, string)>();
            int index;
            while ((index = arguments.IndexOf(CloseDescriptorArgument)) >= 0)
            {
                if (index + 2 >= arguments.Count
                    || !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int descriptor)
                    || descriptor < 3)
                {
                    throw new ArgumentException($"{CloseDescriptorArgument} takes a descriptor number of 3 or more and its target.", nameof(arguments));
                }

                inherited.Add((descriptor, arguments[index + 2]));
                arguments.RemoveRange(index, 3);
            }

            return inherited;
        }

        /// <summary><c>void _exit(int status)</c>.</summary>
        /// <param name="status">The exit code.</param>
        [LibraryImport("libc", EntryPoint = "_exit")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        private static partial void ImmediateExit(int status);

        /// <summary><c>pid_t getppid(void)</c>.</summary>
        /// <returns>The parent's process id.</returns>
        [LibraryImport("libc", EntryPoint = "getppid")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        internal static partial int ParentProcessId();

        /// <summary><c>pid_t setsid(void)</c>.</summary>
        /// <returns>The new session's id; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "setsid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int StartSession();

        /// <summary><c>pid_t getsid(pid_t pid)</c>.</summary>
        /// <param name="process">The process; 0 for this one.</param>
        /// <returns>The id of the process's session; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "getsid", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int SessionOf(int process);

        /// <summary><c>int capset(cap_user_header_t, const cap_user_data_t)</c>.</summary>
        /// <param name="header">The ABI version and process.</param>
        /// <param name="sets">Two capability sets, the low and high words.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "capset", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static unsafe partial int CapSet(CapabilityHeader* header, CapabilitySet* sets);

        /// <summary><c>struct __user_cap_header_struct</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CapabilityHeader
        {
            /// <summary>The ABI version.</summary>
            public uint Version;

            /// <summary>The process; 0 for this one.</summary>
            public int ProcessId;
        }

        /// <summary><c>struct __user_cap_data_struct</c>: one word of each set.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct CapabilitySet
        {
            /// <summary>The effective capabilities.</summary>
            public uint Effective;

            /// <summary>The permitted capabilities.</summary>
            public uint Permitted;

            /// <summary>The inheritable capabilities.</summary>
            public uint Inheritable;
        }

        /// <summary><c>BOOL TerminateProcess(HANDLE process, UINT exitCode)</c>, on this process's pseudo handle.</summary>
        /// <param name="process">The process; -1 for this one.</param>
        /// <param name="exitCode">The exit code.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", EntryPoint = "TerminateProcess")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows")]
        private static partial int TerminateProcess(nint process, uint exitCode);
    }

    /// <summary>The worker's handling of Unix file descriptors.</summary>
    [UnsupportedOSPlatform("windows")]
    internal static partial class UnixDescriptors
    {
        /// <summary>Closes an inherited descriptor, if it still refers to what the host saw it refer to.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="target">Its link target in the host's descriptor table.</param>
        public static void CloseIfStill(int descriptor, string target)
        {
            // A number the runtime has reused since exec refers to something else: closing it
            // would break the runtime, and it is not what was inherited.
            if (string.Equals(LinkTarget(descriptor), target, StringComparison.Ordinal))
            {
                _ = Close(descriptor);
            }
        }

        /// <summary>The link target of one of this process's descriptors, or null when it is not open.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>The target, such as a path or <c>pipe:[1234]</c>.</returns>
        public static string? LinkTarget(int descriptor)
        {
            try
            {
                return new FileInfo(string.Create(CultureInfo.InvariantCulture, $"/proc/self/fd/{descriptor}")).LinkTarget;
            }
            catch (IOException)
            {
                return null;
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        ///     Duplicates standard output into a private descriptor for the protocol, then points
        ///     standard output at standard error.
        /// </summary>
        /// <returns>A stream over the private descriptor.</returns>
        /// <exception cref="InvalidOperationException">A descriptor could not be duplicated.</exception>
        public static Stream TakeStandardOutput()
        {
            int frames = Duplicate(1);
            return frames < 0 || Duplicate2(2, 1) < 0
                ? throw new InvalidOperationException($"Standard output could not be set aside (errno {Marshal.GetLastPInvokeError()}).")
                : new FileStream(new SafeFileHandle(frames, ownsHandle: true), FileAccess.Write, bufferSize: 0);
        }

        /// <summary><c>int close(int fd)</c>.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Close(int descriptor);

        /// <summary><c>int dup(int fd)</c>.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>The new descriptor, or -1.</returns>
        [LibraryImport("libc", EntryPoint = "dup", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Duplicate(int descriptor);

        /// <summary><c>int dup2(int fd, int fd2)</c>.</summary>
        /// <param name="descriptor">The descriptor to copy.</param>
        /// <param name="target">The number it is copied to.</param>
        /// <returns>The target, or -1.</returns>
        [LibraryImport("libc", EntryPoint = "dup2", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Duplicate2(int descriptor, int target);
    }
}
