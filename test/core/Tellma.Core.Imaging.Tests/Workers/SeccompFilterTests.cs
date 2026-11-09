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
    ///     clone3 answers ENOSYS, clone creates threads only, and every other call is allowed.
    ///     The sandbox suite proves the installed filter's effect in a real worker.
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

        public static TheoryData<bool, uint, uint> Calls => new()
        {
            // x64: socket, socketpair, execve, fork, ptrace, io_uring_setup, unshare refused;
            // read, write, mmap, futex allowed.
            { true, 41, PermissionDenied },
            { true, 53, PermissionDenied },
            { true, 59, PermissionDenied },
            { true, 57, PermissionDenied },
            { true, 101, PermissionDenied },
            { true, 425, PermissionDenied },
            { true, 272, PermissionDenied },
            { true, 0, Allow },
            { true, 1, Allow },
            { true, 9, Allow },
            { true, 202, Allow },
            { true, 435, NotImplemented },

            // arm64: socket, execve, ptrace, io_uring_setup refused; read, write, mmap allowed.
            { false, 198, PermissionDenied },
            { false, 221, PermissionDenied },
            { false, 117, PermissionDenied },
            { false, 425, PermissionDenied },
            { false, 63, Allow },
            { false, 64, Allow },
            { false, 222, Allow },
            { false, 435, NotImplemented },
        };

        [Theory]
        [MemberData(nameof(Calls))]
        public void Each_call_gets_its_verdict(bool x64, uint number, uint verdict)
        {
            Assert.Equal(verdict, Run(SeccompFilter.Build(x64), number, x64 ? X64 : Arm64, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Clone_creates_threads_but_no_process(bool x64)
        {
            uint clone = x64 ? 56u : 220u;
            uint architecture = x64 ? X64 : Arm64;
            SeccompFilter.Instruction[] program = SeccompFilter.Build(x64);

            // pthread_create's flags, and fork's (SIGCHLD alone).
            Assert.Equal(Allow, Run(program, clone, architecture, 0x003D0F00));
            Assert.Equal(PermissionDenied, Run(program, clone, architecture, 17));
            Assert.Equal(Allow, Run(program, clone, architecture, CloneThread));
        }

        [Fact]
        public void Another_architectures_calls_and_x32_calls_kill_the_process()
        {
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true), 0, Arm64, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true), 0, 0x4000_0003, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: false), 0, X64, 0));
            Assert.Equal(Kill, Run(SeccompFilter.Build(x64: true), 0x4000_0029, X64, 0));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void The_program_fits_the_kernels_limit(bool x64)
        {
            Assert.InRange(SeccompFilter.Build(x64).Length, 1, 4096);
        }

        /// <summary>
        ///     Interprets a classic BPF program over one <c>seccomp_data</c>, for the few instructions
        ///     the filter uses: absolute word loads, the three conditional jumps, and constant returns.
        /// </summary>
        /// <param name="program">The program.</param>
        /// <param name="number">The system call number.</param>
        /// <param name="architecture">The audit architecture.</param>
        /// <param name="firstArgument">The call's first argument.</param>
        /// <returns>The verdict.</returns>
        private static uint Run(SeccompFilter.Instruction[] program, uint number, uint architecture, ulong firstArgument)
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
