// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     How the worker's files are confined: what it may read is what the host allows it (the
    ///     runtime, the host's assemblies and native libraries, the system libraries they load, the
    ///     fonts PDFium renders with), and it may write, create, rename or delete nothing.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         On Linux the worker confines itself with <see cref="Landlock" />, in two stages. The
    ///         first runs when the host asks for it (<c>--confine</c> with the allowance): it
    ///         restricts its own thread, then executes the same command line again. A restriction
    ///         binds only the thread that asks for it and what that thread starts, and the runtime
    ///         has threads of its own before any of the worker's code runs; code that took over one
    ///         thread could have any thread of the process act for it. The program executed anew
    ///         inherits the restriction from its first instruction, so every thread of the process
    ///         that reads the upload is confined. The second stage (<c>--confined</c>) only checks
    ///         that the confinement holds.
    ///     </para>
    ///     <para>
    ///         The restriction refuses every right the kernel knows except reading, beneath the
    ///         allowed paths and the process's own <c>/proc/&lt;pid&gt;</c>; executing, only the two
    ///         programs the restart runs (the <c>dotnet</c> muxer and the ELF interpreter); and,
    ///         from kernels that know them, binding or connecting TCP sockets, and signalling or
    ///         connecting to the abstract sockets of any process outside the restriction. Every
    ///         other process's <c>/proc</c> entries are closed with the rest of the filesystem.
    ///         The kernel also refuses a restricted process the <c>ptrace</c> family of access to
    ///         any process outside its restriction, which closes the account's other processes
    ///         beyond what Yama's <c>ptrace_scope</c> does.
    ///     </para>
    ///     <para>
    ///         <c>/proc/&lt;pid&gt;</c> is granted through a descriptor that stays open for the
    ///         process's life: the kernel ties a rule to the inode it names, and the inode of a
    ///         process's <c>/proc</c> directory lasts only while something holds it. A rule grants
    ///         everything beneath its directory, so the entries there that describe the network and
    ///         mount namespaces the worker shares with its host (<c>net/tcp</c>, <c>net/unix</c>,
    ///         <c>mountinfo</c>) stay readable with the rest, as a path's existence, size, owner and
    ///         times do through <c>stat</c>.
    ///     </para>
    ///     <para>
    ///         On Windows the host starts the worker in an AppContainer of its own, which reads
    ///         only what grants it read and writes nothing of the account's; the worker checks
    ///         that its token is one.
    ///     </para>
    /// </remarks>
    internal static partial class WorkerConfinement
    {
        /// <summary>The argument that asks the worker to confine itself and start over: <c>--confine</c>.</summary>
        public const string ConfineArgument = "--confine";

        /// <summary>The argument that allows reading one file: <c>--read &lt;path&gt;</c>.</summary>
        public const string ReadArgument = "--read";

        /// <summary>The argument that allows reading and listing beneath a directory: <c>--read-beneath &lt;path&gt;</c>.</summary>
        public const string ReadBeneathArgument = "--read-beneath";

        /// <summary>The argument that allows reading and executing one program: <c>--execute &lt;path&gt;</c>.</summary>
        public const string ExecuteArgument = "--execute";

        /// <summary>The argument the first stage passes the second: <c>--confined &lt;descriptor&gt;</c>.</summary>
        public const string ConfinedArgument = "--confined";

        // open(2) flags, alike on x64 and arm64 (asm-generic/fcntl.h): read-only, O_PATH, O_CLOEXEC.
        private const int ReadOnly = 0;
        private const int PathOnly = 0x20_0000;
        private const int CloseOnExecFlag = 0x8_0000;

        // fcntl's F_SETFD with FD_CLOEXEC; close_range (436 on x64 and arm64 alike, Linux 5.9)
        // with CLOSE_RANGE_CLOEXEC (Linux 5.11), which marks descriptors close-on-exec instead of
        // closing them; prctl's PR_SET_NO_NEW_PRIVS.
        private const int SetDescriptorFlags = 2;
        private const int CloseOnExecDescriptorFlag = 1;
        private const long CloseRangeCall = 436;
        private const long CloseRangeCloseOnExec = 1 << 2;
        private const int SetNoNewPrivileges = 38;

        // errno values (asm-generic/errno-base.h).
        private const int NoSuchFile = 2;
        private const int AccessDenied = 13;

        /// <summary>
        ///     The first stage: restricts this thread to what the arguments allow, then executes the
        ///     same command line again, without the allowance and with <c>--confined</c>. It returns
        ///     only by throwing.
        /// </summary>
        /// <param name="arguments">The worker's arguments, as the host passed them.</param>
        /// <exception cref="ArgumentException">An allowance argument is malformed.</exception>
        /// <exception cref="InvalidOperationException">The confinement could not be put in place.</exception>
        [SupportedOSPlatform("linux")]
        [DoesNotReturn]
        public static void ConfineAndRestart(IReadOnlyList<string> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);

            // Section: what is allowed, and what the program executed anew gets: the rest, less the
            // descriptors the host named as inherited by accident, which the restart closes.
            List<string> passed = [.. arguments];
            List<(string Path, ulong Access)> allowed = TakeAllowance(passed);
            _ = WorkerSandbox.TakeInheritedDescriptors(passed);

            int abi = Landlock.Version();
            if (abi < 1)
            {
                throw new InvalidOperationException("The host asked the worker to confine itself, but the kernel offers no Landlock.");
            }

            // Section: the ruleset, one rule per allowed path. A path that is gone has nothing to
            // allow; any other failure stops the worker.
            int ruleset = Landlock.CreateRuleset(abi);
            foreach ((string path, ulong access) in allowed)
            {
                int descriptor = Open(path, PathOnly | CloseOnExecFlag);
                if (descriptor < 0)
                {
                    int error = Marshal.GetLastPInvokeError();
                    if (error == NoSuchFile)
                    {
                        continue;
                    }

                    throw new InvalidOperationException($"An allowed path could not be opened (errno {error}): {path}");
                }

                try
                {
                    Landlock.Allow(ruleset, descriptor, Landlock.Grantable(abi, access, Directory.Exists(path)));
                }
                finally
                {
                    _ = Close(descriptor);
                }
            }

            // Section: the process's own /proc directory, through a descriptor the restart inherits
            // and the program keeps open: the rule holds while the directory's inode does.
            int anchor = Open("/proc/self", PathOnly);
            if (anchor < 0)
            {
                throw new InvalidOperationException($"The process's /proc directory could not be opened (errno {Marshal.GetLastPInvokeError()}).");
            }

            Landlock.Allow(ruleset, anchor, Landlock.ReadFile | Landlock.ReadDirectory);

            // Section: what the restart executes, read while the files can still be read: the
            // program, its command line and its environment. The worker's arguments end the
            // command line; what comes before them is the runtime's own (the muxer, exec, the
            // runtime files and the worker's assembly), passed on unchanged.
            string program = new FileInfo("/proc/self/exe").LinkTarget
                ?? throw new InvalidOperationException("The worker's program could not be named.");
            List<byte[]> commandLine = Entries(File.ReadAllBytes("/proc/self/cmdline"));
            List<byte[]> environment = Entries(File.ReadAllBytes("/proc/self/environ"));
            int prefix = commandLine.Count - arguments.Count;
            if (prefix < 1 || !program.StartsWith('/') || program.EndsWith(" (deleted)", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("The worker's program or command line is not what the restart needs.");
            }

            for (int index = 0; index < arguments.Count; index++)
            {
                if (!commandLine[prefix + index].AsSpan(0, commandLine[prefix + index].Length - 1).SequenceEqual(Encoding.UTF8.GetBytes(arguments[index])))
                {
                    throw new InvalidOperationException("The worker's command line does not end with its arguments.");
                }
            }

            List<byte[]> restart = [.. commandLine.Take(prefix)];
            foreach (string argument in (IEnumerable<string>)[.. passed, ConfinedArgument, anchor.ToString(CultureInfo.InvariantCulture)])
            {
                restart.Add([.. Encoding.UTF8.GetBytes(argument), 0]);
            }

            // Section: every descriptor from 3 up but the anchor closes at the restart, those the
            // runtime opened and any the host left inheritable alike; the program executed anew
            // starts with its three pipes and the anchor.
            if (SystemCall(CloseRangeCall, 3, uint.MaxValue, CloseRangeCloseOnExec) != 0 || DescriptorControl(anchor, SetDescriptorFlags, 0) != 0)
            {
                throw new InvalidOperationException($"The worker's descriptors could not be marked close-on-exec (errno {Marshal.GetLastPInvokeError()}).");
            }

            // Section: the restriction, which no_new_privs lets an unprivileged process ask for and
            // which nothing undoes, then the restart under it.
            if (SeccompFilter.Prctl(SetNoNewPrivileges, 1, 0, 0, 0) != 0)
            {
                throw new InvalidOperationException($"The kernel refused no_new_privs (errno {Marshal.GetLastPInvokeError()}).");
            }

            Landlock.RestrictSelf(ruleset);
            Execute(program, restart, environment);
            throw new InvalidOperationException($"The worker could not start over confined (errno {Marshal.GetLastPInvokeError()}).");
        }

        /// <summary>
        ///     The second stage: removes <c>--confined</c> and its descriptor from the arguments, and
        ///     checks that the confinement holds.
        /// </summary>
        /// <param name="arguments">The command line.</param>
        /// <returns>True when the worker was started over confined; false when the argument is absent.</returns>
        /// <exception cref="ArgumentException">The argument is malformed.</exception>
        /// <exception cref="InvalidOperationException">The confinement does not hold.</exception>
        [SupportedOSPlatform("linux")]
        public static bool TakeConfined(List<string> arguments)
        {
            ArgumentNullException.ThrowIfNull(arguments);
            int index = arguments.IndexOf(ConfinedArgument);
            if (index < 0)
            {
                return false;
            }

            if (index + 1 >= arguments.Count
                || !int.TryParse(arguments[index + 1], NumberStyles.None, CultureInfo.InvariantCulture, out int anchor)
                || anchor < 3)
            {
                throw new ArgumentException($"{ConfinedArgument} takes a descriptor number of 3 or more.", nameof(arguments));
            }

            arguments.RemoveRange(index, 2);

            // Section: the anchor is this process's own /proc directory; it stays open, but no
            // longer passes to a program the process might execute.
            string own = string.Create(CultureInfo.InvariantCulture, $"/proc/{Environment.ProcessId}");
            if (!string.Equals(UnixDescriptors.LinkTarget(anchor), own, StringComparison.Ordinal)
                || DescriptorControl(anchor, SetDescriptorFlags, CloseOnExecDescriptorFlag) != 0)
            {
                throw new InvalidOperationException("The worker's /proc anchor is not its own /proc directory.");
            }

            // Section: the confinement holds: the root directory, which no rule allows, is closed.
            int root = Open("/", ReadOnly | CloseOnExecFlag);
            int error = Marshal.GetLastPInvokeError();
            if (root >= 0)
            {
                _ = Close(root);
                throw new InvalidOperationException("The worker was started over confined, but the root directory is open to it.");
            }

            return error == AccessDenied
                ? true
                : throw new InvalidOperationException($"The worker's confinement could not be checked (errno {error}).");
        }

        /// <summary>Whether this process runs in an AppContainer, as the host starts a confined worker on Windows.</summary>
        /// <returns>True when its token is an AppContainer token.</returns>
        [SupportedOSPlatform("windows")]
        public static unsafe bool InAppContainer()
        {
            // GetCurrentProcessToken() is the pseudo handle -4; TokenIsAppContainer is class 29.
            const nint currentProcessToken = -4;
            const int tokenIsAppContainer = 29;
            uint value = 0;
            return GetTokenInformation(currentProcessToken, tokenIsAppContainer, &value, sizeof(uint), out _) != 0 && value != 0;
        }

        /// <summary>Removes <c>--confine</c> and every allowance pair from the arguments.</summary>
        /// <param name="arguments">The command line.</param>
        /// <returns>Each allowed path, absolute, with the rights it is allowed.</returns>
        /// <exception cref="ArgumentException">An allowance argument is malformed.</exception>
        internal static List<(string Path, ulong Access)> TakeAllowance(List<string> arguments)
        {
            _ = arguments.Remove(ConfineArgument);
            var allowed = new List<(string, ulong)>();
            for (int index = 0; index < arguments.Count;)
            {
                ulong access = arguments[index] switch
                {
                    ReadArgument => Landlock.ReadFile,
                    ReadBeneathArgument => Landlock.ReadFile | Landlock.ReadDirectory,
                    ExecuteArgument => Landlock.ReadFile | Landlock.Execute,
                    _ => 0,
                };
                if (access == 0)
                {
                    index++;
                    continue;
                }

                if (index + 1 >= arguments.Count || !arguments[index + 1].StartsWith('/'))
                {
                    throw new ArgumentException($"{arguments[index]} takes an absolute path.", nameof(arguments));
                }

                allowed.Add((arguments[index + 1], access));
                arguments.RemoveRange(index, 2);
            }

            return allowed;
        }

        /// <summary>Splits a block of NUL-terminated entries, keeping each terminator.</summary>
        /// <param name="block">The block, as <c>/proc/self/cmdline</c> or <c>environ</c> holds it.</param>
        /// <returns>The entries.</returns>
        private static List<byte[]> Entries(byte[] block)
        {
            var entries = new List<byte[]>();
            int start = 0;
            for (int index = 0; index < block.Length; index++)
            {
                if (block[index] == 0)
                {
                    entries.Add(block[start..(index + 1)]);
                    start = index + 1;
                }
            }

            return entries;
        }

        /// <summary>Executes a program in place of this one, which on success never returns.</summary>
        /// <param name="program">The program's path.</param>
        /// <param name="arguments">Its NUL-terminated arguments, the first naming it.</param>
        /// <param name="environment">Its NUL-terminated environment entries.</param>
        [SupportedOSPlatform("linux")]
        private static unsafe void Execute(string program, List<byte[]> arguments, List<byte[]> environment)
        {
            // The entries are pinned for the call, which on success replaces the process.
            var pins = new List<GCHandle>(arguments.Count + environment.Count);
            try
            {
                nint* argv = stackalloc nint[arguments.Count + 1];
                nint* envp = stackalloc nint[environment.Count + 1];
                for (int index = 0; index < arguments.Count; index++)
                {
                    pins.Add(GCHandle.Alloc(arguments[index], GCHandleType.Pinned));
                    argv[index] = pins[^1].AddrOfPinnedObject();
                }

                for (int index = 0; index < environment.Count; index++)
                {
                    pins.Add(GCHandle.Alloc(environment[index], GCHandleType.Pinned));
                    envp[index] = pins[^1].AddrOfPinnedObject();
                }

                argv[arguments.Count] = 0;
                envp[environment.Count] = 0;
                _ = ExecuteProgram(program, argv, envp);
            }
            finally
            {
                foreach (GCHandle pin in pins)
                {
                    pin.Free();
                }
            }
        }

        /// <summary><c>int open(const char *path, int flags)</c>.</summary>
        /// <param name="path">The path.</param>
        /// <param name="flags">The flags.</param>
        /// <returns>The descriptor; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int Open(string path, int flags);

        /// <summary><c>int close(int fd)</c>.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int Close(int descriptor);

        /// <summary><c>int fcntl(int fd, int cmd, int arg)</c>.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="command">The command.</param>
        /// <param name="argument">Its argument.</param>
        /// <returns>The command's result; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "fcntl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial int DescriptorControl(int descriptor, int command, int argument);

        /// <summary>
        ///     <c>long syscall(long number, ...)</c>, for <c>close_range(2)</c>, which the C library
        ///     wraps only from glibc 2.34: a kernel that offers Landlock may run under an older one.
        /// </summary>
        /// <param name="number">The system call.</param>
        /// <param name="first">The first descriptor.</param>
        /// <param name="last">The last descriptor, read by the kernel as an unsigned int.</param>
        /// <param name="flags"><c>CLOSE_RANGE_CLOEXEC</c>.</param>
        /// <returns>0 on success; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial long SystemCall(long number, long first, long last, long flags);

        /// <summary><c>int execve(const char *path, char *const argv[], char *const envp[])</c>.</summary>
        /// <param name="path">The program.</param>
        /// <param name="arguments">Its arguments, null-terminated.</param>
        /// <param name="environment">Its environment, null-terminated.</param>
        /// <returns>-1 with <c>errno</c> set; on success it does not return.</returns>
        [LibraryImport("libc", EntryPoint = "execve", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static unsafe partial int ExecuteProgram(string path, nint* arguments, nint* environment);

        /// <summary><c>BOOL GetTokenInformation(HANDLE, TOKEN_INFORMATION_CLASS, LPVOID, DWORD, PDWORD)</c>.</summary>
        /// <param name="token">The token.</param>
        /// <param name="informationClass">What is read.</param>
        /// <param name="information">Receives it.</param>
        /// <param name="length">Its size.</param>
        /// <param name="returnLength">Receives the size written.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("advapi32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows")]
        private static unsafe partial int GetTokenInformation(nint token, int informationClass, void* information, uint length, out uint returnLength);
    }
}
