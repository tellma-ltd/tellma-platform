// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>
    ///     The worker's Linux system-call filter: a seccomp-bpf program, installed on every thread
    ///     before the upload is read, that the kernel enforces for the rest of the process's life.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It refuses, with <c>EPERM</c>, what rendering a PDF never needs and a compromised
    ///         renderer would want: creating sockets (so no network, the cloud metadata endpoint
    ///         included); running a program or starting a process (threads are still allowed); SysV
    ///         shared memory, semaphores and message queues, whose objects outlive the process and
    ///         escape its memory limits; reading, writing or signalling another process, or changing
    ///         its resource limits, scheduling, priority or memory placement (signals,
    ///         <c>prlimit64</c> and the scheduling calls are allowed on the worker's own process only,
    ///         and pidfds, <c>tkill</c>, I/O priorities and page migration not at all); clearing the
    ///         parent-death signal that ends the worker with its host; and the kernel interfaces
    ///         exploits favour (io_uring, userfaultfd, perf events, BPF, namespaces, the key store).
    ///         <c>clone3</c> answers <c>ENOSYS</c>, because its flags sit behind a pointer the filter
    ///         cannot read; the C library then creates threads with <c>clone</c>, whose flags it can.
    ///         Any other system call is allowed, so the runtime and PDFium work as they would
    ///         anywhere.
    ///     </para>
    ///     <para>
    ///         A process id is a C <c>int</c>, which the kernel reads from the low half of the
    ///         argument register, so the filter compares that half alone: whatever the high half
    ///         holds, the kernel acts on the pid the filter checked.
    ///     </para>
    ///     <para>
    ///         A system call made through another architecture's entry point (32-bit calls on x64,
    ///         or the x32 ABI) kills the process, because its numbers mean different calls. The
    ///         filter is written for x64 and arm64, the server architectures the platform deploys to.
    ///     </para>
    /// </remarks>
    internal static unsafe partial class SeccompFilter
    {
        // Section: the BPF instruction classes and the filter's return values (linux/filter.h,
        // linux/seccomp.h, linux/audit.h).
        private const ushort LoadWordAbsolute = 0x00 | 0x00 | 0x20;
        private const ushort JumpIfEqual = 0x05 | 0x10 | 0x00;
        private const ushort JumpIfGreaterOrEqual = 0x05 | 0x30 | 0x00;
        private const ushort JumpIfAnyBitSet = 0x05 | 0x40 | 0x00;
        private const ushort ReturnConstant = 0x06 | 0x00;
        private const uint Allow = 0x7fff_0000;
        private const uint KillProcess = 0x8000_0000;
        private const uint ErrorNumber = 0x0005_0000;
        private const uint PermissionDenied = 1;
        private const uint NotImplemented = 38;
        private const uint X64Architecture = 0xC000_003E;
        private const uint Arm64Architecture = 0xC000_00B7;
        private const uint X32SystemCallBit = 0x4000_0000;
        private const uint CloneThread = 0x0001_0000;
        private const uint ProcessPriority = 0; // PRIO_PROCESS
        private const uint SetParentDeathSignal = 1; // PR_SET_PDEATHSIG

        // Offsets into struct seccomp_data: the call number, the architecture, and the low words of
        // the first two arguments (little-endian).
        private const uint NumberOffset = 0;
        private const uint ArchitectureOffset = 4;
        private const uint FirstArgumentOffset = 16;
        private const uint SecondArgumentOffset = 24;

        // prctl and seccomp operations.
        private const int SetNoNewPrivileges = 38;
        private const ulong SetModeFilter = 1;
        private const ulong SynchroniseThreads = 1;

        /// <summary>Whether the filter exists for the processor this process runs on.</summary>
        public static bool IsSupported =>
            RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

        /// <summary>Installs the filter on every thread of the process; it cannot be removed afterwards.</summary>
        /// <exception cref="PlatformNotSupportedException">The process does not run on x64 or arm64.</exception>
        /// <exception cref="InvalidOperationException">The kernel refused the filter.</exception>
        [SupportedOSPlatform("linux")]
        public static void Install()
        {
            if (!IsSupported)
            {
                throw new PlatformNotSupportedException($"The worker's system-call filter is written for x64 and arm64, not {RuntimeInformation.ProcessArchitecture}.");
            }

            Instruction[] program = Build(RuntimeInformation.ProcessArchitecture == Architecture.X64, Environment.ProcessId);

            // Section: an unprivileged process may install a filter once it can never gain
            // privileges again; synchronising the threads applies the filter to the runtime's
            // threads that already exist, not only to this one.
            if (Prctl(SetNoNewPrivileges, 1, 0, 0, 0) != 0)
            {
                throw new InvalidOperationException($"The kernel refused no_new_privs (errno {Marshal.GetLastPInvokeError()}).");
            }

            long seccomp = RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 317 : 277;
            fixed (Instruction* instructions = program)
            {
                var filter = new FilterProgram { Length = (ushort)program.Length, Instructions = instructions };
                long result = SystemCall(seccomp, SetModeFilter, SynchroniseThreads, &filter);
                if (result != 0)
                {
                    throw new InvalidOperationException(result > 0
                        ? $"The kernel could not apply the system-call filter to thread {result}."
                        : $"The kernel refused the system-call filter (errno {Marshal.GetLastPInvokeError()}).");
                }
            }
        }

        /// <summary>Builds the filter program for one architecture and one process.</summary>
        /// <param name="x64">True for x64, false for arm64.</param>
        /// <param name="processId">The worker's own process id, the only one its signals and limits may target.</param>
        /// <returns>The program.</returns>
        internal static Instruction[] Build(bool x64, int processId)
        {
            uint self = (uint)processId;

            // Section: the calls refused with EPERM, by number. x64 numbers are from
            // arch/x86/entry/syscalls/syscall_64.tbl, arm64's from include/uapi/asm-generic/unistd.h.
            uint[] refused = x64
                ? [
                    41, 53,                     // socket, socketpair
                    59, 322, 57, 58,            // execve, execveat, fork, vfork
                    101, 310, 311, 312, 438,    // ptrace, process_vm_readv, process_vm_writev, kcmp, pidfd_getfd
                    434, 424, 200,              // pidfd_open, pidfd_send_signal, tkill
                    251, 256, 279,              // ioprio_set, migrate_pages, move_pages
                    29, 30, 31,                 // shmget, shmat, shmctl
                    64, 65, 66, 220,            // semget, semop, semctl, semtimedop
                    68, 69, 70, 71,             // msgget, msgsnd, msgrcv, msgctl
                    425, 426, 427, 323, 298,    // io_uring_setup, io_uring_enter, io_uring_register, userfaultfd, perf_event_open
                    321, 272, 308,              // bpf, unshare, setns
                    248, 249, 250,              // add_key, request_key, keyctl
                ]
                : [
                    198, 199,                   // socket, socketpair
                    221, 281,                   // execve, execveat
                    117, 270, 271, 272, 438,    // ptrace, process_vm_readv, process_vm_writev, kcmp, pidfd_getfd
                    434, 424, 130,              // pidfd_open, pidfd_send_signal, tkill
                    30, 238, 239,               // ioprio_set, migrate_pages, move_pages
                    194, 196, 195,              // shmget, shmat, shmctl
                    190, 193, 191, 192,         // semget, semop, semctl, semtimedop
                    186, 189, 188, 187,         // msgget, msgsnd, msgrcv, msgctl
                    425, 426, 427, 282, 241,    // io_uring_setup, io_uring_enter, io_uring_register, userfaultfd, perf_event_open
                    280, 97, 268,               // bpf, unshare, setns
                    217, 218, 219,              // add_key, request_key, keyctl
                ];

            // Section: the calls allowed only on the worker's own process, by its pid as the first
            // argument; for the last five, 0 (this process) as well.
            (uint Number, bool OrZero)[] ownProcessOnly = x64
                ? [
                    (62, false), (234, false), (129, false), (297, false),  // kill, tgkill, rt_sigqueueinfo, rt_tgsigqueueinfo
                    (302, true), (203, true), (144, true), (142, true),     // prlimit64, sched_setaffinity, sched_setscheduler, sched_setparam
                    (314, true),                                            // sched_setattr
                ]
                : [
                    (129, false), (131, false), (138, false), (240, false), // kill, tgkill, rt_sigqueueinfo, rt_tgsigqueueinfo
                    (261, true), (122, true), (119, true), (118, true),     // prlimit64, sched_setaffinity, sched_setscheduler, sched_setparam
                    (274, true),                                            // sched_setattr
                ];
            uint setPriority = x64 ? 141u : 140u;
            uint prctl = x64 ? 157u : 167u;
            uint clone = x64 ? 56u : 220u;
            const uint clone3 = 435;

            // Section: another architecture's calls kill the process; so do x32 calls on x64.
            var program = new List<Instruction>
            {
                Statement(LoadWordAbsolute, ArchitectureOffset),
                Jump(JumpIfEqual, x64 ? X64Architecture : Arm64Architecture, 1, 0),
                Statement(ReturnConstant, KillProcess),
                Statement(LoadWordAbsolute, NumberOffset),
            };
            if (x64)
            {
                program.Add(Jump(JumpIfGreaterOrEqual, X32SystemCallBit, 0, 1));
                program.Add(Statement(ReturnConstant, KillProcess));
            }

            // Section: each refused call returns EPERM.
            foreach (uint number in refused)
            {
                program.Add(Jump(JumpIfEqual, number, 0, 1));
                program.Add(Statement(ReturnConstant, ErrorNumber | PermissionDenied));
            }

            // Section: each own-process call. A block is entered only for its call and always
            // returns, so the call number stays loaded for the blocks it skips.
            foreach ((uint number, bool orZero) in ownProcessOnly)
            {
                if (orZero)
                {
                    program.Add(Jump(JumpIfEqual, number, 0, 5));
                    program.Add(Statement(LoadWordAbsolute, FirstArgumentOffset));
                    program.Add(Jump(JumpIfEqual, self, 2, 0));
                    program.Add(Jump(JumpIfEqual, 0, 1, 0));
                }
                else
                {
                    program.Add(Jump(JumpIfEqual, number, 0, 4));
                    program.Add(Statement(LoadWordAbsolute, FirstArgumentOffset));
                    program.Add(Jump(JumpIfEqual, self, 1, 0));
                }

                program.Add(Statement(ReturnConstant, ErrorNumber | PermissionDenied));
                program.Add(Statement(ReturnConstant, Allow));
            }

            // Section: setpriority(which, who, prio) only for PRIO_PROCESS and this process, as 0 or
            // its pid: a process group or user would reach the host.
            program.Add(Jump(JumpIfEqual, setPriority, 0, 7));
            program.Add(Statement(LoadWordAbsolute, FirstArgumentOffset));
            program.Add(Jump(JumpIfEqual, ProcessPriority, 0, 3));
            program.Add(Statement(LoadWordAbsolute, SecondArgumentOffset));
            program.Add(Jump(JumpIfEqual, self, 2, 0));
            program.Add(Jump(JumpIfEqual, 0, 1, 0));
            program.Add(Statement(ReturnConstant, ErrorNumber | PermissionDenied));
            program.Add(Statement(ReturnConstant, Allow));

            // Section: prctl may not change the parent-death signal that ends the worker with its
            // host. Any other call skips the block's four remaining instructions.
            program.Add(Jump(JumpIfEqual, prctl, 0, 4));
            program.Add(Statement(LoadWordAbsolute, FirstArgumentOffset));
            program.Add(Jump(JumpIfEqual, SetParentDeathSignal, 0, 1));
            program.Add(Statement(ReturnConstant, ErrorNumber | PermissionDenied));
            program.Add(Statement(ReturnConstant, Allow));

            // Section: clone3 is not implemented, so the C library falls back to clone, which is
            // allowed only to create a thread (CLONE_THREAD in its flags, its first argument).
            program.Add(Jump(JumpIfEqual, clone3, 0, 1));
            program.Add(Statement(ReturnConstant, ErrorNumber | NotImplemented));
            program.Add(Jump(JumpIfEqual, clone, 0, 3));
            program.Add(Statement(LoadWordAbsolute, FirstArgumentOffset));
            program.Add(Jump(JumpIfAnyBitSet, CloneThread, 1, 0));
            program.Add(Statement(ReturnConstant, ErrorNumber | PermissionDenied));
            program.Add(Statement(ReturnConstant, Allow));
            return [.. program];
        }

        /// <summary>A BPF statement: an instruction without jumps.</summary>
        /// <param name="code">The instruction.</param>
        /// <param name="value">Its operand.</param>
        /// <returns>The instruction.</returns>
        private static Instruction Statement(ushort code, uint value)
        {
            return new Instruction { Code = code, Value = value };
        }

        /// <summary>A BPF conditional jump, by instructions skipped when true and when false.</summary>
        /// <param name="code">The comparison.</param>
        /// <param name="value">The value compared with.</param>
        /// <param name="whenTrue">The instructions skipped when the comparison holds.</param>
        /// <param name="whenFalse">The instructions skipped when it does not.</param>
        /// <returns>The instruction.</returns>
        private static Instruction Jump(ushort code, uint value, byte whenTrue, byte whenFalse)
        {
            return new Instruction { Code = code, Value = value, WhenTrue = whenTrue, WhenFalse = whenFalse };
        }

        /// <summary><c>int prctl(int option, unsigned long, unsigned long, unsigned long, unsigned long)</c>.</summary>
        /// <param name="option">The operation.</param>
        /// <param name="argument2">Its first argument.</param>
        /// <param name="argument3">Its second argument.</param>
        /// <param name="argument4">Its third argument.</param>
        /// <param name="argument5">Its fourth argument.</param>
        /// <returns>0 on success; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        internal static partial int Prctl(int option, ulong argument2, ulong argument3, ulong argument4, ulong argument5);

        /// <summary><c>long syscall(long number, ...)</c>, for <c>seccomp(2)</c>, which the C library does not wrap.</summary>
        /// <param name="number">The system call.</param>
        /// <param name="operation">The seccomp operation.</param>
        /// <param name="flags">Its flags.</param>
        /// <param name="filter">The filter program.</param>
        /// <returns>0 on success; with thread synchronisation, the id of a thread that could not be synchronised; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial long SystemCall(long number, ulong operation, ulong flags, FilterProgram* filter);

        /// <summary>One BPF instruction, <c>struct sock_filter</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct Instruction
        {
            /// <summary>The instruction.</summary>
            public ushort Code;

            /// <summary>The instructions skipped when a comparison holds.</summary>
            public byte WhenTrue;

            /// <summary>The instructions skipped when it does not.</summary>
            public byte WhenFalse;

            /// <summary>The operand.</summary>
            public uint Value;
        }

        /// <summary>A BPF program, <c>struct sock_fprog</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct FilterProgram
        {
            /// <summary>The number of instructions.</summary>
            public ushort Length;

            /// <summary>The instructions.</summary>
            public Instruction* Instructions;
        }
    }
}
