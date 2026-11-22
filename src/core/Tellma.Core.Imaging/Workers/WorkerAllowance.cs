// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using Tellma.Core.Imaging.PdfWorker;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     What a confined worker may read: the runtime it runs on, the host's assemblies and native
    ///     libraries (PDFium and Skia among them) and the runtime files that name them, and on Linux
    ///     the system libraries, the dynamic loader's cache and the fonts PDFium renders with.
    ///     Nothing else of the host's files, its configuration and secrets beside its assemblies
    ///     included, and no file to write.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The host's own trusted assemblies and native-library directories are the worker's:
    ///         it starts on the host's runtime configuration and dependency manifest, so it resolves
    ///         the same files. Each file of the host's is allowed by name, never its directory,
    ///         since that directory holds the host's configuration too.
    ///     </para>
    ///     <para>
    ///         On Linux the runtime's installation is allowed beneath its <c>host</c> and
    ///         <c>shared</c> directories, and the system's library directory beneath the C library's,
    ///         where PDFium's and Skia's own dependencies are found; any other library the host has
    ///         mapped is allowed by name. The muxer and the ELF interpreter it names may be executed,
    ///         which the worker's restart does.
    ///     </para>
    ///     <para>
    ///         On Windows a worker's AppContainer reads the runtime's installation and the system's
    ///         files through the grants their installers give every AppContainer; the host grants
    ///         its worker capability read on the rest, file by file (<see cref="WindowsFiles" />).
    ///     </para>
    /// </remarks>
    /// <param name="Read">The files the worker may read.</param>
    /// <param name="ReadBeneath">The directories the worker may read and list, with everything beneath them.</param>
    /// <param name="Execute">The programs the worker may read and execute.</param>
    /// <param name="WindowsFiles">The files outside the runtime's installation a Windows worker needs read on.</param>
    internal sealed record WorkerAllowance(
        IReadOnlyList<string> Read, IReadOnlyList<string> ReadBeneath, IReadOnlyList<string> Execute, IReadOnlyList<string> WindowsFiles)
    {
        /// <summary>The directories PDFium looks for system fonts in on Linux.</summary>
        internal static readonly string[] LinuxFontDirectories = ["/usr/share/fonts", "/usr/local/share/fonts", "/usr/share/X11/fonts/Type1", "/usr/share/X11/fonts/TTF"];

        /// <summary>The Linux dynamic loader's files: its cache of library paths, and the libraries it preloads.</summary>
        internal static readonly string[] LinuxLoaderFiles = ["/etc/ld.so.cache", "/etc/ld.so.preload"];

        /// <summary>
        ///     The worker's arguments on Linux: <c>--confine</c>, then each allowed path behind the
        ///     argument that names how.
        /// </summary>
        /// <returns>The arguments.</returns>
        public List<string> Arguments()
        {
            List<string> arguments = [WorkerConfinement.ConfineArgument];
            foreach ((string argument, IReadOnlyList<string> paths) in (ReadOnlySpan<(string, IReadOnlyList<string>)>)[
                (WorkerConfinement.ReadBeneathArgument, ReadBeneath), (WorkerConfinement.ReadArgument, Read), (WorkerConfinement.ExecuteArgument, Execute)])
            {
                foreach (string path in paths)
                {
                    arguments.Add(argument);
                    arguments.Add(path);
                }
            }

            return arguments;
        }

        /// <summary>The allowance of a worker started from the given files, by this process's own runtime.</summary>
        /// <param name="files">The worker's files.</param>
        /// <returns>The allowance.</returns>
        public static WorkerAllowance For(PdfWorkerFiles files)
        {
            ArgumentNullException.ThrowIfNull(files);
            bool linux = OperatingSystem.IsLinux();
            return Compose(
                files,
                Split(AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string),
                NativeLibraries(Split(AppContext.GetData("NATIVE_DLL_SEARCH_DIRECTORIES") as string), linux),
                linux ? MappedLibraries() : [],
                linux ? ProgramInterpreter(files.Muxer) : null,
                linux ? [.. LinuxFontDirectories.Where(Directory.Exists)] : [],
                linux ? [.. LinuxLoaderFiles.Where(File.Exists)] : [],
                OperatingSystem.IsWindows());
        }

        /// <summary>Composes an allowance from what the host resolved and mapped.</summary>
        /// <param name="files">The worker's files.</param>
        /// <param name="trustedAssemblies">The host's trusted assemblies.</param>
        /// <param name="nativeLibraries">The native libraries in the host's native-library directories.</param>
        /// <param name="mappedLibraries">The shared libraries the host has mapped (Linux).</param>
        /// <param name="interpreter">The ELF interpreter the muxer names (Linux), or null.</param>
        /// <param name="fontDirectories">The font directories that exist (Linux).</param>
        /// <param name="loaderFiles">The dynamic loader's files that exist (Linux).</param>
        /// <param name="windows">Whether the paths are Windows paths, compared without case.</param>
        /// <returns>The allowance, each list ordered and without duplicates.</returns>
        internal static WorkerAllowance Compose(
            PdfWorkerFiles files,
            IEnumerable<string> trustedAssemblies,
            IEnumerable<string> nativeLibraries,
            IEnumerable<string> mappedLibraries,
            string? interpreter,
            IEnumerable<string> fontDirectories,
            IEnumerable<string> loaderFiles,
            bool windows)
        {
            ArgumentNullException.ThrowIfNull(files);
            StringComparer comparer = windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
            var installation = new List<string> { Join(files.Root, "host", windows), Join(files.Root, "shared", windows) };

            // Section: the host's own files outside the runtime's installation, each by name: the
            // runtime files the worker is started on, the worker, the trusted assemblies and the
            // native libraries. A development runtime configuration is read when present.
            string development = string.Concat(files.RuntimeConfiguration.AsSpan(0, files.RuntimeConfiguration.Length - ".json".Length), ".dev.json");
            var own = new SortedSet<string>(comparer) { files.RuntimeConfiguration, files.Dependencies, files.Worker };
            if (File.Exists(development))
            {
                _ = own.Add(development);
            }

            own.UnionWith(trustedAssemblies.Concat(nativeLibraries).Where(path => !Beneath(path, files.Root, windows)));

            // Section: on Linux, the system's libraries beneath the C library's directory, any other
            // library the host mapped by name, the loader's files and the fonts; and the two
            // programs the restart executes.
            var beneath = new SortedSet<string>(comparer);
            beneath.UnionWith(installation);
            var read = new SortedSet<string>(own, comparer);
            // A mapped library's path is a Linux one, whatever this process runs on.
            string? system = mappedLibraries
                .Where(static path => path[(path.LastIndexOf('/') + 1)..].StartsWith("libc.so", StringComparison.Ordinal))
                .Select(static path => path[..path.LastIndexOf('/')])
                .FirstOrDefault(static directory => directory.Length > 0);
            if (system is not null)
            {
                _ = beneath.Add(system);
            }

            read.UnionWith(mappedLibraries.Where(path => !Beneath(path, files.Root, windows) && (system is null || !Beneath(path, system, windows))));
            read.UnionWith(loaderFiles);
            beneath.UnionWith(fontDirectories);
            var execute = new SortedSet<string>(comparer) { files.Muxer };
            if (interpreter is not null)
            {
                _ = execute.Add(interpreter);
            }

            return new WorkerAllowance([.. read], [.. beneath], [.. execute], [.. own]);
        }

        /// <summary>
        ///     The native libraries in the given directories: on Linux the shared objects, on Windows
        ///     the DLLs, of each directory itself, not beneath it.
        /// </summary>
        /// <param name="directories">The directories.</param>
        /// <param name="linux">Whether the libraries are Linux shared objects.</param>
        /// <returns>The libraries' paths.</returns>
        internal static List<string> NativeLibraries(IEnumerable<string> directories, bool linux)
        {
            var libraries = new List<string>();
            foreach (string directory in directories.Where(Directory.Exists))
            {
                libraries.AddRange(Directory.EnumerateFiles(directory).Where(path => IsLibrary(path, linux)));
            }

            return libraries;
        }

        /// <summary>The shared libraries this process has mapped, from <c>/proc/self/maps</c>.</summary>
        /// <returns>Their paths, as the kernel resolved them.</returns>
        internal static List<string> MappedLibraries()
        {
            // Each line is the range, permissions, offset, device, inode and, for a file, its path.
            var libraries = new HashSet<string>(StringComparer.Ordinal);
            foreach (string line in File.ReadLines("/proc/self/maps"))
            {
                string[] fields = line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries);
                if (fields.Length == 6 && fields[5].StartsWith('/') && !fields[5].EndsWith(" (deleted)", StringComparison.Ordinal) && IsLibrary(fields[5], linux: true))
                {
                    _ = libraries.Add(fields[5]);
                }
            }

            return [.. libraries];
        }

        /// <summary>
        ///     The ELF interpreter a program names (its <c>PT_INTERP</c>), which the kernel opens to
        ///     run it: a 64-bit little-endian ELF file, as on x64 and arm64.
        /// </summary>
        /// <param name="program">The program.</param>
        /// <returns>The interpreter's path; null when the program names none or is not such a file.</returns>
        internal static string? ProgramInterpreter(string program)
        {
            const uint interpreterSegment = 3;
            using FileStream file = File.OpenRead(program);
            Span<byte> header = stackalloc byte[64];
            if (file.Read(header) < header.Length || !header[..4].SequenceEqual("\u007FELF"u8) || header[4] != 2 || header[5] != 1)
            {
                return null;
            }

            // Section: the program headers, each naming its type, file offset and size.
            long table = (long)BinaryPrimitives.ReadUInt64LittleEndian(header[0x20..]);
            int entrySize = BinaryPrimitives.ReadUInt16LittleEndian(header[0x36..]);
            int entries = BinaryPrimitives.ReadUInt16LittleEndian(header[0x38..]);
            Span<byte> entry = stackalloc byte[56];
            for (int index = 0; index < entries && entrySize >= entry.Length; index++)
            {
                file.Position = table + ((long)index * entrySize);
                file.ReadExactly(entry);
                if (BinaryPrimitives.ReadUInt32LittleEndian(entry) != interpreterSegment)
                {
                    continue;
                }

                long offset = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[8..]);
                long size = (long)BinaryPrimitives.ReadUInt64LittleEndian(entry[32..]);
                if (size is < 2 or > 4096)
                {
                    return null;
                }

                byte[] path = new byte[size];
                file.Position = offset;
                file.ReadExactly(path);
                return System.Text.Encoding.UTF8.GetString(path.AsSpan(0, path.Length - 1));
            }

            return null;
        }

        /// <summary>Whether a file's name is a native library's.</summary>
        /// <param name="path">The file.</param>
        /// <param name="linux">Whether the library is a Linux shared object, not a Windows DLL.</param>
        /// <returns>True for <c>*.so</c> and <c>*.so.*</c> on Linux, <c>*.dll</c> on Windows.</returns>
        private static bool IsLibrary(string path, bool linux)
        {
            string name = Path.GetFileName(path);
            return linux
                ? name.EndsWith(".so", StringComparison.Ordinal) || name.Contains(".so.", StringComparison.Ordinal)
                : name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Whether a path lies beneath a directory.</summary>
        /// <param name="path">The path.</param>
        /// <param name="directory">The directory.</param>
        /// <param name="windows">Whether the paths compare without case.</param>
        /// <returns>True when it does.</returns>
        private static bool Beneath(string path, string directory, bool windows)
        {
            string folder = directory.TrimEnd('/', '\\');
            return path.Length > folder.Length + 1
                && path.StartsWith(folder, windows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
                && path[folder.Length] is '/' or '\\';
        }

        /// <summary>A directory's child, joined as the paths' operating system joins them, whatever this process runs on.</summary>
        /// <param name="directory">The directory.</param>
        /// <param name="name">The child's name.</param>
        /// <param name="windows">Whether the paths are Windows paths.</param>
        /// <returns>The child's path.</returns>
        private static string Join(string directory, string name, bool windows)
        {
            char separator = windows ? '\\' : '/';
            return $"{directory.TrimEnd('/', '\\')}{separator}{name}";
        }

        /// <summary>Splits one of the runtime's path lists.</summary>
        /// <param name="list">The list, separated as the platform separates paths; null for none.</param>
        /// <returns>The paths, empty ones dropped.</returns>
        private static string[] Split(string? list)
        {
            return (list ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
