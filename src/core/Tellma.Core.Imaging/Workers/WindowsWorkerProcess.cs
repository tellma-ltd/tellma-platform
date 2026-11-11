// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using static Tellma.Core.Imaging.Workers.WindowsInterop;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     A worker on Windows: created suspended on a restricted, low-integrity copy of the host's
    ///     token, put in its own Job Object, then resumed, with a handle list that lets it inherit
    ///     its three pipes and nothing else.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The worker's token holds no privilege and runs at low integrity, so the mandatory
    ///         integrity policy denies it every memory, thread, handle and settings access to the
    ///         host's process and to any other process of the host's account at medium integrity or
    ///         above, and every write to the host's files. Ending a process is not among the
    ///         accesses that policy denies, so a worker can still terminate its host; and it can open
    ///         the account's other low-integrity processes, other workers among them. Hosts therefore
    ///         render PDF thumbnails on Windows only in Development.
    ///     </para>
    ///     <para>
    ///         The job kills the worker when the host closes its handle, which the system does when
    ///         the host dies however it dies, so a worker never outlives its host. It allows one
    ///         process, so the worker cannot start another; it suppresses the crash dialog, so a
    ///         crash never waits for a person; and it denies the worker the desktop, the clipboard
    ///         and other processes' windows. Once the worker is ready, the job also caps its
    ///         committed memory and its user-mode CPU time. A crashed worker still stays alive, its
    ///         pipes open, while Windows Error Reporting writes its report, which can take longer
    ///         than a call's budget; the budget's kill ends it then.
    ///     </para>
    ///     <para>
    ///         The host's pipe ends are overlapped, and a kill cancels their pending reads and
    ///         writes, so a call ends with its worker even while another process holds one of the
    ///         worker's pipe ends open.
    ///     </para>
    ///     <para>
    ///         Windows offers an unprivileged process no way to deny another process the network,
    ///         so this worker is not kept off it; the README lists that among the residual risks.
    ///     </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal sealed unsafe class WindowsWorkerProcess : WorkerProcess
    {
        // The pipes' buffer sizes: the request and the answer stream through 64 KiB.
        private const int PipeBufferSize = 64 * 1024;
        private const int ErrorPipeBufferSize = 4 * 1024;

        // How long the host's own connection to a pipe it has just created may take.
        private const int ConnectTimeoutMilliseconds = 5_000;

        // STILL_ACTIVE: what GetExitCodeProcess reports for a running process.
        private const uint StillActive = 259;

        // Guards the limits and killing against disposal.
        private readonly Lock _sync = new();
        private readonly SafeJobHandle _job;
        private readonly SafeProcessHandle _process;
        private readonly CancellationTokenSource _killed;
        private readonly KillableStream _input;
        private readonly KillableStream _output;
        private readonly ErrorTail _error;
        private readonly int _id;
        private readonly nint _childOutput;
        private bool _disposed;

        /// <summary>Wraps a started worker.</summary>
        /// <param name="job">Its job.</param>
        /// <param name="process">Its process handle.</param>
        /// <param name="id">Its process id.</param>
        /// <param name="killed">Cancelled when it is killed; owned from here.</param>
        /// <param name="input">The host's end of its standard input.</param>
        /// <param name="output">The host's end of its standard output.</param>
        /// <param name="error">The host's end of its standard error.</param>
        /// <param name="childOutput">The handle value of its standard output, as the worker knows it.</param>
        private WindowsWorkerProcess(
            SafeJobHandle job, SafeProcessHandle process, int id, CancellationTokenSource killed, Stream input, Stream output, Stream error, nint childOutput)
        {
            _job = job;
            _process = process;
            _id = id;
            _killed = killed;
            _input = new KillableStream(input, killed.Token);
            _output = new KillableStream(output, killed.Token);
            _error = new ErrorTail(new KillableStream(error, killed.Token));
            _childOutput = childOutput;
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

        /// <summary>Starts a worker in its own job, on its own restricted token.</summary>
        /// <param name="application">The executable's full path.</param>
        /// <param name="arguments">Its arguments.</param>
        /// <param name="environment">Its whole environment.</param>
        /// <param name="workingDirectory">Its working directory.</param>
        /// <returns>The started worker.</returns>
        /// <exception cref="PdfWorkerException">The worker could not be started.</exception>
        public static WindowsWorkerProcess Start(
            string application, IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment, string workingDirectory)
        {
            NamedPipeServerStream? input = null, output = null, error = null;
            NamedPipeClientStream? childInput = null, childOutput = null, childError = null;
            SafeJobHandle? job = null;
            SafeProcessHandle? process = null;
            CancellationTokenSource? killed = null;
            nint token = 0;
            try
            {
                // Section: the job first, with the limits that hold from the worker's first
                // instruction; memory and CPU follow once the worker is ready.
                job = CreateJob();

                // Section: three pipes, the host's ends overlapped and the worker's synchronous, as
                // a console program expects. Only the worker's ends become inheritable, and only for
                // as long as it takes to create the process.
                (input, childInput) = CreatePipe(PipeDirection.Out, PipeBufferSize);
                (output, childOutput) = CreatePipe(PipeDirection.In, PipeBufferSize);
                (error, childError) = CreatePipe(PipeDirection.In, ErrorPipeBufferSize);
                nint stdin = childInput.SafePipeHandle.DangerousGetHandle();
                nint stdout = childOutput.SafePipeHandle.DangerousGetHandle();
                nint stderr = childError.SafePipeHandle.DangerousGetHandle();
                foreach (nint child in (ReadOnlySpan<nint>)[stdin, stdout, stderr])
                {
                    if (SetHandleInformation(child, HandleFlagInherit, HandleFlagInherit) == 0)
                    {
                        throw Win32Failure("A worker pipe could not be made inheritable");
                    }
                }

                // Section: the process, on its own token, suspended, so it runs nothing before it is
                // in its job.
                token = CreateWorkerToken();
                ProcessInformation created = CreateSuspendedProcess(
                    token, application, Win32Text.CommandLine(application, arguments), Win32Text.EnvironmentBlock(environment), workingDirectory, stdin, stdout, stderr);
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

                // Section: the host's ends, owned from here by the worker; the kill cancels their
                // pending reads and writes.
                killed = new CancellationTokenSource();
                var worker = new WindowsWorkerProcess(job, process, (int)created.ProcessId, killed, input, output, error, stdout);
                (job, process, killed, input, output, error) = (null, null, null, null, null, null);
                return worker;
            }
            finally
            {
                // Section: the worker holds its own copies of its ends; the host closes these at
                // once, so a dead worker's pipes break instead of staying open on the host's side.
                // Should the start fail, the host's ends close too, and closing the job kills a
                // worker that was created.
                childInput?.Dispose();
                childOutput?.Dispose();
                childError?.Dispose();
                input?.Dispose();
                output?.Dispose();
                error?.Dispose();
                killed?.Dispose();
                if (token != 0)
                {
                    _ = CloseHandle(token);
                }

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
                job.Basic.LimitFlags |= JobLimitProcessMemory | JobLimitProcessTime;
                job.ProcessMemoryLimit = checked((nuint)((long)counters.PrivateUsage + limits.Memory));
                job.Basic.PerProcessUserTimeLimit = limits.CpuTime.Ticks;
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
                    // The process first, then whatever reads and writes still wait on its pipes.
                    _ = TerminateJobObject(_job, KilledExitCode);
                    _killed.Cancel();
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

        /// <summary>
        ///     For the suite: a copy, in the host, of the worker's standard output write end, as a
        ///     child some other code started at the wrong moment would hold it.
        /// </summary>
        /// <returns>The copy, which the caller closes.</returns>
        internal SafeFileHandle DuplicateChildOutput()
        {
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return DuplicateHandle(_process, _childOutput, CurrentProcess, out nint copy, 0, 0, DuplicateSameAccess) == 0
                    ? throw Win32Failure("The worker's output could not be duplicated")
                    : new SafeFileHandle(copy, ownsHandle: true);
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
                _killed.Cancel();
                _disposed = true;
            }

            _input.Dispose();
            _output.Dispose();
            _job.Dispose();
            _process.Dispose();
            _killed.Dispose();
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

        /// <summary>
        ///     Creates a pipe for one of the worker's standard streams: a local, single-instance named
        ///     pipe that only the host's account may open, which the host connects to at once, so that
        ///     no other process can take the worker's end. Neither end is inheritable.
        /// </summary>
        /// <param name="direction">The direction of the host's end.</param>
        /// <param name="bufferSize">The pipe's buffer size.</param>
        /// <returns>The host's end, overlapped, and the worker's end, synchronous.</returns>
        private static (NamedPipeServerStream Host, NamedPipeClientStream Child) CreatePipe(PipeDirection direction, int bufferSize)
        {
            string name = $"tellma-pdf-worker-{Guid.NewGuid():N}";
            NamedPipeServerStream? host = null;
            NamedPipeClientStream? child = null;
            try
            {
                host = new NamedPipeServerStream(
                    name,
                    direction,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly | PipeOptions.FirstPipeInstance,
                    bufferSize,
                    bufferSize);
                child = new NamedPipeClientStream(".", name, direction == PipeDirection.Out ? PipeDirection.In : PipeDirection.Out, PipeOptions.None);
                child.Connect(ConnectTimeoutMilliseconds);
                host.WaitForConnection();
                (NamedPipeServerStream, NamedPipeClientStream) pipe = (host, child);
                (host, child) = (null, null);
                return pipe;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or TimeoutException)
            {
                throw new PdfWorkerException(PdfWorkerFailure.Start, $"A worker pipe could not be created: {exception.Message}", exception);
            }
            finally
            {
                // On failure, whichever end exists closes.
                child?.Dispose();
                host?.Dispose();
            }
        }

        /// <summary>
        ///     The worker's token: a copy of the host's with every privilege removed and its integrity
        ///     lowered to low. A restricted copy of the caller's own token is one the caller may
        ///     assign to a new process without any privilege.
        /// </summary>
        /// <returns>The token, which the caller closes.</returns>
        private static nint CreateWorkerToken()
        {
            nint own = 0, restricted = 0;
            try
            {
                // Section: the host's own token, with the access its restricted copy inherits: to
                // be assigned to a process, and to have its integrity level set.
                if (OpenProcessToken(CurrentProcess, TokenDuplicate | TokenQuery | TokenAssignPrimary | TokenAdjustDefault, out own) == 0)
                {
                    throw Win32Failure("The host's token could not be opened");
                }

                // Section: every privilege removed, but the one every process holds to be told of
                // file changes.
                if (CreateRestrictedToken(own, DisableMaxPrivilege, 0, null, 0, null, 0, null, out restricted) == 0)
                {
                    throw Win32Failure("The worker's restricted token could not be created");
                }

                // Section: low integrity, below the host's medium or above.
                byte* sid = stackalloc byte[SecurityMaxSidSize];
                uint sidSize = SecurityMaxSidSize;
                if (CreateWellKnownSid(WinLowLabelSid, 0, sid, &sidSize) == 0)
                {
                    throw Win32Failure("The low integrity level could not be named");
                }

                var label = new TokenMandatoryLabel { Label = new SidAndAttributes { Sid = sid, Attributes = SeGroupIntegrity } };
                if (SetTokenInformation(restricted, TokenIntegrityLevel, &label, (uint)sizeof(TokenMandatoryLabel) + GetLengthSid(sid)) == 0)
                {
                    throw Win32Failure("The worker's token could not be lowered to low integrity");
                }

                (nint token, restricted) = (restricted, 0);
                return token;
            }
            finally
            {
                foreach (nint handle in (ReadOnlySpan<nint>)[own, restricted])
                {
                    if (handle != 0)
                    {
                        _ = CloseHandle(handle);
                    }
                }
            }
        }

        /// <summary>
        ///     Creates the process suspended, on the given token, inheriting exactly the three given
        ///     handles, as its standard input, output and error.
        /// </summary>
        /// <param name="token">The worker's token.</param>
        /// <param name="application">The executable.</param>
        /// <param name="commandLine">The command line.</param>
        /// <param name="environment">The environment block.</param>
        /// <param name="workingDirectory">The working directory.</param>
        /// <param name="input">The worker's standard input.</param>
        /// <param name="output">The worker's standard output.</param>
        /// <param name="error">The worker's standard error.</param>
        /// <returns>The new process and its suspended thread.</returns>
        private static ProcessInformation CreateSuspendedProcess(
            nint token, string application, string commandLine, string environment, string workingDirectory, nint input, nint output, nint error)
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
                        if (CreateProcessAsUser(token, application, commandPointer, 0, 0, 1, flags, environmentPointer, workingDirectory, &startup, &created) == 0)
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
