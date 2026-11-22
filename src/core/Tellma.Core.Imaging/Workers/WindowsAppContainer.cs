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
    ///         start.
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

        /// <summary>The SID of the capability every worker holds and its allowance's files grant read to.</summary>
        public static SecurityIdentifier CapabilitySid => Capability.Value;

        /// <summary>Grants the workers' capability read and execute on each file, unless the file already grants it.</summary>
        /// <param name="files">The files.</param>
        /// <exception cref="PdfWorkerException">A file's access control could not be read or changed.</exception>
        public static void GrantRead(IReadOnlyList<string> files)
        {
            ArgumentNullException.ThrowIfNull(files);
            SecurityIdentifier capability = CapabilitySid;
            foreach (string file in files)
            {
                if (Granted.ContainsKey(file) || !File.Exists(file))
                {
                    continue;
                }

                try
                {
                    // Section: an explicit entry that already allows read and execute is enough;
                    // otherwise one is added, and only the access list is written back.
                    var info = new FileInfo(file);
                    FileSecurity security = info.GetAccessControl(AccessControlSections.Access);
                    bool granted = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                        .OfType<FileSystemAccessRule>()
                        .Any(rule => rule.AccessControlType == AccessControlType.Allow
                            && capability.Equals(rule.IdentityReference)
                            && (rule.FileSystemRights & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute);
                    if (!granted)
                    {
                        security.AddAccessRule(new FileSystemAccessRule(capability, FileSystemRights.ReadAndExecute, AccessControlType.Allow));
                        info.SetAccessControl(security);
                    }

                    _ = Granted.TryAdd(file, 0);
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or PrivilegeNotHeldException)
                {
                    throw new PdfWorkerException(
                        PdfWorkerFailure.Start, $"A worker's AppContainer could not be granted read on {file}: {exception.Message}", exception);
                }
            }
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
