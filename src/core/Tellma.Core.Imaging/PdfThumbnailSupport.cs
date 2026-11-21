// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Hosting;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Imaging
{
    /// <summary>Whether a host renders PDF thumbnails, and why not when it does not.</summary>
    internal enum PdfThumbnailSupport
    {
        /// <summary>PDF thumbnails render, each in a worker process of its own.</summary>
        Enabled,

        /// <summary>
        ///     A host on Windows or macOS outside the Development environment renders none. A worker
        ///     on Windows can still end the host's process, and workers of one account can open each
        ///     other; a worker on macOS runs under no operating-system limit at all.
        /// </summary>
        DisabledOutsideDevelopment,

        /// <summary>
        ///     A Linux host whose C library is not glibc (musl, as on Alpine) renders none: the
        ///     runtime there signals its threads through a system call the worker's filter refuses.
        /// </summary>
        DisabledOnMusl,
    }

    /// <summary>
    ///     Decides whether this host renders PDF thumbnails. They render where the worker's
    ///     confinement holds, on Linux with glibc; and, for developers only, on Windows and macOS in
    ///     the Development environment.
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
            return For(OperatingSystem.IsLinux(), LinuxWithGlibc, environment?.IsDevelopment() == true);
        }

        /// <summary>Whether a host of the given shape renders PDF thumbnails.</summary>
        /// <param name="linux">Whether the host runs on Linux.</param>
        /// <param name="glibc">Whether the host's C library is glibc.</param>
        /// <param name="development">Whether the host runs in the Development environment.</param>
        /// <returns>The support.</returns>
        internal static PdfThumbnailSupport For(bool linux, bool glibc, bool development)
        {
            // On Linux the C library decides; anywhere else, the environment.
            return linux
                ? glibc ? PdfThumbnailSupport.Enabled : PdfThumbnailSupport.DisabledOnMusl
                : development ? PdfThumbnailSupport.Enabled : PdfThumbnailSupport.DisabledOutsideDevelopment;
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
                    "Outside Linux a PDF worker is only partly confined: on Windows it can still end the host's process and open other workers of the same account, and on macOS it runs under no operating-system limit. Such a host renders PDF thumbnails only in the Development environment; Linux with glibc is the production host for them.",
                PdfThumbnailSupport.DisabledOnMusl =>
                    "This host's C library is not glibc, and the PDF worker's system-call filter refuses the call the .NET runtime signals its threads with on musl. Run the host on a glibc-based Linux for PDF thumbnails.",
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
