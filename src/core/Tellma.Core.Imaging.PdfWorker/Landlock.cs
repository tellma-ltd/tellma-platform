// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     The Linux kernel's Landlock interface: an unprivileged process restricts, by path, which
    ///     files it may reach and how, for the rest of its life. The worker confines itself with it;
    ///     the host asks it whether the kernel offers it at all.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A restriction binds the thread that asks for it and whatever that thread starts
    ///         afterwards, a program it executes included; the kernel offers no way to impose it on
    ///         the process's other threads, which is why the worker starts over once restricted.
    ///     </para>
    ///     <para>
    ///         A ruleset names the rights it handles; a handled right is then refused everywhere
    ///         but beneath the paths a rule grants it on. The worker's ruleset handles every right
    ///         the kernel knows, so whatever a newer kernel can refuse, it does. Each kernel release
    ///         that added rights raised the ABI version: 1 (Linux 5.13) knows reading, writing,
    ///         executing, listing, creating and removing; 2 (5.19) moving and linking a file into
    ///         another directory; 3 (6.2) truncating; 4 (6.7) binding and connecting TCP sockets; 5
    ///         (6.10) device <c>ioctl</c>s; 6 (6.12) scopes, which keep a restricted process from
    ///         signalling, or connecting to the abstract Unix sockets of, any process outside its
    ///         restriction.
    ///     </para>
    /// </remarks>
    internal static unsafe partial class Landlock
    {
        /// <summary>Run the file as a program (<c>LANDLOCK_ACCESS_FS_EXECUTE</c>).</summary>
        public const ulong Execute = 1UL << 0;

        /// <summary>Open the file for writing (<c>LANDLOCK_ACCESS_FS_WRITE_FILE</c>).</summary>
        public const ulong WriteFile = 1UL << 1;

        /// <summary>Open the file for reading (<c>LANDLOCK_ACCESS_FS_READ_FILE</c>).</summary>
        public const ulong ReadFile = 1UL << 2;

        /// <summary>List the directory (<c>LANDLOCK_ACCESS_FS_READ_DIR</c>).</summary>
        public const ulong ReadDirectory = 1UL << 3;

        // The system calls, numbered alike on x64 (syscall_64.tbl) and arm64 (asm-generic/unistd.h).
        private const long CreateRulesetCall = 444;
        private const long AddRuleCall = 445;
        private const long RestrictSelfCall = 446;

        // landlock_create_ruleset's flag that asks for the ABI version instead of a ruleset, and the
        // type of a rule that grants rights on a file or on a directory and everything beneath it.
        private const long CreateRulesetVersion = 1;
        private const long RulePathBeneath = 1;

        // Section: the rights, by the ABI that introduced them (linux/landlock.h). ABI 1's thirteen
        // filesystem rights: the four above, then removing a directory or a file and making a
        // character device, a directory, a regular file, a socket, a FIFO, a block device or a
        // symbolic link.
        private const ulong AbiOneFilesystemRights = (1UL << 13) - 1;
        private const ulong Refer = 1UL << 13;
        private const ulong Truncate = 1UL << 14;
        private const ulong DeviceControl = 1UL << 15;
        private const ulong TcpBindAndConnect = (1UL << 0) | (1UL << 1);
        private const ulong AbstractUnixSocketAndSignalScopes = (1UL << 0) | (1UL << 1);

        // The rights a rule on a file, rather than a directory, may carry.
        private const ulong FileRights = Execute | WriteFile | ReadFile | Truncate | DeviceControl;

        /// <summary>The Landlock ABI version the kernel offers; 0 when it offers none.</summary>
        /// <returns>
        ///     The version; 0 when the kernel was built without Landlock, was booted with it
        ///     disabled, or a container's own system-call filter refuses it.
        /// </returns>
        [SupportedOSPlatform("linux")]
        public static int Version()
        {
            long version = SystemCall(CreateRulesetCall, 0, 0, CreateRulesetVersion, 0);
            return version > 0 ? (int)Math.Min(version, int.MaxValue) : 0;
        }

        /// <summary>Every filesystem right an ABI version knows.</summary>
        /// <param name="abi">The version, at least 1.</param>
        /// <returns>The rights.</returns>
        internal static ulong FilesystemRights(int abi)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(abi, 1);
            ulong rights = AbiOneFilesystemRights;
            rights |= abi >= 2 ? Refer : 0;
            rights |= abi >= 3 ? Truncate : 0;
            rights |= abi >= 5 ? DeviceControl : 0;
            return rights;
        }

        /// <summary>
        ///     The rights a rule may grant on one path: those asked for that the ABI knows, and on
        ///     a file only the rights that apply to files, as the kernel requires.
        /// </summary>
        /// <param name="abi">The version, at least 1.</param>
        /// <param name="access">The rights asked for.</param>
        /// <param name="directory">Whether the path is a directory.</param>
        /// <returns>The rights to grant.</returns>
        internal static ulong Grantable(int abi, ulong access, bool directory)
        {
            ulong known = access & FilesystemRights(abi);
            return directory ? known : known & FileRights;
        }

        /// <summary>
        ///     Creates a ruleset that handles every right the ABI knows, the network and the scopes
        ///     included, so that each is refused wherever no rule grants it.
        /// </summary>
        /// <param name="abi">The version the kernel offers, at least 1.</param>
        /// <returns>The ruleset's descriptor, close-on-exec.</returns>
        /// <exception cref="InvalidOperationException">The kernel refused the ruleset.</exception>
        [SupportedOSPlatform("linux")]
        public static int CreateRuleset(int abi)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(abi, 1);

            // Section: struct landlock_ruleset_attr grew with the ABI: the filesystem rights (8
            // bytes), the network rights (16, ABI 4), the scopes (24, ABI 6). Its size tells the
            // kernel which fields the caller knows.
            ulong* attributes = stackalloc ulong[3];
            attributes[0] = FilesystemRights(abi);
            attributes[1] = abi >= 4 ? TcpBindAndConnect : 0;
            attributes[2] = abi >= 6 ? AbstractUnixSocketAndSignalScopes : 0;
            long size = abi >= 6 ? 24 : abi >= 4 ? 16 : 8;
            long ruleset = SystemCall(CreateRulesetCall, (long)attributes, size, 0, 0);
            return ruleset >= 0
                ? (int)ruleset
                : throw new InvalidOperationException($"The kernel refused a Landlock ruleset (errno {Marshal.GetLastPInvokeError()}).");
        }

        /// <summary>Grants rights on the file or directory a descriptor refers to, and beneath it.</summary>
        /// <param name="ruleset">The ruleset.</param>
        /// <param name="descriptor">A descriptor of the path, typically opened with <c>O_PATH</c>.</param>
        /// <param name="access">The rights, which the ruleset must handle.</param>
        /// <exception cref="InvalidOperationException">The kernel refused the rule.</exception>
        [SupportedOSPlatform("linux")]
        public static void Allow(int ruleset, int descriptor, ulong access)
        {
            // struct landlock_path_beneath_attr is packed: the rights, then the descriptor.
            byte* rule = stackalloc byte[12];
            *(ulong*)rule = access;
            *(int*)(rule + 8) = descriptor;
            if (SystemCall(AddRuleCall, ruleset, RulePathBeneath, (long)rule, 0) != 0)
            {
                throw new InvalidOperationException($"The kernel refused a Landlock rule (errno {Marshal.GetLastPInvokeError()}).");
            }
        }

        /// <summary>
        ///     Restricts the calling thread, and everything it starts from now on, to the ruleset.
        ///     The thread must have set <c>no_new_privs</c> first, unless it holds
        ///     <c>CAP_SYS_ADMIN</c>.
        /// </summary>
        /// <param name="ruleset">The ruleset.</param>
        /// <exception cref="InvalidOperationException">The kernel refused the restriction.</exception>
        [SupportedOSPlatform("linux")]
        public static void RestrictSelf(int ruleset)
        {
            if (SystemCall(RestrictSelfCall, ruleset, 0, 0, 0) != 0)
            {
                throw new InvalidOperationException($"The kernel refused the Landlock restriction (errno {Marshal.GetLastPInvokeError()}).");
            }
        }

        /// <summary><c>long syscall(long number, ...)</c>, for the Landlock calls, which the C library does not wrap.</summary>
        /// <param name="number">The system call.</param>
        /// <param name="first">Its first argument.</param>
        /// <param name="second">Its second argument.</param>
        /// <param name="third">Its third argument.</param>
        /// <param name="fourth">Its fourth argument.</param>
        /// <returns>The call's result; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial long SystemCall(long number, long first, long second, long third, long fourth);
    }
}
