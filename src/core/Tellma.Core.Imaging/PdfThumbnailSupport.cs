// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Tellma.Core.Imaging.Workers;

namespace Tellma.Core.Imaging
{
    /// <summary>Whether a host renders PDF thumbnails, and why not when it does not.</summary>
    internal enum PdfThumbnailSupport
    {
        /// <summary>PDF thumbnails render, each in a worker process of its own.</summary>
        Enabled,

        /// <summary>
        ///     A host on Windows or macOS outside the Development environment renders none. A worker
        ///     on Windows is confined by an AppContainer made through an undocumented system call,
        ///     not yet verified under the Windows hosting environments; a worker on macOS runs under
        ///     no operating-system limit at all.
        /// </summary>
        DisabledOutsideDevelopment,

        /// <summary>
        ///     A Linux host whose C library is not glibc (musl, as on Alpine) renders none: the
        ///     runtime there signals its threads through a system call the worker's filter refuses.
        /// </summary>
        DisabledOnMusl,

        /// <summary>
        ///     A Linux host whose kernel offers no Landlock renders none outside the Development
        ///     environment: its workers could not be confined to their own files, and could read
        ///     whatever the host's account can. In Development they render unconfined, and the
        ///     startup check warns of it.
        /// </summary>
        DisabledWithoutLandlock,
    }

    /// <summary>
    ///     Decides whether this host renders PDF thumbnails. They render where the worker's
    ///     confinement holds, on Linux with glibc and a kernel that offers Landlock; and, for
    ///     developers only, on Windows, macOS and Linux without Landlock in the Development
    ///     environment.
    /// </summary>
    internal static partial class PdfThumbnailPolicy
    {
        // Whether this process runs on Linux with glibc as its C library; probed once.
        private static readonly bool LinuxWithGlibc = OperatingSystem.IsLinux() && IsGlibc();

        /// <summary>Whether this host renders PDF thumbnails.</summary>
        /// <param name="environment">The host's environment; a host without one is not in Development.</param>
        /// <returns>The support.</returns>
        public static PdfThumbnailSupport For(IHostEnvironment? environment)
        {
            bool linux = OperatingSystem.IsLinux();
            return For(linux, LinuxWithGlibc, linux && PdfWorkerLauncher.CanConfine, environment?.IsDevelopment() == true);
        }

        /// <summary>Whether a host of the given shape renders PDF thumbnails.</summary>
        /// <param name="linux">Whether the host runs on Linux.</param>
        /// <param name="glibc">Whether the host's C library is glibc.</param>
        /// <param name="landlock">Whether the host's Linux kernel offers Landlock.</param>
        /// <param name="development">Whether the host runs in the Development environment.</param>
        /// <returns>The support.</returns>
        internal static PdfThumbnailSupport For(bool linux, bool glibc, bool landlock, bool development)
        {
            // On Linux the C library decides, then the kernel's Landlock, which only Development
            // does without; anywhere else, the environment alone.
            return !linux
                ? development ? PdfThumbnailSupport.Enabled : PdfThumbnailSupport.DisabledOutsideDevelopment
                : !glibc
                    ? PdfThumbnailSupport.DisabledOnMusl
                    : landlock || development ? PdfThumbnailSupport.Enabled : PdfThumbnailSupport.DisabledWithoutLandlock;
        }

        /// <summary>Why a host renders no PDF thumbnails, for its log.</summary>
        /// <param name="support">The support; not <see cref="PdfThumbnailSupport.Enabled" />.</param>
        /// <returns>The reason.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="support" /> is <see cref="PdfThumbnailSupport.Enabled" /> or undefined.</exception>
        public static string Reason(PdfThumbnailSupport support)
        {
            return support switch
            {
                PdfThumbnailSupport.DisabledOutsideDevelopment =>
                    "Outside Linux a PDF worker's confinement is not yet supported in production: on Windows it runs in an AppContainer made through an undocumented system call and not yet verified under the Windows hosting environments, and on macOS it runs under no operating-system limit. Such a host renders PDF thumbnails only in the Development environment; Linux with glibc and Landlock is the production host for them.",
                PdfThumbnailSupport.DisabledOnMusl =>
                    "This host's C library is not glibc, and the PDF worker's system-call filter refuses the call the .NET runtime signals its threads with on musl. Run the host on a glibc-based Linux for PDF thumbnails.",
                PdfThumbnailSupport.DisabledWithoutLandlock =>
                    "This host's Linux kernel offers no Landlock, so a PDF worker could not be confined to its own files and could read whatever the host's account can. Run the host on Linux 5.13 or later with Landlock enabled (it is then listed in /sys/kernel/security/lsm), under a container runtime whose system-call filter allows the Landlock calls (the default profile of current Docker releases does); a Development host renders PDF thumbnails without it.",
                PdfThumbnailSupport.Enabled => throw new ArgumentOutOfRangeException(nameof(support), support, "PDF thumbnails are enabled."),
                _ => throw new ArgumentOutOfRangeException(nameof(support), support, "The support is undefined."),
            };
        }

        /// <summary>Whether this process's C library is glibc: only glibc exports its version.</summary>
        /// <returns>True on glibc.</returns>
        [SupportedOSPlatform("linux")]
        private static bool IsGlibc()
        {
            try
            {
                return GnuLibcVersion() != 0;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
        }

        /// <summary><c>const char *gnu_get_libc_version(void)</c>, which only glibc has.</summary>
        /// <returns>The version string.</returns>
        [LibraryImport("libc", EntryPoint = "gnu_get_libc_version")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        [SupportedOSPlatform("linux")]
        private static partial nint GnuLibcVersion();
    }
}
