// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Win32.SafeHandles;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.Tests.Infrastructure
{
    /// <summary>
    ///     A file this process holds open through a handle (Windows) or descriptor (Linux) that a
    ///     child would inherit, as one a native library opened carelessly would be; and a look into
    ///     another process's handle table for it.
    /// </summary>
    internal sealed partial class InheritableMarker : IDisposable
    {
        // Windows: HANDLE_FLAG_INHERIT, PROCESS_DUP_HANDLE, DUPLICATE_SAME_ACCESS.
        private const uint HandleFlagInherit = 1;
        private const uint ProcessDuplicateHandle = 0x40;
        private const uint DuplicateSameAccess = 2;

        private readonly FileStream? _stream;
        private readonly int _descriptor = -1;

        /// <summary>Creates the marker file and opens it inheritably.</summary>
        public InheritableMarker()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tellma-inheritance-marker-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(Path, [1]);
            if (OperatingSystem.IsWindows())
            {
                _stream = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                Assert.True(SetHandleInformation(_stream.SafeFileHandle, HandleFlagInherit, HandleFlagInherit) != 0);
                Value = _stream.SafeFileHandle.DangerousGetHandle();
            }
            else
            {
                // open(2) without O_CLOEXEC: what .NET itself never does.
                _descriptor = Open(Path, 0);
                Assert.True(_descriptor >= 3, $"open failed (errno {Marshal.GetLastPInvokeError()}).");
                Value = _descriptor;
            }
        }

        /// <summary>The marker file's path.</summary>
        public string Path { get; }

        /// <summary>The handle's or descriptor's value in this process, which an inheriting child would hold too.</summary>
        public nint Value { get; }

        /// <summary>Whether another process holds the marker file open under the inherited value, or (on Linux) under any descriptor.</summary>
        /// <param name="processId">The process.</param>
        /// <returns>True when it does.</returns>
        public bool IsHeldBy(int processId)
        {
            if (OperatingSystem.IsWindows())
            {
                return IsHeldOnWindows(processId);
            }

            foreach (string entry in Directory.EnumerateFileSystemEntries(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/fd")))
            {
                try
                {
                    if (new FileInfo(entry).LinkTarget == Path)
                    {
                        return true;
                    }
                }
                catch (IOException)
                {
                    // The descriptor closed meanwhile.
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _stream?.Dispose();
            if (_descriptor >= 0 && !OperatingSystem.IsWindows())
            {
                _ = Close(_descriptor);
            }

            File.Delete(Path);
        }

        /// <summary>Duplicates the inherited value out of another process, if it has it, and compares its file.</summary>
        /// <param name="processId">The process.</param>
        /// <returns>True when that process's handle of the same value is the marker file.</returns>
        [SupportedOSPlatform("windows")]
        private unsafe bool IsHeldOnWindows(int processId)
        {
            nint process = OpenProcess(ProcessDuplicateHandle, 0, (uint)processId);
            Assert.True(process != 0, $"The worker could not be opened (error {Marshal.GetLastPInvokeError()}).");
            try
            {
                if (DuplicateHandle(process, Value, GetCurrentProcess(), out nint copy, 0, 0, DuplicateSameAccess) == 0)
                {
                    // No handle of that value in the other process.
                    return false;
                }

                using var handle = new SafeFileHandle(copy, ownsHandle: true);
                char* buffer = stackalloc char[1024];
                uint length = GetFinalPathNameByHandle(handle, buffer, 1024, 0);
                return length is > 0 and < 1024
                    && new string(buffer, 0, (int)length).EndsWith(System.IO.Path.GetFileName(Path), StringComparison.OrdinalIgnoreCase);
            }
            finally
            {
                _ = CloseHandle(process);
            }
        }

        [LibraryImport("libc", EntryPoint = "open", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [UnsupportedOSPlatform("windows")]
        private static partial int Open(string path, int flags);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [UnsupportedOSPlatform("windows")]
        private static partial int Close(int descriptor);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        private static partial int SetHandleInformation(SafeFileHandle handle, uint mask, uint flags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        private static partial nint OpenProcess(uint access, int inherit, uint processId);

        [LibraryImport("kernel32.dll")]
        [SupportedOSPlatform("windows")]
        private static partial nint GetCurrentProcess();

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        private static partial int DuplicateHandle(nint sourceProcess, nint sourceHandle, nint targetProcess, out nint targetHandle, uint access, int inherit, uint options);

        [LibraryImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        private static unsafe partial uint GetFinalPathNameByHandle(SafeFileHandle handle, char* path, uint length, uint flags);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [SupportedOSPlatform("windows")]
        private static partial int CloseHandle(nint handle);
    }
}
