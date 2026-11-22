// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using static Tellma.Core.Imaging.Workers.WindowsInterop;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     The AppContainer a confined Windows worker runs in: one of its own per worker, holding
    ///     one capability, which the files of the worker's allowance grant read to.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A process in an AppContainer passes an access check only where the object grants the
    ///         access to its package, to one of its capabilities, or to every AppContainer (the
    ///         <c>ALL APPLICATION PACKAGES</c> group), on top of what its account is granted. The
    ///         system's files and the runtime's installation under Program Files grant every
    ///         AppContainer read; the host's own files, its account's profile and its temporary
    ///         directory grant none, nor does the host's process, nor any other process but those
    ///         in the same AppContainer, which is why each worker gets one of its own. Holding no
    ///         network capability, an AppContainer reaches no network, the loopback interface
    ///         included.
    ///     </para>
    ///     <para>
    ///         The token is a lowbox token made from the worker's restricted, low-integrity token
    ///         by <c>NtCreateLowBoxToken</c>, the call the system's own AppContainer start makes,
    ///         undocumented but stable since Windows 8 and used the same way by Chromium's sandbox.
    ///         The documented start, <c>PROC_THREAD_ATTRIBUTE_SECURITY_CAPABILITIES</c>, needs an
    ///         AppContainer profile registered per user, which takes tens to hundreds of
    ///         milliseconds to create and delete, and refuses an environment as small as a
    ///         worker's.
    ///     </para>
    ///     <para>
    ///         The grants are access-control entries for the capability on each file, added once and
    ///         left in place: they let only processes holding this capability, the host's workers,
    ///         read the file, and a file a later build writes anew is granted again on the next
    ///         start. A file that already lets every AppContainer read it, as one under Program
    ///         Files does, is left alone. Where the host's account may not change a file's access
    ///         list, its workers cannot be confined, which a Development host reports and runs them
    ///         unconfined.
    ///     </para>
    /// </remarks>
    [SupportedOSPlatform("windows")]
    internal static unsafe partial class WindowsAppContainer
    {
        /// <summary>The name the workers' capability is derived from.</summary>
        public const string CapabilityName = "tellmaPdfWorker";

        // The capability's SID, derived once; and the files granted to it by this process.
        private static readonly Lazy<SecurityIdentifier> Capability = new(DeriveCapability, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly ConcurrentDictionary<string, byte> Granted = new(StringComparer.OrdinalIgnoreCase);

        // ALL APPLICATION PACKAGES, a group every AppContainer token holds.
        private static readonly SecurityIdentifier EveryAppContainer = new("S-1-15-2-1");

        /// <summary>The SID of the capability every worker holds and its allowance's files grant read to.</summary>
        public static SecurityIdentifier CapabilitySid => Capability.Value;

        /// <summary>
        ///     Makes the .NET installation a worker runs on readable to its AppContainer, where it is
        ///     not already: its muxer, and its <c>host</c> and <c>shared</c> directories with
        ///     everything beneath them.
        /// </summary>
        /// <remarks>
        ///     An installer grants every AppContainer read under Program Files, so the installation
        ///     there needs nothing. A runtime installed elsewhere (in a user's profile, or a
        ///     repository's own <c>.dotnet</c>) grants none, and the worker could not load it; the
        ///     workers' capability is granted read and execute on it instead, inherited by what lies
        ///     beneath, where this account may change its permissions.
        /// </remarks>
        /// <param name="muxer">The <c>dotnet</c> executable workers are started with.</param>
        /// <param name="grant">Grants read and execute on a file or a directory, as <see cref="GrantReadAndExecute" /> does.</param>
        /// <returns>Null when a worker's AppContainer can read the installation; otherwise why it cannot.</returns>
        public static string? GrantRuntime(string muxer, Action<string, bool> grant)
        {
            ArgumentNullException.ThrowIfNull(muxer);
            ArgumentNullException.ThrowIfNull(grant);
            string root = Path.GetDirectoryName(Path.GetFullPath(muxer))!;
            try
            {
                grant(muxer, false);
                grant(Path.Combine(root, "host"), true);
                grant(Path.Combine(root, "shared"), true);
                return null;
            }
            catch (Exception exception) when (IsGrantFailure(exception))
            {
                return $"its AppContainer cannot read the .NET installation at {root}, and this account cannot grant it read there ({exception.Message})";
            }
        }

        /// <summary>
        ///     Makes each of the host's own files of a worker's allowance readable to its
        ///     AppContainer, where it is not already.
        /// </summary>
        /// <param name="files">The files; one that is missing is skipped, for starting a worker to report.</param>
        /// <param name="grant">Grants read and execute on a file, as <see cref="GrantReadAndExecute" /> does.</param>
        /// <returns>Null when a worker's AppContainer can read every one; otherwise why it cannot read the first it cannot.</returns>
        public static string? GrantRead(IReadOnlyList<string> files, Action<string, bool> grant)
        {
            ArgumentNullException.ThrowIfNull(files);
            ArgumentNullException.ThrowIfNull(grant);
            foreach (string file in files)
            {
                if (!File.Exists(file))
                {
                    continue;
                }

                try
                {
                    grant(file, false);
                }
                catch (Exception exception) when (IsGrantFailure(exception))
                {
                    return $"its AppContainer cannot read the host's file {file}, and this account cannot grant it read there ({exception.Message})";
                }
            }

            return null;
        }

        /// <summary>
        ///     Whether every AppContainer, or the workers' capability, may read a directory and
        ///     everything beneath it, as Program Files lets every AppContainer: a host installed in
        ///     one hands its configuration to its workers.
        /// </summary>
        /// <param name="directory">The directory.</param>
        /// <returns>True when they may; false when they may not, or its access list cannot be read.</returns>
        public static bool ReadsInFull(string directory)
        {
            ArgumentNullException.ThrowIfNull(directory);
            try
            {
                return Grants(new DirectoryInfo(directory).GetAccessControl(AccessControlSections.Access), directory: true);
            }
            catch (Exception exception) when (IsGrantFailure(exception))
            {
                return false;
            }
        }

        /// <summary>
        ///     Grants the workers' capability read and execute on a file, or on a directory and
        ///     everything beneath it, unless every AppContainer or the capability can already read
        ///     it so.
        /// </summary>
        /// <param name="path">The file or directory.</param>
        /// <param name="directory">Whether it is a directory.</param>
        /// <exception cref="UnauthorizedAccessException">Its access control could not be read or changed.</exception>
        /// <exception cref="IOException">It could not be reached.</exception>
        internal static void GrantReadAndExecute(string path, bool directory)
        {
            // Section: an allowance already there, explicit or inherited, is enough.
            FileSystemSecurity security = directory
                ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
                : new FileInfo(path).GetAccessControl(AccessControlSections.Access);
            if (Grants(security, directory))
            {
                return;
            }

            // Section: a directory's entry is inherited by its files and subdirectories, which the
            // system applies to those already there as it writes the directory's access list.
            if (directory)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    CapabilitySid, FileSystemRights.ReadAndExecute, InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None, AccessControlType.Allow));
                new DirectoryInfo(path).SetAccessControl((DirectorySecurity)security);
            }
            else
            {
                security.AddAccessRule(new FileSystemAccessRule(CapabilitySid, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                new FileInfo(path).SetAccessControl((FileSecurity)security);
            }
        }

        /// <summary>
        ///     Whether an access list lets every AppContainer, or the workers' capability, read and
        ///     execute the object, and, for a directory, everything beneath it too.
        /// </summary>
        /// <remarks>
        ///     A directory often grants itself and what lies beneath it in two entries, as Program
        ///     Files does: read and execute on the directory alone, and generic read and execute
        ///     inherited by its files and subdirectories only. Either form counts.
        /// </remarks>
        /// <param name="security">The object's access list, its inherited entries included.</param>
        /// <param name="directory">Whether the object is a directory.</param>
        /// <returns>True when it does.</returns>
        internal static bool Grants(FileSystemSecurity security, bool directory)
        {
            ArgumentNullException.ThrowIfNull(security);
            const InheritanceFlags beneath = InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit;
            SecurityIdentifier capability = CapabilitySid;
            FileSystemAccessRule[] rules =
            [
                .. security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
                    .OfType<FileSystemAccessRule>()
                    .Where(rule => rule.AccessControlType == AccessControlType.Allow
                        && (EveryAppContainer.Equals(rule.IdentityReference) || capability.Equals(rule.IdentityReference))
                        && ReadsAndExecutes(rule.FileSystemRights)),
            ];

            // Section: the object itself, by an entry that is not only inherited; and for a
            // directory, what lies beneath it, by one its files and subdirectories all inherit.
            bool itself = rules.Any(static rule => !rule.PropagationFlags.HasFlag(PropagationFlags.InheritOnly));
            bool below = rules.Any(static rule => (rule.InheritanceFlags & beneath) == beneath && !rule.PropagationFlags.HasFlag(PropagationFlags.NoPropagateInherit));
            return itself && (!directory || below);
        }

        /// <summary>Whether access rights include reading and executing, as specific rights or as generic ones.</summary>
        /// <param name="rights">The rights, generic bits included as the access list holds them.</param>
        /// <returns>True when they do.</returns>
        private static bool ReadsAndExecutes(FileSystemRights rights)
        {
            // GENERIC_ALL, GENERIC_EXECUTE and GENERIC_READ (winnt.h), which an inherited entry may
            // hold in place of the rights they map to.
            const uint genericAll = 0x1000_0000, genericExecute = 0x2000_0000, genericRead = 0x8000_0000;
            uint mask = unchecked((uint)rights);
            return (rights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute
                || (mask & genericAll) != 0
                || (mask & (genericRead | genericExecute)) == (genericRead | genericExecute);
        }

        /// <summary>
        ///     Grants the workers' capability read and execute on each file, unless the file already
        ///     lets every AppContainer or the capability read it, as a worker starts.
        /// </summary>
        /// <param name="files">The files.</param>
        /// <exception cref="PdfWorkerException">A file's access control could not be read or changed.</exception>
        public static void GrantRead(IReadOnlyList<string> files)
        {
            // Section: each file once per process: one granted here, or found granting, is
            // recorded, and not asked after again.
            ArgumentNullException.ThrowIfNull(files);
            string[] pending = [.. files.Where(static file => !Granted.ContainsKey(file))];
            if (GrantRead(pending, GrantOnce) is string failure)
            {
                throw new PdfWorkerException(PdfWorkerFailure.Start, $"A worker cannot be confined: {failure}.");
            }
        }

        /// <summary>Grants read and execute on a file as <see cref="GrantReadAndExecute" /> does, and records it as granted.</summary>
        /// <param name="file">The file.</param>
        /// <param name="directory">False: a file.</param>
        private static void GrantOnce(string file, bool directory)
        {
            GrantReadAndExecute(file, directory);
            _ = Granted.TryAdd(file, 0);
        }

        /// <summary>
        ///     Whether an exception is one that reading or changing an access list throws: no access,
        ///     a path that cannot be reached, a missing privilege, a volume without access lists, or
        ///     the capability that could not be derived.
        /// </summary>
        /// <param name="exception">The exception.</param>
        /// <returns>True when it is.</returns>
        private static bool IsGrantFailure(Exception exception)
        {
            return exception is UnauthorizedAccessException or IOException or InvalidOperationException or NotSupportedException or PdfWorkerException;
        }

        /// <summary>
        ///     Makes a lowbox token from the given one: in an AppContainer of the worker's own,
        ///     holding the workers' capability, at low integrity.
        /// </summary>
        /// <param name="token">The worker's restricted token, opened for duplication.</param>
        /// <returns>The lowbox token, which the caller closes.</returns>
        /// <exception cref="PdfWorkerException">The token could not be made.</exception>
        public static nint CreateToken(nint token)
        {
            // Section: the package, named afresh for every worker; deriving its SID registers
            // nothing.
            byte[] capability = new byte[CapabilitySid.BinaryLength];
            CapabilitySid.GetBinaryForm(capability, 0);
            int result = DeriveAppContainerSidFromAppContainerName($"Tellma.PdfWorker.{Guid.NewGuid():N}", out nint package);
            if (result != 0)
            {
                throw new PdfWorkerException(PdfWorkerFailure.Start, $"A worker's AppContainer could not be named (HRESULT 0x{result:X8}).");
            }

            try
            {
                // Section: the token, with the capability enabled and the access the process's
                // creation needs.
                fixed (byte* sid = capability)
                {
                    var attributes = new SidAndAttributes { Sid = sid, Attributes = SeGroupEnabled };
                    var objectAttributes = new ObjectAttributes { Length = (uint)sizeof(ObjectAttributes) };
                    nint lowBox = 0;
                    int status = NtCreateLowBoxToken(
                        &lowBox, token, TokenAssignPrimary | TokenDuplicate | TokenQuery, &objectAttributes, package, 1, &attributes, 0, null);
                    return status >= 0
                        ? lowBox
                        : throw new PdfWorkerException(PdfWorkerFailure.Start, $"A worker's AppContainer token could not be created (NTSTATUS 0x{status:X8}).");
                }
            }
            finally
            {
                _ = FreeSid(package);
            }
        }

        /// <summary>Derives the workers' capability SID from its name.</summary>
        /// <returns>The SID.</returns>
        private static SecurityIdentifier DeriveCapability()
        {
            nint* groups = null, capabilities = null;
            uint groupCount = 0, capabilityCount = 0;
            if (DeriveCapabilitySidsFromName(CapabilityName, &groups, &groupCount, &capabilities, &capabilityCount) == 0 || capabilityCount < 1)
            {
                throw new PdfWorkerException(PdfWorkerFailure.Start, $"The workers' AppContainer capability could not be derived (error {Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                return new SecurityIdentifier(capabilities[0]);
            }
            finally
            {
                // Section: each SID and both arrays were allocated for the caller to free.
                for (uint index = 0; index < groupCount; index++)
                {
                    _ = LocalFree(groups[index]);
                }

                for (uint index = 0; index < capabilityCount; index++)
                {
                    _ = LocalFree(capabilities[index]);
                }

                _ = LocalFree((nint)groups);
                _ = LocalFree((nint)capabilities);
            }
        }
    }
}
