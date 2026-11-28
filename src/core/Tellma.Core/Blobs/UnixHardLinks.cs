// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Tellma.Core.Blobs
{
    /// <summary>The outcome of linking a new name to an existing file.</summary>
    internal enum HardLinkResult
    {
        /// <summary>The new name now refers to the file.</summary>
        Linked,

        /// <summary>The new name already existed; nothing changed.</summary>
        AlreadyExists,

        /// <summary>The file system cannot hard-link (no support, or the names are on different devices).</summary>
        NotSupported,

        /// <summary>Any other failure; see the error number.</summary>
        Failed,
    }

    /// <summary>
    ///     POSIX <c>link(2)</c>, the one create-only publish primitive every Unix file system with
    ///     hard links provides: it fails with <c>EEXIST</c> rather than replace an existing name.
    /// </summary>
    /// <remarks>
    ///     .NET's <c>File.Move(source, destination, overwrite: false)</c> on Unix checks that the
    ///     destination is absent and then calls <c>rename(2)</c>, which replaces whatever got there
    ///     in between, so two racing writers can both "succeed". <c>link(2)</c> makes the check and
    ///     the publish one atomic step. It is POSIX (Linux, macOS, the BSDs), unlike
    ///     <c>renameat2(RENAME_NOREPLACE)</c>, which is Linux-only and refused by some file systems.
    /// </remarks>
    [UnsupportedOSPlatform("windows")]
    internal static partial class UnixHardLinks
    {
        // EEXIST, EPERM and EXDEV are 17, 1 and 18 on every Unix .NET runs on.
        private const int EExist = 17;
        private const int EPerm = 1;
        private const int EXDev = 18;

        /// <summary>Links <paramref name="newPath" /> to the file at <paramref name="existingPath" />, never replacing a name.</summary>
        /// <param name="existingPath">The existing file.</param>
        /// <param name="newPath">The name to create.</param>
        /// <param name="errorNumber">The <c>errno</c> of a failure; 0 on success.</param>
        /// <returns>What happened.</returns>
        public static HardLinkResult TryLink(string existingPath, string newPath, out int errorNumber)
        {
            if (Link(existingPath, newPath) == 0)
            {
                errorNumber = 0;
                return HardLinkResult.Linked;
            }

            errorNumber = Marshal.GetLastPInvokeError();
            return errorNumber switch
            {
                EExist => HardLinkResult.AlreadyExists,
                _ when IsNoHardLinkError(errorNumber) => HardLinkResult.NotSupported,
                _ => HardLinkResult.Failed,
            };
        }

        /// <summary>The system's description of an error number.</summary>
        /// <param name="errorNumber">The <c>errno</c>.</param>
        /// <returns>The description, with the number.</returns>
        public static string Describe(int errorNumber)
        {
            return $"{Marshal.GetPInvokeErrorMessage(errorNumber)} (errno {errorNumber})";
        }

        /// <summary>
        ///     Whether an error number says the file system cannot hard-link: <c>EPERM</c> (a file
        ///     system that forbids links), <c>EXDEV</c> (different devices), and the platform's
        ///     <c>ENOTSUP</c>/<c>EOPNOTSUPP</c> and <c>ENOSYS</c>.
        /// </summary>
        /// <param name="errorNumber">The <c>errno</c>.</param>
        /// <returns>True for a no-hard-link error.</returns>
        private static bool IsNoHardLinkError(int errorNumber)
        {
            if (errorNumber is EPerm or EXDev)
            {
                return true;
            }

            // The remaining numbers differ by platform: Linux has EOPNOTSUPP = ENOTSUP = 95 and
            // ENOSYS = 38; macOS and the BSDs have ENOTSUP = 45, EOPNOTSUPP = 102 and ENOSYS = 78.
            return OperatingSystem.IsLinux()
                ? errorNumber is 95 or 38
                : errorNumber is 45 or 102 or 78;
        }

        /// <summary><c>int link(const char *oldpath, const char *newpath)</c>.</summary>
        /// <param name="oldPath">The existing file.</param>
        /// <param name="newPath">The name to create.</param>
        /// <returns>0 on success; -1 with <c>errno</c> set on failure.</returns>
        [LibraryImport("libc", EntryPoint = "link", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Link(string oldPath, string newPath);
    }
}
