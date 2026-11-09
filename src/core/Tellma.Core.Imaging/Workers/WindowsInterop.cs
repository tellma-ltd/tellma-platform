// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     The Win32 calls that start a worker inside a Job Object with exactly its three pipes
    ///     inherited (processthreadsapi.h, jobapi2.h, winbase.h, psapi.h).
    /// </summary>
    [SupportedOSPlatform("windows")]
    internal static unsafe partial class WindowsInterop
    {
        /// <summary><c>HANDLE_FLAG_INHERIT</c>.</summary>
        public const uint HandleFlagInherit = 0x1;

        /// <summary><c>STARTF_USESTDHANDLES</c>.</summary>
        public const uint StartFUseStdHandles = 0x100;

        /// <summary><c>CREATE_SUSPENDED</c>: the worker runs no instruction before it is in its job.</summary>
        public const uint CreateSuspended = 0x0000_0004;

        /// <summary><c>CREATE_UNICODE_ENVIRONMENT</c>.</summary>
        public const uint CreateUnicodeEnvironment = 0x0000_0400;

        /// <summary><c>EXTENDED_STARTUPINFO_PRESENT</c>.</summary>
        public const uint ExtendedStartupInfoPresent = 0x0008_0000;

        /// <summary><c>CREATE_DEFAULT_ERROR_MODE</c>: the worker does not inherit the host's error mode.</summary>
        public const uint CreateDefaultErrorMode = 0x0400_0000;

        /// <summary><c>CREATE_NO_WINDOW</c>.</summary>
        public const uint CreateNoWindow = 0x0800_0000;

        /// <summary><c>PROC_THREAD_ATTRIBUTE_HANDLE_LIST</c>: the only handles the child inherits.</summary>
        public const nuint ProcThreadAttributeHandleList = 0x0002_0002;

        /// <summary><c>JobObjectBasicUIRestrictions</c>.</summary>
        public const int JobObjectBasicUiRestrictions = 4;

        /// <summary><c>JobObjectExtendedLimitInformation</c>.</summary>
        public const int JobObjectExtendedLimitInformation = 9;

        /// <summary><c>JOB_OBJECT_LIMIT_PROCESS_TIME</c>: user-mode CPU time per process.</summary>
        public const uint JobLimitProcessTime = 0x0000_0002;

        /// <summary><c>JOB_OBJECT_LIMIT_ACTIVE_PROCESS</c>.</summary>
        public const uint JobLimitActiveProcess = 0x0000_0008;

        /// <summary><c>JOB_OBJECT_LIMIT_PROCESS_MEMORY</c>: committed memory per process.</summary>
        public const uint JobLimitProcessMemory = 0x0000_0100;

        /// <summary><c>JOB_OBJECT_LIMIT_DIE_ON_UNHANDLED_EXCEPTION</c>: no error dialog, no hang on a crash.</summary>
        public const uint JobLimitDieOnUnhandledException = 0x0000_0400;

        /// <summary><c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c>: the worker dies with the host's last handle to the job.</summary>
        public const uint JobLimitKillOnJobClose = 0x0000_2000;

        /// <summary>All of <c>JOB_OBJECT_UILIMIT_*</c>: no desktop, clipboard, atoms, display or system settings, user handles or exit.</summary>
        public const uint JobUiLimitAll = 0x0000_00FF;

        /// <summary><c>WAIT_OBJECT_0</c>.</summary>
        public const uint WaitObject0 = 0;

        /// <summary><c>ERROR_INSUFFICIENT_BUFFER</c>.</summary>
        public const int ErrorInsufficientBuffer = 122;

        /// <summary><c>BOOL CreatePipe(PHANDLE, PHANDLE, LPSECURITY_ATTRIBUTES, DWORD)</c>.</summary>
        /// <param name="readPipe">The read end.</param>
        /// <param name="writePipe">The write end.</param>
        /// <param name="attributes">Null: neither end is inheritable.</param>
        /// <param name="size">The suggested buffer size.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int CreatePipe(out nint readPipe, out nint writePipe, nint attributes, uint size);

        /// <summary><c>BOOL SetHandleInformation(HANDLE, DWORD, DWORD)</c>.</summary>
        /// <param name="handle">The handle.</param>
        /// <param name="mask">The flags to change.</param>
        /// <param name="flags">Their new values.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int SetHandleInformation(nint handle, uint mask, uint flags);

        /// <summary><c>BOOL CloseHandle(HANDLE)</c>.</summary>
        /// <param name="handle">The handle.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int CloseHandle(nint handle);

        /// <summary><c>BOOL InitializeProcThreadAttributeList(LPPROC_THREAD_ATTRIBUTE_LIST, DWORD, DWORD, PSIZE_T)</c>.</summary>
        /// <param name="list">The list, or null to ask for its size.</param>
        /// <param name="count">The number of attributes.</param>
        /// <param name="flags">Reserved; 0.</param>
        /// <param name="size">The list's size in bytes.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int InitializeProcThreadAttributeList(void* list, uint count, uint flags, nuint* size);

        /// <summary><c>BOOL UpdateProcThreadAttribute(LPPROC_THREAD_ATTRIBUTE_LIST, DWORD, DWORD_PTR, PVOID, SIZE_T, PVOID, PSIZE_T)</c>.</summary>
        /// <param name="list">The list.</param>
        /// <param name="flags">Reserved; 0.</param>
        /// <param name="attribute">The attribute.</param>
        /// <param name="value">Its value, which must outlive the list.</param>
        /// <param name="size">The value's size.</param>
        /// <param name="previousValue">Reserved; null.</param>
        /// <param name="returnSize">Reserved; null.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int UpdateProcThreadAttribute(
            void* list, uint flags, nuint attribute, void* value, nuint size, void* previousValue, nuint* returnSize);

        /// <summary><c>void DeleteProcThreadAttributeList(LPPROC_THREAD_ATTRIBUTE_LIST)</c>.</summary>
        /// <param name="list">The list.</param>
        [LibraryImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial void DeleteProcThreadAttributeList(void* list);

        /// <summary><c>BOOL CreateProcessW(...)</c>.</summary>
        /// <param name="applicationName">The executable's full path.</param>
        /// <param name="commandLine">The command line, in a writable buffer.</param>
        /// <param name="processAttributes">Null: the process handle is not inheritable.</param>
        /// <param name="threadAttributes">Null: the thread handle is not inheritable.</param>
        /// <param name="inheritHandles">Nonzero, so the handle list applies.</param>
        /// <param name="creationFlags">The creation flags.</param>
        /// <param name="environment">The environment block.</param>
        /// <param name="currentDirectory">The working directory.</param>
        /// <param name="startupInfo">The extended startup information.</param>
        /// <param name="processInformation">The new process and thread.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int CreateProcess(
            string applicationName,
            char* commandLine,
            nint processAttributes,
            nint threadAttributes,
            int inheritHandles,
            uint creationFlags,
            char* environment,
            string currentDirectory,
            StartupInfoEx* startupInfo,
            ProcessInformation* processInformation);

        /// <summary><c>DWORD ResumeThread(HANDLE)</c>.</summary>
        /// <param name="thread">The suspended thread.</param>
        /// <returns>The previous suspend count, or <c>(DWORD)-1</c>.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial uint ResumeThread(nint thread);

        /// <summary><c>HANDLE CreateJobObjectW(LPSECURITY_ATTRIBUTES, LPCWSTR)</c>, unnamed and not inheritable.</summary>
        /// <param name="attributes">Null.</param>
        /// <param name="name">Null.</param>
        /// <returns>The job, or null.</returns>
        [LibraryImport("kernel32.dll", EntryPoint = "CreateJobObjectW", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial nint CreateJobObject(nint attributes, nint name);

        /// <summary><c>BOOL SetInformationJobObject(HANDLE, JOBOBJECTINFOCLASS, LPVOID, DWORD)</c>.</summary>
        /// <param name="job">The job.</param>
        /// <param name="informationClass">What is set.</param>
        /// <param name="information">The information.</param>
        /// <param name="length">Its size.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int SetInformationJobObject(SafeJobHandle job, int informationClass, void* information, uint length);

        /// <summary><c>BOOL QueryInformationJobObject(HANDLE, JOBOBJECTINFOCLASS, LPVOID, DWORD, LPDWORD)</c>.</summary>
        /// <param name="job">The job.</param>
        /// <param name="informationClass">What is read.</param>
        /// <param name="information">Receives the information.</param>
        /// <param name="length">Its size.</param>
        /// <param name="returnLength">Receives the size written, or null.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int QueryInformationJobObject(SafeJobHandle job, int informationClass, void* information, uint length, uint* returnLength);

        /// <summary><c>BOOL AssignProcessToJobObject(HANDLE, HANDLE)</c>.</summary>
        /// <param name="job">The job.</param>
        /// <param name="process">The process.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int AssignProcessToJobObject(SafeJobHandle job, SafeProcessHandle process);

        /// <summary><c>BOOL TerminateJobObject(HANDLE, UINT)</c>.</summary>
        /// <param name="job">The job.</param>
        /// <param name="exitCode">The exit code its processes report.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int TerminateJobObject(SafeJobHandle job, uint exitCode);

        /// <summary><c>BOOL TerminateProcess(HANDLE, UINT)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="exitCode">The exit code it reports.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int TerminateProcess(SafeProcessHandle process, uint exitCode);

        /// <summary><c>BOOL GetExitCodeProcess(HANDLE, LPDWORD)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="exitCode">Its exit code, or <c>STILL_ACTIVE</c>.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int GetExitCodeProcess(SafeProcessHandle process, out uint exitCode);

        /// <summary><c>DWORD WaitForSingleObject(HANDLE, DWORD)</c>.</summary>
        /// <param name="handle">The process.</param>
        /// <param name="milliseconds">How long to wait.</param>
        /// <returns><c>WAIT_OBJECT_0</c> once it has exited.</returns>
        [LibraryImport("kernel32.dll", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial uint WaitForSingleObject(SafeProcessHandle handle, uint milliseconds);

        /// <summary><c>BOOL K32GetProcessMemoryInfo(HANDLE, PPROCESS_MEMORY_COUNTERS, DWORD)</c>.</summary>
        /// <param name="process">The process.</param>
        /// <param name="counters">Receives the counters.</param>
        /// <param name="size">Their size.</param>
        /// <returns>Nonzero on success.</returns>
        [LibraryImport("kernel32.dll", EntryPoint = "K32GetProcessMemoryInfo", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        public static partial int GetProcessMemoryInfo(SafeProcessHandle process, ProcessMemoryCounters* counters, uint size);

        /// <summary><c>STARTUPINFOW</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfo
        {
            /// <summary>The size of the extended structure.</summary>
            public uint Size;

            /// <summary>Reserved.</summary>
            public nint Reserved;

            /// <summary>The desktop; null for the parent's.</summary>
            public nint Desktop;

            /// <summary>The console title; unused.</summary>
            public nint Title;

            /// <summary>Window position and size, console buffer size and fill: unused.</summary>
            public uint X;

            /// <summary>Unused.</summary>
            public uint Y;

            /// <summary>Unused.</summary>
            public uint XSize;

            /// <summary>Unused.</summary>
            public uint YSize;

            /// <summary>Unused.</summary>
            public uint XCountChars;

            /// <summary>Unused.</summary>
            public uint YCountChars;

            /// <summary>Unused.</summary>
            public uint FillAttribute;

            /// <summary><c>STARTF_USESTDHANDLES</c>.</summary>
            public uint Flags;

            /// <summary>Unused.</summary>
            public ushort ShowWindow;

            /// <summary>Reserved.</summary>
            public ushort Reserved2Size;

            /// <summary>Reserved.</summary>
            public nint Reserved2;

            /// <summary>The worker's standard input.</summary>
            public nint StdInput;

            /// <summary>The worker's standard output.</summary>
            public nint StdOutput;

            /// <summary>The worker's standard error.</summary>
            public nint StdError;
        }

        /// <summary><c>STARTUPINFOEXW</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct StartupInfoEx
        {
            /// <summary>The basic startup information.</summary>
            public StartupInfo StartupInfo;

            /// <summary>The attribute list.</summary>
            public void* AttributeList;
        }

        /// <summary><c>PROCESS_INFORMATION</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessInformation
        {
            /// <summary>The process.</summary>
            public nint Process;

            /// <summary>Its primary thread.</summary>
            public nint Thread;

            /// <summary>The process id.</summary>
            public uint ProcessId;

            /// <summary>The thread id.</summary>
            public uint ThreadId;
        }

        /// <summary><c>JOBOBJECT_BASIC_LIMIT_INFORMATION</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JobBasicLimits
        {
            /// <summary>User-mode CPU time per process, in 100-nanosecond units.</summary>
            public long PerProcessUserTimeLimit;

            /// <summary>Unused.</summary>
            public long PerJobUserTimeLimit;

            /// <summary>Which limits apply.</summary>
            public uint LimitFlags;

            /// <summary>Unused.</summary>
            public nuint MinimumWorkingSetSize;

            /// <summary>Unused.</summary>
            public nuint MaximumWorkingSetSize;

            /// <summary>The most processes the job may hold.</summary>
            public uint ActiveProcessLimit;

            /// <summary>Unused.</summary>
            public nuint Affinity;

            /// <summary>Unused.</summary>
            public uint PriorityClass;

            /// <summary>Unused.</summary>
            public uint SchedulingClass;
        }

        /// <summary><c>IO_COUNTERS</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct IoCounters
        {
            /// <summary>Unused.</summary>
            public ulong ReadOperationCount;

            /// <summary>Unused.</summary>
            public ulong WriteOperationCount;

            /// <summary>Unused.</summary>
            public ulong OtherOperationCount;

            /// <summary>Unused.</summary>
            public ulong ReadTransferCount;

            /// <summary>Unused.</summary>
            public ulong WriteTransferCount;

            /// <summary>Unused.</summary>
            public ulong OtherTransferCount;
        }

        /// <summary><c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JobExtendedLimits
        {
            /// <summary>The basic limits.</summary>
            public JobBasicLimits Basic;

            /// <summary>Unused.</summary>
            public IoCounters Io;

            /// <summary>Committed memory per process, in bytes.</summary>
            public nuint ProcessMemoryLimit;

            /// <summary>Unused.</summary>
            public nuint JobMemoryLimit;

            /// <summary>The most memory any process of the job has committed.</summary>
            public nuint PeakProcessMemoryUsed;

            /// <summary>Unused.</summary>
            public nuint PeakJobMemoryUsed;
        }

        /// <summary><c>JOBOBJECT_BASIC_UI_RESTRICTIONS</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct JobUiRestrictions
        {
            /// <summary>The restrictions.</summary>
            public uint RestrictionsClass;
        }

        /// <summary><c>PROCESS_MEMORY_COUNTERS_EX</c>.</summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct ProcessMemoryCounters
        {
            /// <summary>The structure's size.</summary>
            public uint Size;

            /// <summary>Unused.</summary>
            public uint PageFaultCount;

            /// <summary>Unused.</summary>
            public nuint PeakWorkingSetSize;

            /// <summary>Unused.</summary>
            public nuint WorkingSetSize;

            /// <summary>Unused.</summary>
            public nuint QuotaPeakPagedPoolUsage;

            /// <summary>Unused.</summary>
            public nuint QuotaPagedPoolUsage;

            /// <summary>Unused.</summary>
            public nuint QuotaPeakNonPagedPoolUsage;

            /// <summary>Unused.</summary>
            public nuint QuotaNonPagedPoolUsage;

            /// <summary>Unused.</summary>
            public nuint PagefileUsage;

            /// <summary>Unused.</summary>
            public nuint PeakPagefileUsage;

            /// <summary>The process's committed private memory: what the job's memory limit counts.</summary>
            public nuint PrivateUsage;
        }
    }

    /// <summary>A Job Object handle, closed when released, which kills the job's processes.</summary>
    [SupportedOSPlatform("windows")]
    internal sealed class SafeJobHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        /// <summary>Creates an invalid handle, for the marshaller.</summary>
        public SafeJobHandle()
            : base(ownsHandle: true)
        {
        }

        /// <summary>Takes ownership of a job handle.</summary>
        /// <param name="handle">The handle.</param>
        public SafeJobHandle(nint handle)
            : base(ownsHandle: true)
        {
            SetHandle(handle);
        }

        /// <inheritdoc />
        protected override bool ReleaseHandle()
        {
            return WindowsInterop.CloseHandle(handle) != 0;
        }
    }
}
