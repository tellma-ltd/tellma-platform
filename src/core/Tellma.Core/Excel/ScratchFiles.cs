// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Scratch files for the codec's spools: each randomly named in the scratch directory, read
    ///     and written by its creator alone, and deleted by the operating system when closed, so a
    ///     crashed request leaves nothing behind.
    /// </summary>
    internal static class ScratchFiles
    {
        /// <summary>The buffer each scratch file stream holds.</summary>
        private const int BufferSize = 64 * 1024;

        /// <summary>Creates a scratch file.</summary>
        /// <param name="directory">The scratch directory; it must exist.</param>
        /// <returns>The file, open for reading and writing, deleted when the stream is disposed.</returns>
        /// <exception cref="IOException">The file could not be created.</exception>
        public static FileStream Create(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            string path = Path.Combine(directory, string.Concat("tellma-", Guid.NewGuid().ToString("N"), ".tmp"));
            return new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                BufferSize,
                FileOptions.DeleteOnClose | FileOptions.SequentialScan);
        }
    }
}
