// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.TestWorker
{
    /// <summary>
    ///     A PDF worker that misbehaves on purpose, one way per mode, inside the real worker's
    ///     sandbox: it crashes, hangs, spins, allocates without end, breaks the protocol or answers
    ///     with a malformed image, or probes what its sandbox and limits allow and reports it.
    /// </summary>
    /// <remarks>
    ///     A probe reports through a refusal's detail, which the host reads as an ordinary answer;
    ///     the misbehaviours give the host nothing it should accept.
    /// </remarks>
    internal static class Program
    {
        /// <summary>Runs the mode named by the first argument the sandbox leaves.</summary>
        /// <param name="args">The command line.</param>
        /// <returns>The exit code.</returns>
        private static int Main(string[] args)
        {
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
                    Environment.FailFast("The test worker crashes before it is ready.");
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

            // As the real worker does: from here, input ending means the host is gone.
            WorkerSandbox.ExitWhenInputEnds(input);

            // Section: misbehaviours after the request.
            switch (mode)
            {
                case "crash-after-request":
                    Environment.FailFast("The test worker crashes while rendering.");
                    return 1;

                case "exit-after-request":
                    return 4;

                case "hang-after-request":
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
                    WorkerProtocol.WriteResult(output, WorkerStatus.Refused, Encoding.UTF8.GetBytes(Probe(mode)));
                    return 0;
            }
        }

        /// <summary>Runs one probe of the sandbox and the limits.</summary>
        /// <param name="mode">The probe.</param>
        /// <returns>What it found, as <c>name: outcome</c>.</returns>
        private static string Probe(string mode)
        {
            try
            {
                return mode switch
                {
                    "allocate-native" => $"allocated: {AllocateNative()}",
                    "allocate-managed" => $"allocated: {AllocateManaged()}",
                    "socket" => $"socket: {OpenSocket()}",
                    "process" => $"process: {StartProcess()}",
                    "environment" => $"environment: {string.Join(',', Environment.GetEnvironmentVariables().Keys.Cast<string>().Order(StringComparer.Ordinal))}",
                    "write-file" => $"write-file: {WriteFile()}",
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

        /// <summary>Tries to write a file in the temporary folder.</summary>
        /// <returns><c>written</c>, or why not.</returns>
        private static string WriteFile()
        {
            string path = Path.Combine(Path.GetTempPath(), $"tellma-test-worker-{Environment.ProcessId}.bin");
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
