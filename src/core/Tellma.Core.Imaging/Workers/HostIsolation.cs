// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     What a Linux host does to itself before it starts a worker. A worker runs under the
    ///     host's own account, and both the kernel and the .NET runtime let a process into another
    ///     process of its account through ordinary files, which no system-call filter on the worker
    ///     can tell from the files it must open. So the host closes those ways from its own side.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>/proc/&lt;pid&gt;</c>. Whatever Yama's ptrace scope, the kernel lets a process
    ///         that holds the capabilities of another process of its account read that process's
    ///         environment and memory map and reopen its descriptors; one of a .NET process's
    ///         descriptors is the memory its compiled code is written through, so reopening it is
    ///         writing the process's code. A process that is not dumpable is closed to all of that,
    ///         and to <c>/proc/&lt;pid&gt;/mem</c>, for every process without
    ///         <c>CAP_SYS_PTRACE</c>, which a worker never holds: it drops every capability, as
    ///         root too.
    ///     </para>
    ///     <para>
    ///         The debugger pipes. Unless it was started with its debugger disabled, the runtime
    ///         listens for a managed debugger on two named pipes in the temporary directory,
    ///         <c>clr-debug-pipe-&lt;pid&gt;-&lt;start time&gt;-in</c> and <c>-out</c>, open to the
    ///         account: whoever opens them is the process's debugger, and reads and writes its
    ///         memory. The host removes its own, so no debugger can attach to it afterwards; one
    ///         already attached stays attached.
    ///     </para>
    ///     <para>
    ///         The price is the host's. A process that is not dumpable leaves no core dump, and
    ///         only a process with <c>CAP_SYS_PTRACE</c> can still trace it or write a dump of it,
    ///         the runtime's own dump writer included. The runtime's diagnostics port is a socket,
    ///         which a worker cannot create, and stays as it is: counters and traces still work.
    ///     </para>
    /// </remarks>
    [SupportedOSPlatform("linux")]
    internal static partial class HostIsolation
    {
        /// <summary>What the names of the runtime's debugger pipes start with; the process id follows.</summary>
        internal const string DebuggerPipePrefix = "clr-debug-pipe-";

        // prctl operations (linux/prctl.h).
        private const int GetDumpable = 3;
        private const int SetDumpable = 4;

        // access(2) modes.
        private const int MayWrite = 2;
        private const int MayRead = 4;

        /// <summary>Whether this process is dumpable, and so open through <c>/proc</c> to processes of its account.</summary>
        internal static bool IsDumpable => Prctl(GetDumpable, 0, 0, 0, 0) != 0;

        /// <summary>
        ///     Closes this process to the processes of its own account that hold no capability: it
        ///     makes it non-dumpable and removes its runtime's debugger pipes. It costs a few system
        ///     calls and a listing of the temporary directory, changes nothing once done, and runs
        ///     before every worker start, because the runtime creates its pipes anew when a debugger
        ///     that was attached detaches.
        /// </summary>
        /// <exception cref="PdfWorkerException">The process could not be closed, so no worker may start.</exception>
        public static void CloseToWorkers()
        {
            // Section: /proc/<pid>, by the kernel's dumpable flag, read back.
            int refused = Prctl(SetDumpable, 0, 0, 0, 0);
            int error = Marshal.GetLastPInvokeError();
            if (refused != 0 || IsDumpable)
            {
                throw new PdfWorkerException(
                    PdfWorkerFailure.Start,
                    $"PDF workers cannot start: this process could not be made non-dumpable, which is what keeps a worker out of it (errno {error}).");
            }

            // Section: the debugger pipes.
            RemoveDebuggerPipes(DebuggerPipes(Environment.ProcessId));
        }

        /// <summary>
        ///     Removes debugger pipes, and refuses to go on beside one that is still there and that
        ///     this account can open: a worker could open it too.
        /// </summary>
        /// <param name="pipes">The paths; one that names nothing is passed over.</param>
        /// <exception cref="PdfWorkerException">A pipe could not be removed, so no worker may start.</exception>
        internal static void RemoveDebuggerPipes(IEnumerable<string> pipes)
        {
            foreach (string pipe in pipes)
            {
                try
                {
                    File.Delete(pipe);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // Not there, or not this account's to remove: the check below tells which.
                }

                if (Access(pipe, MayRead) == 0 || Access(pipe, MayWrite) == 0)
                {
                    throw new PdfWorkerException(
                        PdfWorkerFailure.Start,
                        $"PDF workers cannot start: the runtime's debugger pipe {pipe} could not be removed, and a worker could open it. Let the host write its temporary directory, or start it with DOTNET_EnableDiagnostics_Debugger=0.");
                }
            }
        }

        /// <summary>
        ///     The paths at which a process's debugger pipes are, or would be: in each temporary
        ///     directory, the names the runtime makes for them, and whatever else there starts with
        ///     the process's prefix.
        /// </summary>
        /// <remarks>
        ///     The made names find the pipes in a directory this account may write but not list;
        ///     the listing finds them should a runtime make its names otherwise.
        /// </remarks>
        /// <param name="processId">The process.</param>
        /// <returns>The paths; most name nothing.</returns>
        internal static IReadOnlyCollection<string> DebuggerPipes(int processId)
        {
            var pipes = new HashSet<string>(DebuggerPipeNames(processId), StringComparer.Ordinal);
            string pattern = string.Create(CultureInfo.InvariantCulture, $"{DebuggerPipePrefix}{processId}-*");
            foreach (string directory in TemporaryDirectories())
            {
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(directory, pattern))
                    {
                        _ = pipes.Add(path);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // No such directory, or one this account cannot list: the made names cover it.
                }
            }

            return pipes;
        }

        /// <summary>
        ///     The names the runtime makes for a process's two debugger pipes, in each temporary
        ///     directory: the process's id, then its start time, or 0 where the runtime could not
        ///     read one, then the pipe's direction.
        /// </summary>
        /// <param name="processId">The process.</param>
        /// <returns>The paths, whether or not anything is there.</returns>
        internal static IReadOnlyList<string> DebuggerPipeNames(int processId)
        {
            var names = new List<string>();
            string?[] keys = [StartTime(processId), "0"];
            foreach (string directory in TemporaryDirectories())
            {
                foreach (string? key in keys)
                {
                    if (key is not null)
                    {
                        string name = string.Create(CultureInfo.InvariantCulture, $"{DebuggerPipePrefix}{processId}-{key}");
                        names.Add(Path.Combine(directory, name + "-in"));
                        names.Add(Path.Combine(directory, name + "-out"));
                    }
                }
            }

            return names;
        }

        /// <summary>
        ///     The directories the runtime may have put its pipes in: the temporary directory as this
        ///     process sees it now, and the default one, in case the process changed its own since
        ///     it started.
        /// </summary>
        /// <returns>The directories, without a trailing separator.</returns>
        private static IEnumerable<string> TemporaryDirectories()
        {
            return new[] { Path.GetTempPath(), "/tmp" }
                .Select(static directory => directory.Length > 1 ? directory.TrimEnd('/') : directory)
                .Distinct(StringComparer.Ordinal);
        }

        /// <summary>A process's start time as the runtime reads it: field 22 of <c>/proc/&lt;pid&gt;/stat</c>, in clock ticks.</summary>
        /// <param name="processId">The process.</param>
        /// <returns>The field's text, or null when it cannot be read.</returns>
        private static string? StartTime(int processId)
        {
            try
            {
                // The command name, in parentheses, may itself hold spaces or parentheses; the fields
                // after the last ')' start with the state (field 3), so the start time is the 20th.
                string stat = File.ReadAllText(string.Create(CultureInfo.InvariantCulture, $"/proc/{processId}/stat"));
                string[] fields = stat[(stat.LastIndexOf(')') + 2)..].Split(' ');
                return fields.Length > 19 && ulong.TryParse(fields[19], NumberStyles.None, CultureInfo.InvariantCulture, out _) ? fields[19] : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        /// <summary><c>int prctl(int option, unsigned long, unsigned long, unsigned long, unsigned long)</c>.</summary>
        /// <param name="option">The operation.</param>
        /// <param name="argument2">Its first argument.</param>
        /// <param name="argument3">Its second argument.</param>
        /// <param name="argument4">Its third argument.</param>
        /// <param name="argument5">Its fourth argument.</param>
        /// <returns>The operation's result; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "prctl", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Prctl(int option, ulong argument2, ulong argument3, ulong argument4, ulong argument5);

        /// <summary><c>int access(const char *path, int mode)</c>.</summary>
        /// <param name="path">The file.</param>
        /// <param name="mode">The access asked about.</param>
        /// <returns>0 when this account has that access; -1 with <c>errno</c> set otherwise.</returns>
        [LibraryImport("libc", EntryPoint = "access", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Access(string path, int mode);
    }
}
