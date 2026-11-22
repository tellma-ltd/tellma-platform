// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.TestWorker
{
    /// <summary>
    ///     A PDF worker that misbehaves on purpose, one way per mode, inside the real worker's
    ///     sandbox: it ends as a crash ends a process, hangs (heeding its input or not), spins,
    ///     allocates without end, breaks the protocol or answers with a malformed image, or probes
    ///     what its sandbox, its filesystem confinement and its limits allow, the host's process
    ///     and files, another process's files under <c>/proc</c>, the network, the host's debugger
    ///     pipes, its own session and terminal and a thread older than the sandbox among them, and
    ///     reports it. One mode renders as the real worker does and reports what the render mapped.
    /// </summary>
    /// <remarks>
    ///     A probe reports through a refusal's detail, which the host reads as an ordinary answer;
    ///     the misbehaviours give the host nothing it should accept.
    /// </remarks>
    internal static class Program
    {
        // STATUS_ACCESS_VIOLATION, the exit code of a process that an access violation ended.
        private const int AccessViolation = unchecked((int)0xC000_0005);

        /// <summary>Runs the mode named by the first argument the sandbox leaves.</summary>
        /// <param name="args">The command line.</param>
        /// <returns>The exit code.</returns>
        private static int Main(string[] args)
        {
            // Section: what probes need from before the sandbox: the host's process id, which the
            // sandbox consumes, and a thread that exists before the system-call filter does.
            int host = WorkerSandbox.TakeHostProcess([.. args]) ?? 0;
            using EarlyThread? early = args.Contains("early-thread") ? new EarlyThread() : null;
            string? beforeFilter = OperatingSystem.IsLinux() ? FilesBeforeFilter(args) : null;

            // Section: the sandbox, exactly as the real worker enters it.
            List<string> remaining = [.. args];
            WorkerCapabilities capabilities = WorkerSandbox.Enter(remaining, out Stream output);
            string mode = remaining.Count > 0 ? remaining[0] : "missing";
            Stream input = Console.OpenStandardInput();

            // Section: misbehaviours before the worker is ready.
            switch (mode)
            {
                case "exit-before-ready":
                    return 3;

                case "crash-before-ready":
                    Crash("The test worker crashes before it is ready.");
                    return 1;

                case "hang-before-ready":
                    Thread.Sleep(Timeout.Infinite);
                    return 1;

                case "bad-ready":
                    output.Write("NOTREADY"u8);
                    output.Flush();
                    Thread.Sleep(Timeout.Infinite);
                    return 1;

                case "real":
                    // The real worker, run from this assembly.
                    return WorkerSession.Serve(capabilities, input, output, PdfiumRasterizer.Instance);

                default:
                    break;
            }

            // Section: the ready frame and the request, as a worker reads them; one mode leaves its
            // filesystem confinement out of the ready frame, whether or not it is confined.
            WorkerProtocol.WriteReady(output, mode == "ready-unconfined" ? capabilities & ~WorkerCapabilities.FilesystemConfinement : capabilities);
            WorkerRequest request = WorkerProtocol.ReadRequest(input);
            int size = request.Size;

            // As the real worker does: from here, input ending means the host is gone. A worker
            // that ignores its input relies on the operating system alone to end it with its host.
            if (mode != "hang-ignoring-input")
            {
                WorkerSandbox.ExitWhenInputEnds(input);
            }

            // Section: misbehaviours after the request.
            switch (mode)
            {
                case "crash-after-request":
                    Crash("The test worker crashes while rendering.");
                    return 1;

                case "fail-fast-after-request":
                    // A real crash on every operating system, the system's error reporting included.
                    Environment.FailFast("The test worker fails fast while rendering.");
                    return 1;

                case "exit-after-request":
                    return 4;

                case "hang-after-request":
                case "hang-ignoring-input":
                    Thread.Sleep(Timeout.Infinite);
                    return 1;

                case "spin":
                    Spin();
                    return 1;

                case "unknown-status":
                    WriteHeader(output, 7, 0);
                    return 0;

                case "oversized":
                    WriteHeader(output, (int)WorkerStatus.Rendered, int.MaxValue);
                    return 0;

                case "truncated":
                    WriteHeader(output, (int)WorkerStatus.Rendered, 1000);
                    output.Write(new byte[10]);
                    output.Flush();
                    return 0;

                case "stray-bytes":
                    output.Write("stray native output"u8);
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Png(size, size, extraChunk: false));
                    return 0;

                case "not-png":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Encoding.ASCII.GetBytes("<html>not an image</html>"));
                    return 0;

                case "wrong-size":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Png(size - 1, size - 1, extraChunk: false));
                    return 0;

                case "extra-chunk":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Png(size, size, extraChunk: true));
                    return 0;

                case "corrupt-png":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Corrupt(Png(size, size, extraChunk: false)));
                    return 0;

                case "good":
                case "ready-unconfined":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Png(size, size, extraChunk: false));
                    return 0;

                case "failed":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Failed, "OutOfMemoryException: the test worker ran out\nof memory"u8);
                    return 0;

                default:
                    // Section: probes, each reported as a refusal's detail.
                    string? argument = remaining.Count > 1 ? remaining[1] : null, second = remaining.Count > 2 ? remaining[2] : null;
                    WorkerProtocol.WriteResult(output, WorkerStatus.Refused, Encoding.UTF8.GetBytes(Probe(mode, argument, second, host, early, beforeFilter, request)));
                    return 0;
            }
        }

        /// <summary>
        ///     Ends this process as a crash ends one: abruptly, with no answer and an abnormal exit
        ///     code.
        /// </summary>
        /// <remarks>
        ///     On Linux that is a real crash: the runtime aborts, and the process is gone at once. On
        ///     Windows a real crash goes to Windows Error Reporting first, which keeps the dying
        ///     process, its pipes still open, until its report is written: seconds on a quiet machine
        ///     and half a minute on a loaded one, which no test of what follows a crash can bound.
        ///     There the stand-in ends itself with the exit code of an access violation, which is
        ///     all a host sees of a crash once that report is written.
        /// </remarks>
        /// <param name="message">What the crash says of itself, where it says anything.</param>
        private static void Crash(string message)
        {
            if (OperatingSystem.IsWindows())
            {
                WorkerSandbox.ExitNow(AccessViolation);
            }

            Environment.FailFast(message);
        }

        /// <summary>Runs one probe of the sandbox and the limits.</summary>
        /// <param name="mode">The probe.</param>
        /// <param name="argument">The probe's own argument, when the command line carries one.</param>
        /// <param name="second">The probe's second argument, when the command line carries one.</param>
        /// <param name="host">The host's process id.</param>
        /// <param name="early">The thread started before the sandbox, for the probe that uses it.</param>
        /// <param name="beforeFilter">What the files probe found before the system-call filter, for the probe that reports it.</param>
        /// <param name="request">The host's request, for the probe that renders it.</param>
        /// <returns>What it found, as <c>name: outcome</c>.</returns>
        private static string Probe(string mode, string? argument, string? second, int host, EarlyThread? early, string? beforeFilter, WorkerRequest request)
        {
            try
            {
                return mode switch
                {
                    "allocate-native" => $"allocated: {AllocateNative()}",
                    "allocate-managed" => $"allocated: {AllocateManaged()}",
                    "map-shared" => $"allocated: {MapShared()}",
                    "socket" => $"socket: {OpenSocket()}",
                    "process" => $"process: {StartProcess()}",
                    "environment" => $"environment: {string.Join(',', Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal))}",
                    "write-file" => $"write-file: {WriteFile(argument)}",
                    "footprint" => $"footprint: {Footprint(request)}",
                    "host-access" => $"host-access: {HostAccess(host)}",
                    "capabilities" => $"capabilities: {Capabilities()}",
                    "open-host" => $"open-host: {OpenProcess(host)}",
                    "open-process" => $"open-process: {OpenProcess(int.Parse(argument ?? "0", CultureInfo.InvariantCulture))}",
                    "proc-files" => $"proc-files: {ProcessFiles(argument)}",
                    "proc-reads" => $"proc-reads: {ProcessReads(argument)}",
                    "files" => $"files: {Files(argument, second)}",
                    "files-before-filter" => $"files-before-filter: {beforeFilter ?? "not run"}",
                    "connect" => $"connect: {Connect(argument)}",
                    "descriptors" => $"descriptors: {Descriptors(argument)}",
                    "host-debugger-pipes" => $"host-debugger-pipes: {HostDebuggerPipes(host, argument)}",
                    "session" => $"session: {Session()}",
                    "early-thread" => $"early-thread: {early!.Run(() => $"socket {OpenSocket()}, process {StartProcess()}, read {ReadFile(argument)}")}",
                    _ => $"unknown mode: {mode}",
                };
            }
#pragma warning disable CA1031 // A probe reports whatever stopped it.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return $"{mode}: {exception.GetType().Name}";
            }
        }

        /// <summary>Allocates and touches native memory in 16 MiB blocks until refused.</summary>
        /// <returns>How many MiB it got.</returns>
        private static long AllocateNative()
        {
            const int block = 16 * 1024 * 1024;
            var blocks = new List<IntPtr>();
            try
            {
                while (blocks.Count < 1024)
                {
                    IntPtr memory = Marshal.AllocHGlobal(block);
                    blocks.Add(memory);
                    unsafe
                    {
                        new Span<byte>((void*)memory, block).Fill(1);
                    }
                }
            }
            catch (OutOfMemoryException)
            {
                // The limit held.
            }

            return blocks.Count * 16L;
        }

        /// <summary>Allocates and touches managed arrays in 16 MiB blocks until refused.</summary>
        /// <returns>How many MiB it got.</returns>
        private static long AllocateManaged()
        {
            var blocks = new List<byte[]>();
            try
            {
                while (blocks.Count < 1024)
                {
                    byte[] memory = new byte[16 * 1024 * 1024];
                    Array.Fill(memory, (byte)1);
                    blocks.Add(memory);
                }
            }
            catch (OutOfMemoryException)
            {
                // The limit held.
            }

            return blocks.Count * 16L;
        }

        /// <summary>
        ///     Maps and touches shared anonymous memory in 16 MiB blocks until refused, up to 768 MiB:
        ///     memory the data limit does not count, so only the address-space limit stops it.
        /// </summary>
        /// <returns>How many MiB it got.</returns>
        private static unsafe long MapShared()
        {
            if (!OperatingSystem.IsLinux())
            {
                return -1;
            }

            const int block = 16 * 1024 * 1024;
            const int readWrite = 0x3;
            const int sharedAnonymous = 0x01 | 0x20;
            int blocks = 0;
            while (blocks < 48)
            {
                nint memory = Native.MapMemory(0, block, readWrite, sharedAnonymous, -1, 0);
                if (memory == -1)
                {
                    break;
                }

                new Span<byte>((void*)memory, block).Fill(1);
                blocks++;
            }

            return blocks * 16L;
        }

        /// <summary>
        ///     Tries, on Linux, every way the filter closes of acting on the host's process or
        ///     escaping the memory limits, then the same calls on the worker itself, which must work.
        /// </summary>
        /// <param name="host">The host's process id.</param>
        /// <returns>Each call as <c>name=EPERM</c>, <c>name=allowed</c> or <c>name=errnoN</c>.</returns>
        private static unsafe string HostAccess(int host)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            // Section: x64 and arm64 numbers of the calls the C library may not wrap.
            bool x64 = RuntimeInformation.ProcessArchitecture == Architecture.X64;
            long tgkill = x64 ? 234 : 131, tkill = x64 ? 200 : 130, pipe2 = x64 ? 293 : 59;
            const long pidfdOpen = 434;
            const int processPriority = 0, openFiles = 7;
            ulong* limit = stackalloc ulong[2];
            byte* mask = stackalloc byte[128];
            int self = Environment.ProcessId;

            // Section: a pipe of the worker's own, whose owner the worker tries to name: the kernel
            // signals a descriptor's owner on I/O. The owner as fcntl's F_SETOWN_EX takes it is a
            // struct f_owner_ex: F_OWNER_PID (1), then the process.
            const int closeOnExec = 0x80000, setOwner = 8, setSignal = 10, setOwnerExtended = 15, getFlags = 3, setFlags = 4, ioSignal = 29;
            int* pipe = stackalloc int[2];
            pipe[0] = pipe[1] = -1;
            _ = Native.SystemCall(pipe2, (long)pipe, closeOnExec, 0);
            int* owner = stackalloc int[2];
            owner[0] = 1;
            owner[1] = host;

            // Section: the ioctl requests that push input into a terminal (TIOCSTI, TIOCLINUX) or
            // name a socket's owner (FIOSETOWN, SIOCSPGRP), tried on the same pipe. A pipe knows
            // none of them, so a kernel that is asked answers ENOTTY and nothing happens; the filter
            // refuses each before the kernel looks at the descriptor. FIONREAD is one a pipe knows.
            const long pushInput = 0x5412, consoleInput = 0x541C, socketOwner = 0x8901, socketGroup = 0x8902, bytesReadable = 0x541B;
            const int nonBlocking = 0x800;
            int* word = stackalloc int[1];
            word[0] = 0;
            int flags = (int)DescriptorControl(pipe[0], getFlags, 0);

            // Section: the host's process, through every call that could signal it, change its
            // limits or scheduling, or open a handle to it; and SysV objects, which outlive the
            // process and escape its limits. Each, should it work, is undone.
            List<string> outcomes =
            [
                Outcome("kill", Native.Kill(host, 0)),
                Outcome("kill-group", Native.Kill(0, 0)),
                Outcome("tgkill", Native.SystemCall(tgkill, host, host, 0)),
                Outcome("tkill", Native.SystemCall(tkill, host, 0, 0)),
                Outcome("pidfd_open", Native.SystemCall(pidfdOpen, host, 0, 0)),
                Outcome("prlimit", Native.Prlimit(host, openFiles, null, limit)),
                Outcome("setpriority", Native.SetPriority(processPriority, (uint)host, Native.GetPriority(processPriority, (uint)host))),
                Outcome("sched_setaffinity", Native.GetAffinity(host, 128, mask) == 0 ? Native.SetAffinity(host, 128, mask) : -1),
                Outcome("prctl-pdeathsig", Native.Prctl(1, 0, 0, 0, 0)),
                Outcome("fcntl-setown", DescriptorOwner(pipe[0], setOwner, host)),
                Outcome("fcntl-setown-ex", DescriptorOwner(pipe[0], setOwnerExtended, (long)owner)),
                Outcome("fcntl-setsig", DescriptorOwner(pipe[0], setSignal, ioSignal)),
                Outcome("fcntl-setfl-async", SignalDriven(pipe[0], byRequest: false)),
                Outcome("ioctl-fioasync", SignalDriven(pipe[0], byRequest: true)),
                Outcome("ioctl-tiocsti", DeviceControl(pipe[0], pushInput, (long)word)),
                Outcome("ioctl-tioclinux", DeviceControl(pipe[0], consoleInput, (long)word)),
                Outcome("ioctl-fiosetown", DeviceControl(pipe[0], socketOwner, (long)word)),
                Outcome("ioctl-siocspgrp", DeviceControl(pipe[0], socketGroup, (long)word)),
                Outcome("shmget", SystemV('m')),
                Outcome("msgget", SystemV('q')),
                Outcome("semget", SystemV('s')),

                // Section: the same calls on the worker itself.
                Outcome("kill-self", Native.Kill(self, 0)),
                Outcome("prlimit-self", Native.Prlimit(0, openFiles, null, limit)),
                Outcome("prlimit-own", Native.Prlimit(self, openFiles, null, limit)),
                Outcome("fcntl-getfl", DescriptorControl(pipe[0], getFlags, 0)),
                Outcome("fcntl-setfl", DescriptorControl(pipe[0], setFlags, flags | nonBlocking)),
                Outcome("ioctl-fionread", DeviceControl(pipe[0], bytesReadable, (long)word)),
            ];
            _ = Native.Close(pipe[0]);
            _ = Native.Close(pipe[1]);
            return string.Join(',', outcomes);
        }

        /// <summary><c>fcntl(fd, cmd, arg)</c>, as a system call.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="command">The command.</param>
        /// <param name="argument">Its argument.</param>
        /// <returns>The call's result; -1 with <c>errno</c> set on failure.</returns>
        [UnsupportedOSPlatform("windows")]
        private static long DescriptorControl(int descriptor, int command, long argument)
        {
            return Native.SystemCall(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 72 : 25, descriptor, command, argument);
        }

        /// <summary><c>ioctl(fd, request, arg)</c>, as a system call.</summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="request">The request.</param>
        /// <param name="argument">Its argument.</param>
        /// <returns>The call's result; -1 with <c>errno</c> set on failure.</returns>
        [UnsupportedOSPlatform("windows")]
        private static long DeviceControl(int descriptor, long request, long argument)
        {
            return Native.SystemCall(RuntimeInformation.ProcessArchitecture == Architecture.X64 ? 16 : 29, descriptor, request, argument);
        }

        /// <summary>
        ///     Tries to turn signal-driven I/O on for a descriptor, with <c>fcntl</c>'s
        ///     <c>F_SETFL</c> and <c>O_ASYNC</c> or with <c>ioctl</c>'s <c>FIOASYNC</c>, and turns it
        ///     off again should that work, keeping the attempt's result and <c>errno</c>. The
        ///     descriptor has no owner and sees no I/O meanwhile, so no signal is sent either way.
        /// </summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="byRequest">True for <c>ioctl</c>, false for <c>fcntl</c>.</param>
        /// <returns>The attempt's result.</returns>
        [UnsupportedOSPlatform("windows")]
        private static unsafe long SignalDriven(int descriptor, bool byRequest)
        {
            const int getFlags = 3, setFlags = 4, signalDriven = 0x2000;
            const long signalDrivenRequest = 0x5452;
            int on = 1, off = 0;
            int flags = (int)DescriptorControl(descriptor, getFlags, 0);
            long result = byRequest
                ? DeviceControl(descriptor, signalDrivenRequest, (long)&on)
                : DescriptorControl(descriptor, setFlags, flags | signalDriven);
            int error = Marshal.GetLastPInvokeError();
            if (result >= 0)
            {
                _ = byRequest
                    ? DeviceControl(descriptor, signalDrivenRequest, (long)&off)
                    : DescriptorControl(descriptor, setFlags, flags);
            }

            Marshal.SetLastPInvokeError(error);
            return result;
        }

        /// <summary>
        ///     Tries one <c>fcntl</c> command that names a descriptor's owner or the owner's signal,
        ///     and withdraws both should it work, keeping the command's result and <c>errno</c>. The
        ///     descriptor never asks for signals, so none is sent either way.
        /// </summary>
        /// <param name="descriptor">The descriptor.</param>
        /// <param name="command">The command.</param>
        /// <param name="argument">Its argument.</param>
        /// <returns>The command's result.</returns>
        [UnsupportedOSPlatform("windows")]
        private static long DescriptorOwner(int descriptor, int command, long argument)
        {
            const int setOwner = 8, setSignal = 10;
            long result = DescriptorControl(descriptor, command, argument);
            int error = Marshal.GetLastPInvokeError();
            if (result >= 0)
            {
                _ = DescriptorControl(descriptor, setOwner, 0);
                _ = DescriptorControl(descriptor, setSignal, 0);
            }

            Marshal.SetLastPInvokeError(error);
            return result;
        }

        /// <summary>A system call's outcome, from its result and <c>errno</c>.</summary>
        /// <param name="name">The call.</param>
        /// <param name="result">Its result.</param>
        /// <returns><c>name=allowed</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        private static string Outcome(string name, long result)
        {
            int error = Marshal.GetLastPInvokeError();
            return result >= 0 ? $"{name}=allowed" : error == Native.PermissionDenied ? $"{name}=EPERM" : $"{name}=errno{error}";
        }

        /// <summary>
        ///     Tries to create a private SysV object, and removes it should that work, keeping the
        ///     creation's result and <c>errno</c>.
        /// </summary>
        /// <param name="kind"><c>m</c> for shared memory, <c>q</c> for a message queue, <c>s</c> for a semaphore set.</param>
        /// <returns>The object's id, or -1.</returns>
        [UnsupportedOSPlatform("windows")]
        private static int SystemV(char kind)
        {
            const int createPrivate = 0x200 | 0x180, removeId = 0;
            int id = kind switch
            {
                'm' => Native.SharedMemoryGet(0, 1024 * 1024, createPrivate),
                'q' => Native.MessageQueueGet(0, createPrivate),
                _ => Native.SemaphoreGet(0, 1, createPrivate),
            };
            int error = Marshal.GetLastPInvokeError();
            if (id >= 0)
            {
                _ = kind switch
                {
                    'm' => Native.SharedMemoryControl(id, removeId, 0),
                    'q' => Native.MessageQueueControl(id, removeId, 0),
                    _ => Native.SemaphoreControl(id, 0, removeId),
                };
            }

            Marshal.SetLastPInvokeError(error);
            return id;
        }

        /// <summary>The worker's capability sets, as the kernel reports them, the bounding set aside.</summary>
        /// <returns><c>CapInh=..,CapPrm=..,CapEff=..,CapAmb=..</c>.</returns>
        private static string Capabilities()
        {
            return !OperatingSystem.IsLinux()
                ? "not linux"
                : string.Join(',', File.ReadAllLines("/proc/self/status")
                    .Where(static line => line.StartsWith("Cap", StringComparison.Ordinal) && !line.StartsWith("CapBnd", StringComparison.Ordinal))
                    .Select(static line => string.Join('=', line.Split(':', StringSplitOptions.TrimEntries))));
        }

        /// <summary>
        ///     Tries to open a process, the host's or another: on Windows, for each right that would
        ///     let the worker read or write its memory, run code in it, take its handles, change or
        ///     end it, or merely ask after it; on Linux, through its memory file.
        /// </summary>
        /// <param name="process">The process's id.</param>
        /// <returns><c>refused</c>, or <c>opened (rights)</c>.</returns>
        private static string OpenProcess(int process)
        {
            if (OperatingSystem.IsWindows())
            {
                (string Name, uint Right)[] rights =
                [
                    ("vm-read", Native.ProcessVmRead),
                    ("vm-write", Native.ProcessVmWrite),
                    ("vm-operation", Native.ProcessVmOperation),
                    ("create-thread", Native.ProcessCreateThread),
                    ("dup-handle", Native.ProcessDuplicateHandle),
                    ("set-information", Native.ProcessSetInformation),
                    ("terminate", Native.ProcessTerminate),
                    ("query-limited", Native.ProcessQueryLimitedInformation),
                ];
                List<string> opened = [];
                foreach ((string name, uint right) in rights)
                {
                    nint handle = Native.OpenProcess(right, 0, (uint)process);
                    if (handle != 0)
                    {
                        opened.Add(name);
                        _ = Native.CloseHandle(handle);
                    }
                }

                return opened.Count == 0 ? "refused" : $"opened ({string.Join(',', opened)})";
            }

            try
            {
                using Microsoft.Win32.SafeHandles.SafeFileHandle memory = File.OpenHandle($"/proc/{process.ToString(CultureInfo.InvariantCulture)}/mem");
                return "opened (mem)";
            }
            catch (UnauthorizedAccessException)
            {
                return "refused";
            }
        }

        /// <summary>
        ///     Tries, on Linux, to open another process through <c>/proc</c>: each named descriptor
        ///     of its table, its environment, its memory and its memory map, each for reading, which
        ///     the kernel itself decides on (the filter refuses an open for writing before the kernel
        ///     looks). Whatever opens is closed again, untouched.
        /// </summary>
        /// <param name="argument">The process and the descriptors, as <c>pid:n,n</c>.</param>
        /// <returns>Each as <c>name=opened</c>, <c>name=EACCES</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        private static string ProcessFiles(string? argument)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            const int readOnly = 0;
            string[] parts = (argument ?? string.Empty).Split(':');
            string process = parts[0];
            string[] descriptors = parts.Length > 1 ? parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries) : [];
            List<string> outcomes = [];
            foreach (string descriptor in descriptors)
            {
                outcomes.Add(Opened($"fd{descriptor}", $"/proc/{process}/fd/{descriptor}", readOnly));
            }

            outcomes.Add(Opened("environ", $"/proc/{process}/environ", readOnly));
            outcomes.Add(Opened("mem", $"/proc/{process}/mem", readOnly));
            outcomes.Add(Opened("maps", $"/proc/{process}/maps", readOnly));
            return string.Join(',', outcomes);
        }

        /// <summary>
        ///     Tries, on Linux, to read what <c>/proc</c> tells of another process, which its owner's
        ///     other processes may ordinarily read, and of the worker itself, which it must still
        ///     read; and to list <c>/proc</c>, which names every process.
        /// </summary>
        /// <param name="argument">The other process's id.</param>
        /// <returns>Each as <c>name=opened</c>, <c>name=EACCES</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        private static string ProcessReads(string? argument)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            const int readOnly = 0;
            string process = argument ?? "1";
            List<string> outcomes = [];
            foreach (string file in (ReadOnlySpan<string>)["status", "stat", "cmdline", "comm", "environ", "maps", "cgroup"])
            {
                outcomes.Add(Opened(file, $"/proc/{process}/{file}", readOnly));
            }

            outcomes.Add(Opened("task", $"/proc/{process}/task", readOnly));
            outcomes.Add(Opened("list-proc", "/proc", readOnly));
            outcomes.Add(Opened("self-status", "/proc/self/status", readOnly));
            outcomes.Add(Opened("self-maps", "/proc/self/maps", readOnly));
            outcomes.Add(Opened("thread-self-status", "/proc/thread-self/status", readOnly));
            return string.Join(',', outcomes);
        }

        /// <summary>
        ///     Tries every way of reading, changing or adding to the host's files: reading a file of
        ///     the host's temporary directory, a file beside the worker's own assembly that is not one
        ///     the worker runs on, and a system file; listing them; writing the host's file in place,
        ///     by an open for writing or through a shared mapping, truncating it, renaming it, linking
        ///     to it and deleting it; making files, directories, links and pipes beside it, removing a
        ///     directory there, and changing the file's permissions and times; on Linux also watching
        ///     the file, reading its extended attributes by name, and asking after or changing a
        ///     readable file's attributes through a descriptor opened for reading. Whatever an
        ///     attempt opens is closed again at once, and nothing is written through it.
        /// </summary>
        /// <param name="directory">A directory of the host's, holding <c>victim.txt</c> and an empty directory <c>empty</c>.</param>
        /// <param name="beside">A file beside the worker's own assembly.</param>
        /// <returns>Each attempt as <c>name=done</c> or its refusal.</returns>
        private static string Files(string? directory, string? beside)
        {
            ArgumentNullException.ThrowIfNull(directory);
            ArgumentNullException.ThrowIfNull(beside);
            string victim = Path.Combine(directory, "victim.txt");
            string own = typeof(Program).Assembly.Location;
            return OperatingSystem.IsWindows()
                ? string.Join(',', WindowsFiles(directory, victim, beside, own))
                : string.Join(',', UnixFiles(directory, victim, beside, own));
        }

        /// <summary>
        ///     On Linux, runs the files probe where the worker is confined by Landlock alone: once
        ///     it has started over confined, before it enters the sandbox and installs its
        ///     system-call filter. It runs only once the confinement is seen to hold, so an
        ///     unconfined worker changes nothing.
        /// </summary>
        /// <param name="args">The command line, before the sandbox takes its arguments.</param>
        /// <returns>What it found; <c>not confined</c> when it did not run; null when not asked.</returns>
        [UnsupportedOSPlatform("windows")]
        private static string? FilesBeforeFilter(string[] args)
        {
            const string probe = "files-before-filter";
            const int closeOnExec = 0x80000;
            int at = Array.IndexOf(args, probe);
            if (at < 0 || at + 2 >= args.Length)
            {
                return null;
            }

            // Section: only the second start carries --confined, and a confined worker cannot read
            // the root directory.
            int root = args.Contains(WorkerConfinement.ConfinedArgument) ? Native.Open("/", closeOnExec) : 0;
            if (root >= 0)
            {
                _ = root > 0 ? Native.Close(root) : 0;
                return "not confined";
            }

            return Files(args[at + 1], args[at + 2]);
        }

        /// <summary>The attempts of <see cref="Files" />, by the C library's calls, each as the kernel answers it.</summary>
        /// <param name="directory">The host's directory.</param>
        /// <param name="victim">The host's file in it.</param>
        /// <param name="beside">A file beside the worker's own assembly.</param>
        /// <param name="own">The worker's own assembly, which it may read.</param>
        /// <returns>Each attempt as <c>name=done</c>, <c>name=EACCES</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        [UnsupportedOSPlatform("windows")]
        private static List<string> UnixFiles(string directory, string victim, string beside, string own)
        {
            const int readOnly = 0, writeOnly = 1, readWrite = 2, create = 0x40, exclusive = 0x80, truncate = 0x200, append = 0x400, closeOnExec = 0x80000;
            const uint ownerOnly = 0x180, reportFileIdentity = 0x200; // FAN_CLASS_NOTIF | FAN_REPORT_FID
            string worker = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);

            // Section: reads, of files and of directories, outside the allowance.
            List<string> outcomes =
            [
                Called("read-victim", Closed(Native.Open(victim, readOnly | closeOnExec))),
                Called("read-beside", Closed(Native.Open(beside, readOnly | closeOnExec))),
                Called("read-system", Closed(Native.Open("/etc/passwd", readOnly | closeOnExec))),
                Called("list-root", Closed(Native.Open("/", readOnly | closeOnExec))),
                Called("list-folder", Closed(Native.Open(directory, readOnly | closeOnExec))),

                // Section: the host's file changed in place: opened for writing, appending or
                // truncation, truncated by name.
                Called("write-victim", Closed(Native.Open(victim, writeOnly | closeOnExec))),
                Called("read-write-victim", Closed(Native.Open(victim, readWrite | closeOnExec))),
                Called("append-victim", Closed(Native.Open(victim, writeOnly | append | closeOnExec))),
                Called("truncate-open", Closed(Native.Open(victim, readOnly | truncate | closeOnExec))),
                Called("truncate", Native.Truncate(victim, 0)),

                // Section: the host's file renamed, linked to or deleted, and new entries beside it,
                // in the worker's own temporary directory and in shared memory.
                Called("rename", Native.Rename(victim, Path.Combine(directory, "renamed.txt"))),
                Called("link", Native.Link(victim, Path.Combine(directory, "linked.txt"))),
                Called("symlink", Native.SymbolicLink(victim, Path.Combine(directory, "symlinked.txt"))),
                Called("delete", Native.Unlink(victim)),
                Called("create", Closed(Native.OpenCreate(Path.Combine(directory, "created.txt"), writeOnly | create | exclusive | closeOnExec, ownerOnly))),
                Called("create-temp", Removed(Native.OpenCreate($"/tmp/tellma-worker-{worker}.txt", writeOnly | create | exclusive | closeOnExec, ownerOnly), $"/tmp/tellma-worker-{worker}.txt")),
                Called("mkdir", Native.MakeDirectory(Path.Combine(directory, "made"), 0x1C0)),
                Called("rmdir", Native.RemoveDirectory(Path.Combine(directory, "empty"))),
                Called("mkfifo", Native.MakeFifo(Path.Combine(directory, "fifo"), ownerOnly)),

                // Section: the file's metadata.
                Called("chmod", Native.ChangeMode(victim, 0x1B6)),
                Called("set-times", Native.SetTimes(victim, 0)),

                // Section: the file watched, by inotify and by fanotify, and its extended attributes
                // read and listed by name.
                Called("watch", Watched(victim)),
                Called("notify", Closed(Native.FileNotifyInit(reportFileIdentity, readOnly | closeOnExec))),
                Called("get-xattr", Native.GetAttribute(victim, "user.tellma", 0, 0)),
                Called("list-xattr", Native.ListAttributes(victim, 0, 0)),

                // Section: a file the worker may read, its own assembly, mapped shared for writing,
                // directly or by changing a read-only shared mapping's protection, and its
                // attributes asked after or changed through a descriptor opened for reading.
                MappedForWriting(own),
                AttributeControls(own),
            ];
            if (Directory.Exists("/dev/shm"))
            {
                outcomes.Add(Called("create-shm", Removed(Native.OpenCreate($"/dev/shm/tellma-worker-{worker}", readWrite | create | exclusive | closeOnExec, ownerOnly), $"/dev/shm/tellma-worker-{worker}")));
            }

            return outcomes;
        }

        /// <summary>
        ///     Maps a file the worker may read, shared and writable, which would write the file
        ///     through memory: first at once, then by mapping it read-only and changing the
        ///     mapping's protection. Nothing is written through either; whatever maps is unmapped.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <returns><c>map-shared-write=...,mprotect-shared-write=...</c>.</returns>
        [UnsupportedOSPlatform("windows")]
        private static string MappedForWriting(string path)
        {
            const int readOnly = 0, closeOnExec = 0x80000, read = 0x1, write = 0x2, shared = 0x01;
            const nuint length = 4096;
            int descriptor = Native.Open(path, readOnly | closeOnExec);
            if (descriptor < 0)
            {
                return Called("map-own-open", descriptor);
            }

            try
            {
                nint writable = Native.MapMemory(0, length, read | write, shared, descriptor, 0);
                string direct = Called("map-shared-write", writable == -1 ? -1 : 0);
                if (writable != -1)
                {
                    _ = Native.UnmapMemory(writable, length);
                }

                nint readable = Native.MapMemory(0, length, read, shared, descriptor, 0);
                if (readable == -1)
                {
                    return $"{direct},{Called("mprotect-shared-write-map", -1)}";
                }

                string changed = Called("mprotect-shared-write", Native.ProtectMemory(readable, length, read | write));
                _ = Native.UnmapMemory(readable, length);
                return $"{direct},{changed}";
            }
            finally
            {
                _ = Native.Close(descriptor);
            }
        }

        /// <summary>
        ///     Watches a file for access with inotify, closing the instance again at once, and keeps
        ///     the watch's result and <c>errno</c>.
        /// </summary>
        /// <param name="path">The file.</param>
        /// <returns>The watch's descriptor, or the instance's failure; -1 with <c>errno</c> set on failure.</returns>
        [UnsupportedOSPlatform("windows")]
        private static int Watched(string path)
        {
            const int closeOnExec = 0x80000;
            const uint accessed = 0x1, opened = 0x20; // IN_ACCESS, IN_OPEN
            int instance = Native.WatchInit(closeOnExec);
            if (instance < 0)
            {
                return instance;
            }

            int watch = Native.Watch(instance, path, accessed | opened);
            int error = Marshal.GetLastPInvokeError();
            _ = Native.Close(instance);
            Marshal.SetLastPInvokeError(error);
            return watch;
        }

        /// <summary>
        ///     Asks after and changes a file's attributes through <c>ioctl</c>s on a descriptor
        ///     opened only for reading, one of each type the filter refuses whatever the request:
        ///     <c>FS_IOC_GETFLAGS</c> and <c>FS_IOC_ENABLE_VERITY</c> ('f'), <c>FS_IOC_FSGETXATTR</c>
        ///     ('X') and <c>FICLONE</c> (0x94). None changes anything even where it is allowed: the
        ///     verity request carries version 0, which no kernel takes, and a clone into a
        ///     descriptor opened for reading is refused by the kernel itself.
        /// </summary>
        /// <param name="path">A file the worker may read.</param>
        /// <returns><c>get-flags=...,enable-verity=...,get-fsxattr=...,clone-into=...</c>.</returns>
        [UnsupportedOSPlatform("windows")]
        private static unsafe string AttributeControls(string path)
        {
            const int readOnly = 0, closeOnExec = 0x80000, room = 256;
            const long getFlags = 0x80086601, enableVerity = 0x40806685, getExtendedAttributes = 0x801C581F, clone = 0x40049409;
            int descriptor = Native.Open(path, readOnly | closeOnExec);
            if (descriptor < 0)
            {
                return Called("ioctl-open", descriptor);
            }

            try
            {
                // Room for the largest argument, fsverity_enable_arg's 128 bytes, all zero.
                byte* argument = stackalloc byte[room];
                new Span<byte>(argument, room).Clear();
                return string.Join(
                    ',',
                    Called("get-flags", DeviceControl(descriptor, getFlags, (long)argument)),
                    Called("enable-verity", DeviceControl(descriptor, enableVerity, (long)argument)),
                    Called("get-fsxattr", DeviceControl(descriptor, getExtendedAttributes, (long)argument)),
                    Called("clone-into", DeviceControl(descriptor, clone, descriptor)));
            }
            finally
            {
                _ = Native.Close(descriptor);
            }
        }

        /// <summary>
        ///     The attempts of <see cref="Files" />, each as Windows answers it: opens and mappings
        ///     by .NET's own calls, the rest by the system's, since .NET looks for a file before it
        ///     renames it and reports a directory it may not remove as merely in use.
        /// </summary>
        /// <param name="directory">The host's directory.</param>
        /// <param name="victim">The host's file in it.</param>
        /// <param name="beside">A file beside the worker's own assembly.</param>
        /// <param name="own">The worker's own assembly, which it may read.</param>
        /// <returns>Each attempt as <c>name=done</c>, <c>name=refused</c>, <c>name=errorN</c> or <c>name=</c> and the exception's type.</returns>
        [SupportedOSPlatform("windows")]
        private static List<string> WindowsFiles(string directory, string victim, string beside, string own)
        {
            return
            [
                // Section: reads, of files and of the directory, outside the allowance.
                Attempt("read-victim", () => File.OpenHandle(victim, FileMode.Open, FileAccess.Read).Dispose()),
                Attempt("read-beside", () => File.OpenHandle(beside, FileMode.Open, FileAccess.Read).Dispose()),
                Attempt("list-folder", () => _ = Directory.EnumerateFileSystemEntries(directory).Count()),

                // Section: the host's file changed in place, directly or through a shared mapping.
                Attempt("write-victim", () => File.OpenHandle(victim, FileMode.Open, FileAccess.Write).Dispose()),
                Attempt("read-write-victim", () => File.OpenHandle(victim, FileMode.Open, FileAccess.ReadWrite).Dispose()),
                Attempt("append-victim", () => File.OpenHandle(victim, FileMode.Append, FileAccess.Write).Dispose()),
                Attempt("truncate-open", () => File.OpenHandle(victim, FileMode.Truncate, FileAccess.Write).Dispose()),
                Attempt("map-victim", () => MemoryMappedFile.CreateFromFile(victim, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite).Dispose()),
                Attempt("map-own-write", () => MemoryMappedFile.CreateFromFile(own, FileMode.Open, null, 0, MemoryMappedFileAccess.ReadWrite).Dispose()),

                // Section: the host's file renamed, linked to or deleted, new entries beside it, and
                // the file's metadata.
                Answered("rename", Native.MoveFile(victim, Path.Combine(directory, "renamed.txt"), 0)),
                Answered("link", Native.CreateHardLink(Path.Combine(directory, "linked.txt"), victim, 0)),
                Answered("delete", Native.DeleteFile(victim)),
                Attempt("create", () => File.OpenHandle(Path.Combine(directory, "created.txt"), FileMode.CreateNew, FileAccess.Write).Dispose()),
                Answered("mkdir", Native.CreateDirectory(Path.Combine(directory, "made"), 0)),
                Answered("rmdir", Native.RemoveDirectoryW(Path.Combine(directory, "empty"))),
                Answered("set-attributes", Native.SetFileAttributes(victim, (uint)FileAttributes.Hidden)),
                Attempt("set-times", () => File.SetLastWriteTimeUtc(victim, DateTime.UnixEpoch)),
            ];
        }

        /// <summary>Tries, on any operating system, to read a file.</summary>
        /// <param name="path">The file; none when null.</param>
        /// <returns>The attempt as <c>file=done</c> or its refusal.</returns>
        private static string ReadFile(string? path)
        {
            return path is null
                ? "file=none"
                : OperatingSystem.IsWindows()
                    ? Attempt("file", () => File.OpenHandle(path, FileMode.Open, FileAccess.Read).Dispose())
                    : Called("file", Closed(Native.Open(path, 0x80000)));
        }

        /// <summary>Runs one attempt of .NET's and names its outcome.</summary>
        /// <param name="name">The attempt.</param>
        /// <param name="attempt">What it does.</param>
        /// <returns><c>name=done</c>, <c>name=refused</c> for a denial of access, or <c>name=</c> and the exception's type.</returns>
        private static string Attempt(string name, Action attempt)
        {
            try
            {
                attempt();
                return $"{name}=done";
            }
            catch (UnauthorizedAccessException)
            {
                return $"{name}=refused";
            }
#pragma warning disable CA1031 // A probe reports whatever stopped it.
            catch (Exception exception)
#pragma warning restore CA1031
            {
                return $"{name}={exception.GetType().Name}";
            }
        }

        /// <summary>A Windows call's outcome, from its result and its last error.</summary>
        /// <param name="name">The call.</param>
        /// <param name="result">Its result, nonzero on success.</param>
        /// <returns><c>name=done</c>, <c>name=refused</c> for <c>ERROR_ACCESS_DENIED</c>, or <c>name=errorN</c>.</returns>
        private static string Answered(string name, int result)
        {
            const int accessDenied = 5;
            int error = Marshal.GetLastPInvokeError();
            return result != 0
                ? $"{name}=done"
                : error == accessDenied ? $"{name}=refused" : $"{name}=error{error.ToString(CultureInfo.InvariantCulture)}";
        }

        /// <summary>A call's outcome, from its result and <c>errno</c>.</summary>
        /// <param name="name">The call.</param>
        /// <param name="result">Its result.</param>
        /// <returns><c>name=done</c>, <c>name=EACCES</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        private static string Called(string name, long result)
        {
            int error = Marshal.GetLastPInvokeError();
            return result >= 0
                ? $"{name}=done"
                : error switch
                {
                    Native.AccessDenied => $"{name}=EACCES",
                    Native.PermissionDenied => $"{name}=EPERM",
                    _ => $"{name}=errno{error.ToString(CultureInfo.InvariantCulture)}",
                };
        }

        /// <summary>Closes a descriptor that an attempt opened, keeping the attempt's result and <c>errno</c>.</summary>
        /// <param name="descriptor">The attempt's result.</param>
        /// <returns>The same result.</returns>
        [UnsupportedOSPlatform("windows")]
        private static int Closed(int descriptor)
        {
            int error = Marshal.GetLastPInvokeError();
            if (descriptor >= 0)
            {
                _ = Native.Close(descriptor);
            }

            Marshal.SetLastPInvokeError(error);
            return descriptor;
        }

        /// <summary>Closes and removes a file that an attempt created, keeping the attempt's result and <c>errno</c>.</summary>
        /// <param name="descriptor">The attempt's result.</param>
        /// <param name="path">The file.</param>
        /// <returns>The same result.</returns>
        [UnsupportedOSPlatform("windows")]
        private static int Removed(int descriptor, string path)
        {
            int error = Marshal.GetLastPInvokeError();
            if (descriptor >= 0)
            {
                _ = Native.Close(descriptor);
                _ = Native.Unlink(path);
            }

            Marshal.SetLastPInvokeError(error);
            return descriptor;
        }

        /// <summary>
        ///     Tries to connect to a TCP listener of the host's on the loopback interface. On Linux
        ///     the filter refuses the socket itself; on Windows a socket is created, and the
        ///     connection is the network's to refuse.
        /// </summary>
        /// <param name="argument">The listener's port.</param>
        /// <returns><c>connected</c>, or the socket error.</returns>
        private static string Connect(string? argument)
        {
            int port = int.Parse(argument ?? "0", CultureInfo.InvariantCulture);
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, port), budget.Token).AsTask().GetAwaiter().GetResult();
                return "connected";
            }
            catch (SocketException exception)
            {
                return $"refused ({exception.SocketErrorCode})";
            }
            catch (OperationCanceledException)
            {
                return "refused (no answer)";
            }
        }

        /// <summary>Tries to open a file, and closes it should that work.</summary>
        /// <param name="name">What the file is, for the report.</param>
        /// <param name="path">The file.</param>
        /// <param name="access">The access wanted.</param>
        /// <returns><c>name=opened</c>, <c>name=EACCES</c>, <c>name=EPERM</c> or <c>name=errnoN</c>.</returns>
        [UnsupportedOSPlatform("windows")]
        private static string Opened(string name, string path, int access)
        {
            const int closeOnExec = 0x80000;
            int descriptor = Native.Open(path, access | closeOnExec);
            int error = Marshal.GetLastPInvokeError();
            if (descriptor >= 0)
            {
                _ = Native.Close(descriptor);
                return $"{name}=opened";
            }

            return error switch
            {
                Native.AccessDenied => $"{name}=EACCES",
                Native.PermissionDenied => $"{name}=EPERM",
                _ => $"{name}=errno{error.ToString(CultureInfo.InvariantCulture)}",
            };
        }

        /// <summary>Whether, on Linux, any descriptor this process holds refers to a file.</summary>
        /// <param name="path">The file's path.</param>
        /// <returns><c>held</c>, or <c>not held</c> with the number of descriptors looked at.</returns>
        private static string Descriptors(string? path)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            int open = 0;
            foreach (string entry in Directory.EnumerateFileSystemEntries("/proc/self/fd"))
            {
                open++;
                if (int.TryParse(Path.GetFileName(entry), NumberStyles.None, CultureInfo.InvariantCulture, out int descriptor)
                    && string.Equals(UnixDescriptors.LinkTarget(descriptor), path, StringComparison.Ordinal))
                {
                    return "held";
                }
            }

            return $"not held ({open.ToString(CultureInfo.InvariantCulture)} open)";
        }

        /// <summary>
        ///     Looks, on Linux, in the host's temporary directory and in <c>/tmp</c>, for the pipes
        ///     the host's runtime listens on for a debugger, and asks of each whether this process
        ///     could open it.
        /// </summary>
        /// <param name="host">The host's process id.</param>
        /// <param name="directory">The host's temporary directory.</param>
        /// <returns><c>none</c>, or each pipe found as <c>name=reachable</c> or <c>name=closed</c>.</returns>
        private static string HostDebuggerPipes(int host, string? directory)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            const int mayWrite = 2, mayRead = 4;
            string pattern = $"clr-debug-pipe-{host.ToString(CultureInfo.InvariantCulture)}-*";
            List<string> found = [];
            string[] folders = [directory ?? "/tmp", "/tmp"];
            foreach (string folder in folders.Select(static folder => folder.Length > 1 ? folder.TrimEnd('/') : folder).Distinct(StringComparer.Ordinal))
            {
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(folder, pattern))
                    {
                        bool reachable = Native.Access(path, mayRead) == 0 || Native.Access(path, mayWrite) == 0;
                        found.Add($"{Path.GetFileName(path)}={(reachable ? "reachable" : "closed")}");
                    }
                }
                catch (DirectoryNotFoundException)
                {
                    // Nothing can be there.
                }
                catch (UnauthorizedAccessException)
                {
                    found.Add($"{folder}=unlisted");
                }
            }

            return found.Count == 0 ? "none" : string.Join(',', found);
        }

        /// <summary>
        ///     Reports, on Linux, whether this process leads a session of its own, which terminal
        ///     controls that session, and whether the process can open a controlling terminal.
        /// </summary>
        /// <returns>
        ///     <c>own</c> or <c>inherited</c>, then <c>terminal=N</c> with the terminal's device
        ///     number (0 for none), then <c>dev-tty=</c> with <c>opened</c> or the refusal.
        /// </returns>
        private static string Session()
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            // After the command's closing parenthesis come the state, the parent, the process group,
            // the session and the controlling terminal's device number.
            const int readWrite = 2, noControllingTerminal = 0x100;
            string stat = File.ReadAllText("/proc/self/stat");
            string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
            bool own = fields[3] == Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            return $"{(own ? "own" : "inherited")},terminal={fields[4]},{Opened("dev-tty", "/dev/tty", readWrite | noControllingTerminal)}";
        }

        /// <summary>Tries to create a TCP socket.</summary>
        /// <returns><c>created</c>, or the socket error.</returns>
        private static string OpenSocket()
        {
            try
            {
                using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                return "created";
            }
            catch (SocketException exception)
            {
                return $"refused ({exception.SocketErrorCode})";
            }
        }

        /// <summary>Tries to start another process: the muxer that runs this one.</summary>
        /// <returns><c>started</c>, or why not.</returns>
        private static string StartProcess()
        {
            try
            {
                using Process process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--version") { RedirectStandardOutput = true, UseShellExecute = false })!;
                process.WaitForExit();
                return "started";
            }
            catch (System.ComponentModel.Win32Exception exception)
            {
                return $"refused ({exception.NativeErrorCode})";
            }
        }

        /// <summary>
        ///     Renders the request as the real worker does, then reads what this process holds from
        ///     <c>/proc/self/status</c>: its address space, its private writable memory, and the
        ///     address space's peak, each in kB.
        /// </summary>
        /// <param name="request">The host's request.</param>
        /// <returns><c>size=..,data=..,peak=..</c>.</returns>
        private static string Footprint(WorkerRequest request)
        {
            if (!OperatingSystem.IsLinux())
            {
                return "not linux";
            }

            _ = PdfRenderer.Render(request.Pdf, request.Size, PdfiumRasterizer.Instance);
            var status = File.ReadAllLines("/proc/self/status")
                .Select(static line => line.Split(':', StringSplitOptions.TrimEntries))
                .Where(static pair => pair.Length == 2)
                .ToDictionary(static pair => pair[0], static pair => pair[1].Split(' ')[0], StringComparer.Ordinal);
            return $"size={status["VmSize"]},data={status["VmData"]},peak={status["VmPeak"]}";
        }

        /// <summary>Tries to write a file in a folder.</summary>
        /// <param name="directory">The folder; the temporary folder when null.</param>
        /// <returns><c>written</c>, or why not.</returns>
        private static string WriteFile(string? directory)
        {
            string path = Path.Combine(directory ?? Path.GetTempPath(), $"tellma-test-worker-{Environment.ProcessId}.bin");
            try
            {
                File.WriteAllBytes(path, new byte[4096]);
                File.Delete(path);
                return "written";
            }
            catch (IOException exception)
            {
                return $"refused ({exception.GetType().Name})";
            }
            catch (UnauthorizedAccessException)
            {
                return "refused (access)";
            }
        }

        /// <summary>
        ///     A thread started before the sandbox, idle until asked to run one piece of work, so a
        ///     probe can show that the system-call filter covers threads older than itself.
        /// </summary>
        private sealed class EarlyThread : IDisposable
        {
            private readonly SemaphoreSlim _go = new(0);
            private readonly SemaphoreSlim _done = new(0);
            private readonly Thread _thread;
            private Func<string>? _work;
            private string _result = "not run";

            /// <summary>Starts the thread, which waits.</summary>
            public EarlyThread()
            {
                _thread = new Thread(Serve) { IsBackground = true, Name = "early" };
                _thread.Start();
            }

            /// <summary>Runs <paramref name="work" /> on the thread and returns its result.</summary>
            /// <param name="work">The work.</param>
            /// <returns>The work's result.</returns>
            public string Run(Func<string> work)
            {
                _work = work;
                _ = _go.Release();
                _done.Wait();
                return _result;
            }

            /// <inheritdoc />
            public void Dispose()
            {
                _go.Dispose();
                _done.Dispose();
            }

            /// <summary>Waits for the work, runs it, and hands back its result.</summary>
            private void Serve()
            {
                _go.Wait();
                _result = _work!();
                _ = _done.Release();
            }
        }

        /// <summary>Burns CPU without end.</summary>
        private static void Spin()
        {
            ulong x = 1;
            while (x != 0)
            {
                x = (x * 6364136223846793005UL) + 1442695040888963407UL;
            }
        }

        /// <summary>Writes a result header only.</summary>
        /// <param name="output">The protocol output.</param>
        /// <param name="status">The status to claim.</param>
        /// <param name="length">The length to claim.</param>
        private static void WriteHeader(Stream output, int status, int length)
        {
            Span<byte> header = stackalloc byte[12];
            BinaryPrimitives.WriteUInt32LittleEndian(header, WorkerProtocol.ResultMagic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], status);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], length);
            output.Write(header);
            output.Flush();
        }

        /// <summary>A white PNG, encoded by Skia as the real worker encodes, optionally with a text chunk before its end.</summary>
        /// <param name="width">Its width.</param>
        /// <param name="height">Its height.</param>
        /// <param name="extraChunk">Whether to splice in a <c>tEXt</c> chunk.</param>
        /// <returns>The PNG.</returns>
        private static byte[] Png(int width, int height, bool extraChunk)
        {
            using var bitmap = new SKBitmap(new SKImageInfo(Math.Max(1, width), Math.Max(1, height), SKColorType.Bgra8888, SKAlphaType.Premul));
            bitmap.Erase(SKColors.White);
            using SKData data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
            byte[] png = data.ToArray();
            if (!extraChunk)
            {
                return png;
            }

            // The text chunk goes before the 12-byte end chunk; its CRC is not checked by the host.
            byte[] text = [0, 0, 0, 4, (byte)'t', (byte)'E', (byte)'X', (byte)'t', (byte)'a', (byte)'=', (byte)'b', 0, 0, 0, 0, 0];
            return [.. png.AsSpan(0, png.Length - 12), .. text, .. png.AsSpan(png.Length - 12)];
        }

        /// <summary>Breaks a PNG's image data while keeping its structure intact.</summary>
        /// <param name="png">The PNG.</param>
        /// <returns>The corrupted copy.</returns>
        private static byte[] Corrupt(byte[] png)
        {
            byte[] copy = [.. png];

            // Every byte of the first IDAT's data, after its length and type, up to its CRC.
            int type = copy.AsSpan().IndexOf("IDAT"u8);
            int length = BinaryPrimitives.ReadInt32BigEndian(copy.AsSpan(type - 4));
            for (int i = type + 4; i < type + 4 + length; i++)
            {
                copy[i] ^= 0x5A;
            }

            return copy;
        }
    }
}
