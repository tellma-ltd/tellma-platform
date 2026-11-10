// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.TestWorker
{
    /// <summary>The operating-system calls the probes make, each as a compromised worker would make it.</summary>
    internal static unsafe partial class Native
    {
        /// <summary><c>EPERM</c>.</summary>
        public const int PermissionDenied = 1;

        /// <summary><c>PROCESS_VM_OPERATION</c>.</summary>
        public const uint ProcessVmOperation = 0x0008;

        /// <summary><c>PROCESS_VM_READ</c>.</summary>
        public const uint ProcessVmRead = 0x0010;

        /// <summary><c>PROCESS_VM_WRITE</c>.</summary>
        public const uint ProcessVmWrite = 0x0020;

        /// <summary><c>PROCESS_DUP_HANDLE</c>.</summary>
        public const uint ProcessDuplicateHandle = 0x0040;

        /// <summary><c>PROCESS_CREATE_THREAD</c>.</summary>
        public const uint ProcessCreateThread = 0x0002;

        /// <summary><c>PROCESS_SET_INFORMATION</c>.</summary>
        public const uint ProcessSetInformation = 0x0200;

        /// <summary><c>PROCESS_TERMINATE</c>.</summary>
        public const uint ProcessTerminate = 0x0001;

        /// <summary><c>int kill(pid_t, int)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="signal">The signal; 0 only checks permission.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "kill", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int Kill(int process, int signal);

        /// <summary><c>int prlimit(pid_t, int, const struct rlimit*, struct rlimit*)</c>.</summary>
        /// <param name="process">The process; 0 for this one.</param>
        /// <param name="resource">The resource.</param>
        /// <param name="limit">The new limit, or null.</param>
        /// <param name="old">Receives the old limit, or null.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "prlimit", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int Prlimit(int process, int resource, ulong* limit, ulong* old);

        /// <summary><c>int shmget(key_t, size_t, int)</c>.</summary>
        /// <param name="key">The key; 0 for a new private segment.</param>
        /// <param name="size">Its size.</param>
        /// <param name="flags">The flags.</param>
        /// <returns>Its id, or -1.</returns>
        [LibraryImport("libc", EntryPoint = "shmget", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SharedMemoryGet(int key, nuint size, int flags);

        /// <summary><c>int shmctl(int, int, struct shmid_ds*)</c>.</summary>
        /// <param name="id">The segment.</param>
        /// <param name="command">The command.</param>
        /// <param name="buffer">Null.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "shmctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SharedMemoryControl(int id, int command, nint buffer);

        /// <summary><c>int msgget(key_t, int)</c>.</summary>
        /// <param name="key">The key.</param>
        /// <param name="flags">The flags.</param>
        /// <returns>Its id, or -1.</returns>
        [LibraryImport("libc", EntryPoint = "msgget", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int MessageQueueGet(int key, int flags);

        /// <summary><c>int msgctl(int, int, struct msqid_ds*)</c>.</summary>
        /// <param name="id">The queue.</param>
        /// <param name="command">The command.</param>
        /// <param name="buffer">Null.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "msgctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int MessageQueueControl(int id, int command, nint buffer);

        /// <summary><c>int semget(key_t, int, int)</c>.</summary>
        /// <param name="key">The key.</param>
        /// <param name="count">The number of semaphores.</param>
        /// <param name="flags">The flags.</param>
        /// <returns>Its id, or -1.</returns>
        [LibraryImport("libc", EntryPoint = "semget", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SemaphoreGet(int key, int count, int flags);

        /// <summary><c>int semctl(int, int, int, ...)</c>, with no fourth argument.</summary>
        /// <param name="id">The set.</param>
        /// <param name="number">The semaphore.</param>
        /// <param name="command">The command.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "semctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SemaphoreControl(int id, int number, int command);

        /// <summary><c>int getpriority(int, id_t)</c>.</summary>
        /// <param name="which">What <paramref name="who" /> names.</param>
        /// <param name="who">The process.</param>
        /// <returns>Its nice value.</returns>
        [LibraryImport("libc", EntryPoint = "getpriority", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int GetPriority(int which, uint who);

        /// <summary><c>int setpriority(int, id_t, int)</c>.</summary>
        /// <param name="which">What <paramref name="who" /> names.</param>
        /// <param name="who">The process.</param>
        /// <param name="priority">The nice value.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "setpriority", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SetPriority(int which, uint who, int priority);

        /// <summary><c>int sched_getaffinity(pid_t, size_t, cpu_set_t*)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="size">The mask's size.</param>
        /// <param name="mask">Receives the mask.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "sched_getaffinity", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int GetAffinity(int process, nuint size, byte* mask);

        /// <summary><c>int sched_setaffinity(pid_t, size_t, const cpu_set_t*)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="size">The mask's size.</param>
        /// <param name="mask">The mask.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "sched_setaffinity", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int SetAffinity(int process, nuint size, byte* mask);

        /// <summary><c>int prctl(int, unsigned long, unsigned long, unsigned long, unsigned long)</c>.</summary>
        /// <param name="option">The operation.</param>
        /// <param name="argument2">Its first argument.</param>
        /// <param name="argument3">Its second argument.</param>
        /// <param name="argument4">Its third argument.</param>
        /// <param name="argument5">Its fourth argument.</param>
        /// <returns>0 on success.</returns>
        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial int Prctl(int option, ulong argument2, ulong argument3, ulong argument4, ulong argument5);

        /// <summary><c>long syscall(long, ...)</c>, for the calls the C library may not wrap.</summary>
        /// <param name="number">The system call.</param>
        /// <param name="argument1">Its first argument.</param>
        /// <param name="argument2">Its second argument.</param>
        /// <param name="argument3">Its third argument.</param>
        /// <returns>The call's result; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "syscall", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial long SystemCall(long number, long argument1, long argument2, long argument3);

        /// <summary><c>void* mmap(void*, size_t, int, int, int, off_t)</c>.</summary>
        /// <param name="address">Null.</param>
        /// <param name="length">The length.</param>
        /// <param name="protection">The protection.</param>
        /// <param name="flags">The flags.</param>
        /// <param name="descriptor">-1.</param>
        /// <param name="offset">0.</param>
        /// <returns>The mapping, or <c>MAP_FAILED</c> (-1).</returns>
        [LibraryImport("libc", EntryPoint = "mmap", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [UnsupportedOSPlatform("windows")]
        public static partial nint MapMemory(nint address, nuint length, int protection, int flags, int descriptor, long offset);

        /// <summary><c>HANDLE OpenProcess(DWORD, BOOL, DWORD)</c>.</summary>
        /// <param name="access">The access wanted.</param>
        /// <param name="inherit">0.</param>
        /// <param name="processId">The process.</param>
        /// <returns>The handle, or null.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows")]
        public static partial nint OpenProcess(uint access, int inherit, uint processId);

        /// <summary><c>BOOL CloseHandle(HANDLE)</c>.</summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows")]
        public static partial int CloseHandle(nint handle);
    }
}
