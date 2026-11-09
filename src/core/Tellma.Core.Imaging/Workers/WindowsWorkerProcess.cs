// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Tellma.Core.Imaging.Workers.WindowsInterop;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     A worker on Windows: created suspended, put in its own Job Object, then resumed, with a
    ///     handle list that lets it inherit its three pipes and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The job kills the worker when the host closes its handle, which the system does when
    ///         the host dies however it dies, so a worker never outlives its host. It allows one
    ///         process, so the worker cannot start another; it suppresses the crash dialog, so a
    ///         crash ends the process at once; and it denies the worker the desktop, the clipboard
    ///         and other processes' windows. Once the worker is ready, the job also caps its
    ///         committed memory and its user-mode CPU time.
    ///     </para>
    ///     <para>
    ///         Windows offers an unprivileged process no way to deny another process the network,
    ///         so this worker is not kept off it; the README lists that among the residual risks.
    ///     </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal sealed unsafe class WindowsWorkerProcess : WorkerProcess
    {
        // The pipes' suggested buffer sizes: the request and the answer stream through 64 KiB.
        private const uint PipeBufferSize = 64 * 1024;
        private const uint ErrorPipeBufferSize = 4 * 1024;

        // STILL_ACTIVE: what GetExitCodeProcess reports for a running process.
        private const uint StillActive = 259;

        // Guards the limits and killing against disposal.
        private readonly Lock _sync = new();
        private readonly SafeJobHandle _job;
        private readonly SafeProcessHandle _process;
        private readonly FileStream _input;
        private readonly FileStream _output;
        private readonly ErrorTail _error;
        private readonly int _id;
        private bool _disposed;

        /// <summary>Wraps a started worker.</summary>
        /// <param name="job">Its job.</param>
        /// <param name="process">Its process handle.</param>
        /// <param name="id">Its process id.</param>
        /// <param name="input">The write end of its standard input.</param>
        /// <param name="output">The read end of its standard output.</param>
        /// <param name="error">The read end of its standard error.</param>
        private WindowsWorkerProcess(SafeJobHandle job, SafeProcessHandle process, int id, FileStream input, FileStream output, FileStream error)
        {
            _job = job;
            _process = process;
            _id = id;
            _input = input;
            _output = output;
            _error = new ErrorTail(error);
        }

        /// <inheritdoc />
        public override int Id => _id;

        /// <inheritdoc />
        public override Stream Input => _input;

        /// <inheritdoc />
        public override Stream Output => _output;

        /// <inheritdoc />
        public override int? ExitCode
        {
            get
            {
                lock (_sync)
                {
                    return !_disposed && GetExitCodeProcess(_process, out uint code) != 0 && code != StillActive ? (int)code : null;
                }
            }
        }

        /// <inheritdoc />
        public override long? PeakMemory
        {
            get
            {
                lock (_sync)
                {
                    if (_disposed)
                    {
                        return null;
                    }

                    JobExtendedLimits limits = default;
                    return QueryInformationJobObject(_job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobExtendedLimits), null) != 0
                        ? (long)limits.PeakProcessMemoryUsed
                        : null;
                }
            }
        }

        /// <inheritdoc />
        protected override ErrorTail Error => _error;

        /// <summary>Starts a worker in its own job.</summary>
        /// <param name="application">The executable's full path.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <param name="environment">Its whole environment.</param>
        /// <param name="workingDirectory">Its working directory.</param>
        /// <returns>The started worker.</returns>
        /// <exception cref="PdfWorkerException">The worker could not be started.</exception>
        public static WindowsWorkerProcess Start(
            string application, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, string workingDirectory)
        {
            nint childInput = 0, parentInput = 0, parentOutput = 0, childOutput = 0, parentError = 0, childError = 0;
            SafeJobHandle? job = null;
            SafeProcessHandle? process = null;
            try
            {
                // Section: the job first, with the limits that hold from the worker's first
                // instruction; memory and CPU follow once the worker is ready.
                job = CreateJob();

                // Section: three pipes. Only the worker's ends become inheritable, and only for as
                // long as it takes to create the process.
                CreatePipeOrThrow(out childInput, out parentInput, PipeBufferSize);
                CreatePipeOrThrow(out parentOutput, out childOutput, PipeBufferSize);
                CreatePipeOrThrow(out parentError, out childError, ErrorPipeBufferSize);
                foreach (nint child in (ReadOnlySpan<nint>)[childInput, childOutput, childError])
                {
                    if (SetHandleInformation(child, HandleFlagInherit, HandleFlagInherit) == 0)
                    {
                        throw Win32Failure("A worker pipe could not be made inheritable");
                    }
                }

                // Section: the process, suspended, so it runs nothing before it is in its job.
                ProcessInformation created = CreateSuspendedProcess(
                    application, Win32Text.CommandLine(application, arguments), Win32Text.EnvironmentBlock(environment), workingDirectory, childInput, childOutput, childError);
                process = new SafeProcessHandle(created.Process, ownsHandle: true);
                try
                {
                    if (AssignProcessToJobObject(job, process) == 0)
                    {
                        throw Win32Failure("The worker could not be put in its Job Object");
                    }

                    if (ResumeThread(created.Thread) == uint.MaxValue)
                    {
                        throw Win32Failure("The worker's thread could not be resumed");
                    }
                }
                catch
                {
                    // A suspended process, perhaps outside the job, would live on: end it first.
                    _ = TerminateProcess(process, KilledExitCode);
                    throw;
                }
                finally
                {
                    _ = CloseHandle(created.Thread);
                }

                // Section: the host's ends, owned from here by safe handles, then wrapped as
                // unbuffered streams over the synchronous pipes; should a wrap fail, the handles
                // close and the job's disposal below kills the worker.
                var inputHandle = new SafeFileHandle(parentInput, ownsHandle: true);
                var outputHandle = new SafeFileHandle(parentOutput, ownsHandle: true);
                var errorHandle = new SafeFileHandle(parentError, ownsHandle: true);
                (parentInput, parentOutput, parentError) = (0, 0, 0);
                try
                {
                    var worker = new WindowsWorkerProcess(
                        job,
                        process,
                        (int)created.ProcessId,
                        new FileStream(inputHandle, FileAccess.Write, bufferSize: 0, isAsync: false),
                        new FileStream(outputHandle, FileAccess.Read, bufferSize: 0, isAsync: false),
                        new FileStream(errorHandle, FileAccess.Read, bufferSize: 0, isAsync: false));
                    (job, process) = (null, null);
                    return worker;
                }
                catch
                {
                    inputHandle.Dispose();
                    outputHandle.Dispose();
                    errorHandle.Dispose();
                    throw;
                }
            }
            finally
            {
                // Section: the worker holds its own copies of its ends; the host closes these at
                // once, so a dead worker's pipes break instead of staying open on the host's side.
                foreach (nint handle in (ReadOnlySpan<nint>)[childInput, childOutput, childError, parentInput, parentOutput, parentError])
                {
                    if (handle != 0)
                    {
                        _ = CloseHandle(handle);
                    }
                }

                // Closing the job of a worker that failed to start kills it.
                job?.Dispose();
                process?.Dispose();
            }
        }

        /// <inheritdoc />
        public override void ApplyLimits(WorkerLimits limits)
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);

                // Section: the memory cap counts from what the worker has committed now, its runtime
                // started and nothing of the upload read.
                ProcessMemoryCounters counters = default;
                counters.Size = (uint)sizeof(ProcessMemoryCounters);
                if (GetProcessMemoryInfo(_process, &counters, counters.Size) == 0)
                {
                    throw Win32Failure("The worker's memory could not be measured");
                }

                JobExtendedLimits job = BaseLimits();
                job.Basic.LimitFlags |= JobLimitProcessMemory;
                job.ProcessMemoryLimit = checked((nuint)((long)counters.PrivateUsage + limits.Memory));
                if (limits.CpuTime is TimeSpan cpu)
                {
                    job.Basic.LimitFlags |= JobLimitProcessTime;
                    job.Basic.PerProcessUserTimeLimit = cpu.Ticks;
                }

                if (SetInformationJobObject(_job, JobObjectExtendedLimitInformation, &job, (uint)sizeof(JobExtendedLimits)) == 0)
                {
                    throw Win32Failure("The worker's memory and CPU limits could not be applied");
                }
            }
        }

        /// <inheritdoc />
        public override void Kill()
        {
            lock (_sync)
            {
                if (!_disposed)
                {
                    _ = TerminateJobObject(_job, KilledExitCode);
                }
            }
        }

        /// <inheritdoc />
        public override bool WaitForExit(TimeSpan timeout)
        {
            uint milliseconds = (uint)Math.Clamp((long)timeout.TotalMilliseconds, 0, uint.MaxValue - 1);
            try
            {
                // The marshaller references the handle for the call, so disposal meanwhile cannot
                // close it under the wait.
                return WaitForSingleObject(_process, milliseconds) == WaitObject0;
            }
            catch (ObjectDisposedException)
            {
                // Disposal kills the worker first.
                return true;
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                // Closing the job kills the worker if it still runs.
                _ = TerminateJobObject(_job, KilledExitCode);
                _disposed = true;
            }

            _input.Dispose();
            _output.Dispose();
            _job.Dispose();
            _process.Dispose();
        }

        /// <summary>Creates the worker's job, with the limits that hold from its first instruction.</summary>
        /// <returns>The job.</returns>
        private static SafeJobHandle CreateJob()
        {
            nint handle = CreateJobObject(0, 0);
            if (handle == 0)
            {
                throw Win32Failure("The worker's Job Object could not be created");
            }

            var job = new SafeJobHandle(handle);
            try
            {
                JobExtendedLimits limits = BaseLimits();
                var restrictions = new JobUiRestrictions { RestrictionsClass = JobUiLimitAll };
                return SetInformationJobObject(job, JobObjectExtendedLimitInformation, &limits, (uint)sizeof(JobExtendedLimits)) == 0
                    || SetInformationJobObject(job, JobObjectBasicUiRestrictions, &restrictions, (uint)sizeof(JobUiRestrictions)) == 0
                    ? throw Win32Failure("The worker's Job Object could not be configured")
                    : job;
            }
            catch
            {
                job.Dispose();
                throw;
            }
        }

        /// <summary>The limits every worker's job carries: one process, killed with the job, no crash dialog.</summary>
        /// <returns>The limits.</returns>
        private static JobExtendedLimits BaseLimits()
        {
            JobExtendedLimits limits = default;
            limits.Basic.LimitFlags = JobLimitKillOnJobClose | JobLimitDieOnUnhandledException | JobLimitActiveProcess;
            limits.Basic.ActiveProcessLimit = 1;
            return limits;
        }

        /// <summary>Creates an anonymous pipe neither of whose ends is inheritable.</summary>
        /// <param name="read">The read end.</param>
        /// <param name="write">The write end.</param>
        /// <param name="size">The suggested buffer size.</param>
        private static void CreatePipeOrThrow(out nint read, out nint write, uint size)
        {
            if (CreatePipe(out read, out write, 0, size) == 0)
            {
                throw Win32Failure("A worker pipe could not be created");
            }
        }

        /// <summary>
        ///     Creates the process suspended, inheriting exactly the three given handles, as its
        ///     standard input, output and error.
        /// </summary>
        /// <param name="application">The executable.</param>
        /// <param name="commandLine">The command line.</param>
        /// <param name="environment">The environment block.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <param name="input">The worker's standard input.</param>
        /// <param name="output">The worker's standard output.</param>
        /// <param name="error">The worker's standard error.</param>
        /// <returns>The new process and its suspended thread.</returns>
        private static ProcessInformation CreateSuspendedProcess(
            string application, string commandLine, string environment, string workingDirectory, nint input, nint output, nint error)
        {
            // Section: the attribute list holding the handle list; its value must outlive the list.
            nuint size = 0;
            if (InitializeProcThreadAttributeList(null, 1, 0, &size) == 0 && Marshal.GetLastPInvokeError() != ErrorInsufficientBuffer)
            {
                throw Win32Failure("The worker's attribute list could not be sized");
            }

            void* list = NativeMemory.Alloc(size);
            nint* inherited = stackalloc nint[3] { input, output, error };
            try
            {
                if (InitializeProcThreadAttributeList(list, 1, 0, &size) == 0)
                {
                    throw Win32Failure("The worker's attribute list could not be initialised");
                }

                try
                {
                    if (UpdateProcThreadAttribute(list, 0, ProcThreadAttributeHandleList, inherited, (nuint)(3 * sizeof(nint)), null, null) == 0)
                    {
                        throw Win32Failure("The worker's handle list could not be set");
                    }

                    // Section: the process. The command line buffer must be writable.
                    var startup = default(StartupInfoEx);
                    startup.StartupInfo.Size = (uint)sizeof(StartupInfoEx);
                    startup.StartupInfo.Flags = StartFUseStdHandles;
                    startup.StartupInfo.StdInput = input;
                    startup.StartupInfo.StdOutput = output;
                    startup.StartupInfo.StdError = error;
                    startup.AttributeList = list;

                    char[] command = [.. commandLine, '\0'];
                    ProcessInformation created = default;
                    fixed (char* commandPointer = command)
                    fixed (char* environmentPointer = environment)
                    {
                        const uint flags = CreateSuspended | CreateUnicodeEnvironment | ExtendedStartupInfoPresent | CreateDefaultErrorMode | CreateNoWindow;
                        if (CreateProcess(application, commandPointer, 0, 0, 1, flags, environmentPointer, workingDirectory, &startup, &created) == 0)
                        {
                            throw Win32Failure($"The worker could not be started ({application})");
                        }
                    }

                    return created;
                }
                finally
                {
                    DeleteProcThreadAttributeList(list);
                }
            }
            finally
            {
                NativeMemory.Free(list);
            }
        }

        /// <summary>A start failure carrying the last Win32 error.</summary>
        /// <param name="what">What failed.</param>
        /// <returns>The exception.</returns>
        private static PdfWorkerException Win32Failure(string what)
        {
            var cause = new Win32Exception(Marshal.GetLastPInvokeError());
            return new PdfWorkerException(PdfWorkerFailure.Start, $"{what}: {cause.Message}", cause);
        }
    }
}
