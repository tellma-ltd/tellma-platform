// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
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
    ///     what its sandbox and limits allow, the host's process and a thread older than the sandbox
    ///     among them, and reports it. One mode renders as the real worker does and reports what the
    ///     render mapped.
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

            // Section: the ready frame and the request, as a worker reads them.
            WorkerProtocol.WriteReady(output, capabilities);
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
                    WorkerProtocol.WriteResult(output, WorkerStatus.Rendered, Png(size, size, extraChunk: false));
                    return 0;

                case "failed":
                    WorkerProtocol.WriteResult(output, WorkerStatus.Failed, "OutOfMemoryException: the test worker ran out\nof memory"u8);
                    return 0;

                default:
                    // Section: probes, each reported as a refusal's detail.
                    WorkerProtocol.WriteResult(output, WorkerStatus.Refused, Encoding.UTF8.GetBytes(Probe(mode, remaining.Count > 1 ? remaining[1] : null, host, early, request)));
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
        /// <param name="host">The host's process id.</param>
        /// <param name="early">The thread started before the sandbox, for the probe that uses it.</param>
        /// <param name="request">The host's request, for the probe that renders it.</param>
        /// <returns>What it found, as <c>name: outcome</c>.</returns>
        private static string Probe(string mode, string? argument, int host, EarlyThread? early, WorkerRequest request)
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
                    "open-host" => $"open-host: {OpenHost(host)}",
                    "early-thread" => $"early-thread: {early!.Run(static () => $"socket {OpenSocket()}, process {StartProcess()}")}",
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
            long tgkill = x64 ? 234 : 131, tkill = x64 ? 200 : 130;
            const long pidfdOpen = 434;
            const int processPriority = 0, openFiles = 7;
            ulong* limit = stackalloc ulong[2];
            byte* mask = stackalloc byte[128];
            int self = Environment.ProcessId;

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
                Outcome("shmget", SystemV('m')),
                Outcome("msgget", SystemV('q')),
                Outcome("semget", SystemV('s')),

                // Section: the same calls on the worker itself.
                Outcome("kill-self", Native.Kill(self, 0)),
                Outcome("prlimit-self", Native.Prlimit(0, openFiles, null, limit)),
                Outcome("prlimit-own", Native.Prlimit(self, openFiles, null, limit)),
            ];
            return string.Join(',', outcomes);
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
        ///     Tries to open the host's process: on Windows, for each right that would let the worker
        ///     read or write its memory, run code in it, take its handles, change or end it; on Linux,
        ///     through its memory file.
        /// </summary>
        /// <param name="host">The host's process id.</param>
        /// <returns><c>refused</c>, or <c>opened (rights)</c>.</returns>
        private static string OpenHost(int host)
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
                ];
                List<string> opened = [];
                foreach ((string name, uint right) in rights)
                {
                    nint handle = Native.OpenProcess(right, 0, (uint)host);
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
                using Microsoft.Win32.SafeHandles.SafeFileHandle memory = File.OpenHandle($"/proc/{host.ToString(CultureInfo.InvariantCulture)}/mem");
                return "opened (mem)";
            }
            catch (UnauthorizedAccessException)
            {
                return "refused";
            }
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
