// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     Where the file-system blob store keeps its files, bound from
    ///     <c>Tellma:Blobs:FileSystem</c> by <see cref="BlobsComposition.AddFileSystemBlobStore" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Tenant <c>42</c>'s blobs live under <c>{RootPath}/t42/</c>, one directory per kind and
    ///         fan-out segment, with in-flight writes under <c>{RootPath}/t42/.tmp/</c>.
    ///     </para>
    ///     <para>
    ///         In Development an unset root defaults to <c>Tellma/{slug}/blobs</c> under the user's
    ///         local application data folder (<c>%LOCALAPPDATA%</c> on Windows,
    ///         <c>~/.local/share</c> on Linux). Outside Development it must be set. Either way it must
    ///         be absolute and, on Windows, at most <see cref="MaxWindowsRootPathLength" /> characters,
    ///         so the deepest object path stays within the classic 260-character path limit that
    ///         backup and antivirus tools still assume.
    ///     </para>
    /// </remarks>
    public sealed class FileSystemBlobStoreOptions
    {
        /// <summary>The configuration section the options bind from.</summary>
        public const string SectionName = "Tellma:Blobs:FileSystem";

        /// <summary>
        ///     The longest root path accepted on Windows: 259 characters (the classic path limit less
        ///     its terminator) less the 106 characters the deepest object adds beneath the root — a
        ///     separator, <c>t2147483647</c>, and a maximal kind, fan-out segment, key and variant.
        /// </summary>
        public const int MaxWindowsRootPathLength = 259 - 106;

        /// <summary>The absolute directory under which every tenant's blob directory lives.</summary>
        public string? RootPath { get; set; }
    }
}
