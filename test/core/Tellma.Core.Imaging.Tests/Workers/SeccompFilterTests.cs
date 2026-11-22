// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Tests.Workers
{
    /// <summary>
    ///     The worker's Linux system-call filter, run as the kernel runs it, on both architectures:
    ///     another architecture's calls and x32 calls kill, each refused call is refused with EPERM,
    ///     signals, resource limits and scheduling reach the worker's own process only, the
    ///     parent-death signal cannot be changed, fcntl and ioctl cannot name a descriptor's owner
    ///     or its signal, ask for signal-driven I/O or push input into a terminal, no ioctl of the
    ///     families that change a file's attributes is made whatever its request, a file opens for
    ///     reading only, nothing changes a file by name, nothing watches a file or reads its
    ///     extended attributes by name, clone3 and openat2 answer ENOSYS, clone creates threads
    ///     only, and every other call is allowed, number by number. The sandbox and confinement
    ///     suites prove the installed filter's effect in a real worker.
    /// </summary>
    public sealed class SeccompFilterTests
    {
        private const uint X64 = 0xC000_003E;
        private const uint Arm64 = 0xC000_00B7;
        private const uint Allow = 0x7fff_0000;
        private const uint Kill = 0x8000_0000;
        private const uint PermissionDenied = 0x0005_0001;
        private const uint NotImplemented = 0x0005_0026;
        private const ulong CloneThread = 0x0001_0000;

        // The worker's own process id, as the filter is built for it, and its host's.
        private const int Self = 4242;
        private const int Host = 4241;

        // Every call refused whatever its arguments, kept here apart from the filter's own list, so
        // that a number changed in either shows. x64: socket, socketpair; execve, execveat, fork,
        // vfork; ptrace, process_vm_readv, process_vm_writev, kcmp, pidfd_getfd; pidfd_open,
        // pidfd_send_signal, tkill; ioprio_set, migrate_pages, move_pages; shmget, shmat, shmctl;
        // semget, semop, semctl, semtimedop; msgget, msgsnd, msgrcv, msgctl; io_uring_setup,
        // io_uring_enter, io_uring_register, userfaultfd, perf_event_open; bpf, unshare, setns;
        // add_key, request_key, keyctl. Then the calls that change a file by name or through a
        // descriptor opened for reading: truncate, creat, open_by_handle_at; rename, renameat,
        // renameat2, link, linkat; symlink, symlinkat, unlink, unlinkat; mkdir, mkdirat, rmdir,
        // mknod, mknodat; chmod, fchmod, fchmodat, fchmodat2; chown, fchown, lchown, fchownat;
        // utime, utimes, futimesat, utimensat; setxattr, lsetxattr, fsetxattr, setxattrat;
        // removexattr, lremovexattr, fremovexattr, removexattrat; file_setattr. Then the calls
        // that watch a file or read its extended attributes by name: inotify_init,
        // inotify_init1, inotify_add_watch, fanotify_init, fanotify_mark; getxattr, lgetxattr,
        // listxattr, llistxattr; getxattrat, listxattrat.
        private static readonly uint[] RefusedX64 =
        [
            41, 53,
            59, 322, 57, 58,
            101, 310, 311, 312, 438,
            434, 424, 200,
            251, 256, 279,
            29, 30, 31,
            64, 65, 66, 220,
            68, 69, 70, 71,
            425, 426, 427, 323, 298,
            321, 272, 308,
            248, 249, 250,
            76, 85, 304,
            82, 264, 316, 86, 265,
            88, 266, 87, 263,
            83, 258, 84, 133, 259,
            90, 91, 268, 452,
            92, 93, 94, 260,
            132, 235, 261, 280,
            188, 189, 190, 463,
            197, 198, 199, 466,
            469,
            253, 294, 254, 300, 301,
            191, 192, 194, 195,
            464, 465,
        ];

        // The same calls on arm64, which has no fork or vfork, no inotify_init, and of the calls
        // that change a file, only those that take a directory descriptor (and truncate).
        private static readonly uint[] RefusedArm64 =
        [
            198, 199,
            221, 281,
            117, 270, 271, 272, 438,
            434, 424, 130,
            30, 238, 239,
            194, 196, 195,
            190, 193, 191, 192,
            186, 189, 188, 187,
            425, 426, 427, 282, 241,
            280, 97, 268,
            217, 218, 219,
            45, 265,
            38, 276, 37,
            36, 35,
            34, 33,
            52, 53, 452,
            55, 54,
            88,
            5, 6, 7, 463,
            14, 15, 16, 466,
            469,
            26, 27, 262, 263,
            8, 9, 11, 12,
            464, 465,
        ];

        public static TheoryData<bool, uint, uint> Calls => new()
        {
            // x64: socket, socketpair, execve, fork, ptrace, io_uring_setup, unshare refused.
            { true, 41, PermissionDenied },
            { true, 53, PermissionDenied },
            { true, 59, PermissionDenied },
            { true, 57, PermissionDenied },
            { true, 101, PermissionDenied },
            { true, 425, PermissionDenied },
            { true, 272, PermissionDenied },

            // x64: shmget, shmat, shmctl, semget, semop, semctl, semtimedop, msgget, msgsnd, msgrcv,
            // msgctl refused.
            { true, 29, PermissionDenied },
            { true, 30, PermissionDenied },
            { true, 31, PermissionDenied },
            { true, 64, PermissionDenied },
            { true, 65, PermissionDenied },
            { true, 66, PermissionDenied },
            { true, 220, PermissionDenied },
            { true, 68, PermissionDenied },
            { true, 69, PermissionDenied },
            { true, 70, PermissionDenied },
            { true, 71, PermissionDenied },

            // x64: tkill, pidfd_open, pidfd_send_signal, pidfd_getfd, ioprio_set, migrate_pages,
            // move_pages refused.
            { true, 200, PermissionDenied },
            { true, 434, PermissionDenied },
            { true, 424, PermissionDenied },
            { true, 438, PermissionDenied },
            { true, 251, PermissionDenied },
            { true, 256, PermissionDenied },
            { true, 279, PermissionDenied },

            // x64: read, write, mmap, futex, getpid, sched_yield, sched_getaffinity, ioprio_get and
            // shmdt allowed; clone3 not implemented.
            { true, 0, Allow },
            { true, 1, Allow },
            { true, 9, Allow },
            { true, 202, Allow },
            { true, 39, Allow },
            { true, 24, Allow },
            { true, 204, Allow },
            { true, 252, Allow },
            { true, 67, Allow },
            { true, 435, NotImplemented },

            // arm64: socket, execve, ptrace, io_uring_setup refused.
            { false, 198, PermissionDenied },
            { false, 221, PermissionDenied },
            { false, 117, PermissionDenied },
            { false, 425, PermissionDenied },

            // arm64: shmget, shmat, shmctl, semget, semop, semctl, semtimedop, msgget, msgsnd,
            // msgrcv, msgctl refused.
            { false, 194, PermissionDenied },
            { false, 196, PermissionDenied },
            { false, 195, PermissionDenied },
            { false, 190, PermissionDenied },
            { false, 193, PermissionDenied },
            { false, 191, PermissionDenied },
            { false, 192, PermissionDenied },
            { false, 186, PermissionDenied },
            { false, 189, PermissionDenied },
            { false, 188, PermissionDenied },
            { false, 187, PermissionDenied },

            // arm64: tkill, pidfd_open, pidfd_send_signal, pidfd_getfd, ioprio_set, migrate_pages,
            // move_pages refused.
            { false, 130, PermissionDenied },
            { false, 434, PermissionDenied },
            { false, 424, PermissionDenied },
            { false, 438, PermissionDenied },
            { false, 30, PermissionDenied },
            { false, 238, PermissionDenied },
            { false, 239, PermissionDenied },

            // arm64: read, write, mmap, futex, getpid, sched_yield, sched_getaffinity, ioprio_get
            // and shmdt allowed; clone3 not implemented.
            { false, 63, Allow },
            { false, 64, Allow },
            { false, 222, Allow },
            { false, 98, Allow },
            { false, 172, Allow },
            { false, 124, Allow },
            { false, 123, Allow },
            { false, 31, Allow },
            { false, 197, Allow },
            { false, 435, NotImplemented },
        };

        // Each call allowed on the worker's own process only, by architecture, and whether 0 also
        // names that process: kill, tgkill, rt_sigqueueinfo, rt_tgsigqueueinfo, prlimit64,
        // sched_setaffinity, sched_setscheduler, sched_setparam, sched_setattr.
        private static readonly (bool X64, uint Number, bool OrZero)[] OwnProcess =
        [
            (true, 62, false),
            (true, 234, false),
            (true, 129, false),
            (true, 297, false),
            (true, 302, true),
            (true, 203, true),
            (true, 144, true),
            (true, 142, true),
            (true, 314, true),
            (false, 129, false),
            (false, 131, false),
            (false, 138, false),
            (false, 240, false),
            (false, 261, true),
            (false, 122, true),
            (false, 119, true),
            (false, 118, true),
            (false, 274, true),
        ];

        public static TheoryData<bool, uint, bool> OwnProcessCalls
        {
            get
            {
                var calls = new TheoryData<bool, uint, bool>();
                foreach ((bool x64, uint number, bool orZero) in OwnProcess)
                {
                    calls.Add(x64, number, orZero);
                }

                return calls;
            }
        }

        [Theory]
        [MemberData(nameof(Calls))]
        public void Each_call_gets_its_verdict(bool x64, uint number, uint verdict)
        {
            Assert.Equal(verdict, Run(SeccompFilter.Build(x64, Self), number, x64 ? X64 : Arm64, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Every_call_the_filter_does_not_name_is_allowed_and_every_named_one_keeps_its_own_verdict(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint architecture = x64 ? X64 : Arm64;

            // Section: the calls refused when aimed at the host: those refused outright, those
            // allowed on the worker's own process only, setpriority (whose first argument here is
            // no PRIO_PROCESS), clone (whose flags here lack CLONE_THREAD) and, on x64, open (whose
            // flags here, the host's pid, hold O_WRONLY). openat's flags, its third argument, are
            // none here, so it opens for reading.
            HashSet<uint> refused =
            [
                .. x64 ? RefusedX64 : RefusedArm64,
                .. OwnProcess.Where(call => call.X64 == x64).Select(static call => call.Number),
                x64 ? 141u : 140u,
                x64 ? 56u : 220u,
            ];
            if (x64)
            {
                _ = refused.Add(2);
            }

            const uint clone3 = 435, openAt2 = 437;

            // Section: every number, with the host's pid as the first two arguments. One wrong jump
            // in the program would hand some call another call's verdict.
            for (uint number = 0; number < 1024; number++)
            {
                uint expected = number is clone3 or openAt2 ? NotImplemented : refused.Contains(number) ? PermissionDenied : Allow;
                Assert.Equal((number, expected), (number, Run(program, number, architecture, Host, Host)));
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_refused_calls_are_refused(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);

            // Whatever their arguments: the worker's own pid no more than the host's.
            Assert.All(x64 ? RefusedX64 : RefusedArm64, number =>
            {
                Assert.Equal((number, PermissionDenied), (number, Run(program, number, x64 ? X64 : Arm64, Self, Self)));
                Assert.Equal((number, PermissionDenied), (number, Run(program, number, x64 ? X64 : Arm64, 0)));
            });
        }

        [Theory]
        [MemberData(nameof(OwnProcessCalls))]
        public void Signals_limits_and_scheduling_reach_the_workers_own_process_only(bool x64, uint number, bool orZero)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint architecture = x64 ? X64 : Arm64;

            Assert.Equal(Allow, Run(program, number, architecture, Self));
            Assert.Equal(PermissionDenied, Run(program, number, architecture, Host));
            Assert.Equal(PermissionDenied, Run(program, number, architecture, uint.MaxValue));
            Assert.Equal(orZero ? Allow : PermissionDenied, Run(program, number, architecture, 0));

            // The kernel reads a pid as a C int, from the register's low half.
            Assert.Equal(Allow, Run(program, number, architecture, 0xFFFF_FFFF_0000_0000UL | Self));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Setpriority_reaches_the_workers_own_process_only(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint setPriority = x64 ? 141u : 140u;
            uint architecture = x64 ? X64 : Arm64;

            // PRIO_PROCESS (0) for this process, as its pid or 0; never another process, a process
            // group (1) or a user (2).
            Assert.Equal(Allow, Run(program, setPriority, architecture, 0, Self));
            Assert.Equal(Allow, Run(program, setPriority, architecture, 0, 0));
            Assert.Equal(PermissionDenied, Run(program, setPriority, architecture, 0, Host));
            Assert.Equal(PermissionDenied, Run(program, setPriority, architecture, 1, 0));
            Assert.Equal(PermissionDenied, Run(program, setPriority, architecture, 2, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Prctl_cannot_change_the_parent_death_signal(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint prctl = x64 ? 157u : 167u;
            uint architecture = x64 ? X64 : Arm64;

            // PR_SET_PDEATHSIG (1) refused; PR_GET_PDEATHSIG (2) and PR_SET_NAME (15) allowed.
            Assert.Equal(PermissionDenied, Run(program, prctl, architecture, 1));
            Assert.Equal(Allow, Run(program, prctl, architecture, 2));
            Assert.Equal(Allow, Run(program, prctl, architecture, 15));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Fcntl_cannot_name_a_descriptors_owner_or_signal_nor_ask_for_signal_driven_io(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint fcntl = x64 ? 72u : 25u;
            uint architecture = x64 ? X64 : Arm64;
            const ulong descriptor = 3, signalDriven = 0x2000, nonBlocking = 0x800, highHalf = 0xFFFF_FFFF_0000_0000UL;

            // Section: F_SETOWN (8), F_SETSIG (10) and F_SETOWN_EX (15) are refused whatever they
            // name. The kernel reads the command as a C int, from the register's low half.
            Assert.All((ulong[])[8, 10, 15], command =>
            {
                Assert.Equal((command, PermissionDenied), (command, Run(program, fcntl, architecture, descriptor, command, Host)));
                Assert.Equal((command, PermissionDenied), (command, Run(program, fcntl, architecture, descriptor, command, 0)));
                Assert.Equal((command, PermissionDenied), (command, Run(program, fcntl, architecture, descriptor, highHalf | command, 0)));
            });

            // Section: F_SETFL (4) is refused with O_ASYNC among its flags, alone or not, and
            // allowed without it.
            Assert.Equal(PermissionDenied, Run(program, fcntl, architecture, descriptor, 4, signalDriven));
            Assert.Equal(PermissionDenied, Run(program, fcntl, architecture, descriptor, 4, signalDriven | nonBlocking));
            Assert.Equal(PermissionDenied, Run(program, fcntl, architecture, descriptor, 4, highHalf | signalDriven));
            Assert.Equal(Allow, Run(program, fcntl, architecture, descriptor, 4, nonBlocking));
            Assert.Equal(Allow, Run(program, fcntl, architecture, descriptor, 4, 0));

            // Section: every other command is allowed, even one whose argument, no set of flags,
            // has that bit: F_DUPFD (0), F_GETFD (1), F_SETFD (2), F_GETFL (3), F_GETOWN (9),
            // F_GETSIG (11), F_GETOWN_EX (16) and F_DUPFD_CLOEXEC (1030).
            Assert.All((ulong[])[0, 1, 2, 3, 9, 11, 16, 1030], command =>
                Assert.Equal((command, Allow), (command, Run(program, fcntl, architecture, descriptor, command, signalDriven))));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ioctl_cannot_push_input_into_a_terminal_ask_for_signal_driven_io_or_name_a_sockets_owner(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint ioctl = x64 ? 16u : 29u;
            uint architecture = x64 ? X64 : Arm64;

            // Section: TIOCSTI, TIOCLINUX, FIOASYNC, FIOSETOWN and SIOCSPGRP are refused, as are
            // FS_IOC_SETVERSION and FS_IOC32_SETVERSION. The kernel reads the request as a C
            // unsigned int, from the register's low half.
            Assert.All((ulong[])[0x5412, 0x541C, 0x5452, 0x8901, 0x8902, 0x40087602, 0x40047602], request =>
            {
                Assert.Equal((request, PermissionDenied), (request, Run(program, ioctl, architecture, 0, request)));
                Assert.Equal((request, PermissionDenied), (request, Run(program, ioctl, architecture, 0, 0xFFFF_FFFF_0000_0000UL | request)));
            });

            // Section: their neighbours are allowed: TCGETS, TIOCGPGRP, TIOCGWINSZ, FIONREAD,
            // FIONBIO, FIONCLEX, FIOCLEX, FIOGETOWN, SIOCGPGRP and FS_IOC_GETVERSION.
            Assert.All((ulong[])[0x5401, 0x540F, 0x5413, 0x541B, 0x5421, 0x5450, 0x5451, 0x8903, 0x8904, 0x80087601], request =>
                Assert.Equal((request, Allow), (request, Run(program, ioctl, architecture, 0, request))));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ioctl_makes_no_request_of_the_families_that_change_a_files_attributes(bool x64)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint ioctl = x64 ? 16u : 29u;
            uint architecture = x64 ? X64 : Arm64;
            const ulong highHalf = 0xFFFF_FFFF_0000_0000UL;

            // Section: the requests a compromised worker would make through a descriptor opened
            // for reading: FS_IOC_SETFLAGS and FS_IOC32_SETFLAGS, FS_IOC_ENABLE_VERITY,
            // FS_IOC_SET_ENCRYPTION_POLICY, FS_IOC_FSSETXATTR, BTRFS_IOC_SUBVOL_SETFLAGS, FICLONE,
            // FIDEDUPERANGE and F2FS_IOC_SET_PIN_FILE; and reads of the same families, which the
            // worker makes no more than their writes, FS_IOC_GETFLAGS and FS_IOC_FSGETXATTR.
            Assert.All((ulong[])[0x40086602, 0x40046602, 0x40806685, 0x800C6613, 0x401C5820, 0x4008941A, 0x40049409, 0xC0189436, 0x4004F50D, 0x80086601, 0x801C581F], request =>
            {
                Assert.Equal((request, PermissionDenied), (request, Run(program, ioctl, architecture, 0, request)));
                Assert.Equal((request, PermissionDenied), (request, Run(program, ioctl, architecture, 0, highHalf | request)));
            });

            // Section: every request of the four types 'f', 'X', 0x94 and 0xF5, whatever its number,
            // direction and size.
            foreach (ulong type in (ulong[])[0x66, 0x58, 0x94, 0xF5])
            {
                for (ulong number = 0; number < 256; number++)
                {
                    foreach (ulong form in (ulong[])[0x0000_0000, 0x4004_0000, 0x8008_0000, 0xC018_0000, 0xFFFF_0000])
                    {
                        ulong request = form | (type << 8) | number;
                        Assert.Equal((request, PermissionDenied), (request, Run(program, ioctl, architecture, 0, request)));
                    }
                }
            }

            // Section: the types beside them are allowed, as is a request of type 0.
            foreach (ulong type in (ulong[])[0x00, 0x54, 0x65, 0x67, 0x57, 0x59, 0x93, 0x95, 0xF4, 0xF6, 0x76])
            {
                ulong request = 0x8008_0000 | (type << 8) | 0x01;
                Assert.Equal((request, Allow), (request, Run(program, ioctl, architecture, 0, request)));
            }
        }

        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void A_file_opens_for_reading_only(bool x64, bool legacyOpen)
        {
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            uint architecture = x64 ? X64 : Arm64;
            const ulong directory = 0xFFFF_FF9C, highHalf = 0xFFFF_FFFF_0000_0000UL;

            // Section: x64's open takes its flags second; openat, on both, third, after the
            // directory descriptor (here AT_FDCWD).
            uint Open(ulong flags)
            {
                return legacyOpen
                    ? Run(program, 2, architecture, 0x1000, flags)
                    : Run(program, x64 ? 257u : 56u, architecture, directory, 0x1000, flags);
            }

            // Section: O_WRONLY, O_RDWR, O_CREAT and O_TRUNC are refused, alone, together with
            // others (O_CLOEXEC, O_APPEND, O_TMPFILE's directory bit), or under a high half the
            // kernel ignores, since it reads the flags as a C int.
            Assert.All((ulong[])[0x1, 0x2, 0x40, 0x200, 0x1 | 0x80000, 0x2 | 0x400, 0x41 | 0x80, 0x241, 0x410000 | 0x2, highHalf | 0x1], flags =>
                Assert.Equal((flags, PermissionDenied), (flags, Open(flags))));

            // Section: reading is allowed, with any flag that writes nothing: O_CLOEXEC,
            // O_NONBLOCK, O_NOFOLLOW, O_PATH, O_APPEND without write access, the directory bit;
            // and so is a high half alone.
            Assert.All((ulong[])[0, 0x80000, 0x800, 0x20000 | 0x80000, 0x200000 | 0x80000, 0x400, 0x10000, highHalf], flags =>
                Assert.Equal((flags, Allow), (flags, Open(flags))));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Openat2_is_not_implemented_whatever_its_flags(bool x64)
        {
            // Its flags are behind a pointer, its third argument, which the filter cannot read.
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);
            Assert.Equal(NotImplemented, Run(program, 437, x64 ? X64 : Arm64, 0xFFFF_FF9C, 0x1000, 0x2000));
            Assert.Equal(NotImplemented, Run(program, 437, x64 ? X64 : Arm64, 0, 0, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Clone_creates_threads_but_no_process(bool x64)
        {
            uint clone = x64 ? 56u : 220u;
            uint architecture = x64 ? X64 : Arm64;
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64, Self);

            // pthread_create's flags, and fork's (SIGCHLD alone).
            Assert.Equal(Allow, Run(program, clone, architecture, 0x003D0F00));
            Assert.Equal(PermissionDenied, Run(program, clone, architecture, 17));
            Assert.Equal(Allow, Run(program, clone, architecture, CloneThread));
        }

        [Fact]
        public void Another_architectures_calls_and_x32_calls_kill_the_process()
        {
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true, Self), 0, Arm64, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true, Self), 0, 0x4000_0003, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: false, Self), 0, X64, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true, Self), 0x4000_0029, X64, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_program_fits_the_kernels_limit(bool x64)
        {
            Assert.InRange(SeccompFilter.Build(x64, Self).Length, 1, 4096);
        }

        /// <summary>
        ///     Interprets a classic BPF program over one <c>seccomp_data</c>, for the few instructions
        ///     the filter uses: absolute word loads, the three conditional jumps, and constant returns.
        /// </summary>
        /// <param name="program">The program.</param>
        /// <param name="number">The system call number.</param>
        /// <param name="architecture">The audit architecture.</param>
        /// <param name="firstArgument">The call's first argument.</param>
        /// <param name="secondArgument">The call's second argument.</param>
        /// <param name="thirdArgument">The call's third argument.</param>
        /// <returns>The verdict.</returns>
        private static uint Run(SeccompFilter.Instruction[] program, uint number, uint architecture, ulong firstArgument, ulong secondArgument = 0, ulong thirdArgument = 0)
        {
            uint accumulator = 0;
            for (int pc = 0; pc < program.Length; pc++)
            {
                SeccompFilter.Instruction instruction = program[pc];
                switch (instruction.Code)
                {
                    case 0x20:
                        accumulator = instruction.Value switch
                        {
                            0 => number,
                            4 => architecture,
                            16 => (uint)firstArgument,
                            24 => (uint)secondArgument,
                            32 => (uint)thirdArgument,
                            _ => throw new InvalidOperationException($"Unexpected load offset {instruction.Value}."),
                        };
                        break;

                    case 0x15:
                        pc += accumulator == instruction.Value ? instruction.WhenTrue : instruction.WhenFalse;
                        break;

                    case 0x35:
                        pc += accumulator >= instruction.Value ? instruction.WhenTrue : instruction.WhenFalse;
                        break;

                    case 0x45:
                        pc += (accumulator & instruction.Value) != 0 ? instruction.WhenTrue : instruction.WhenFalse;
                        break;

                    case 0x54:
                        accumulator &= instruction.Value;
                        break;

                    case 0x06:
                        return instruction.Value;

                    default:
                        throw new InvalidOperationException($"Unexpected instruction {instruction.Code:x}.");
                }
            }

            throw new InvalidOperationException("The program ran off its end.");
        }
    }
}
