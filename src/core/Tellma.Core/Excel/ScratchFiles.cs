// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Scratch files for the codec's spools, each randomly named in the scratch directory and gone
    ///     once its stream is disposed. On Windows the file is opened unshared and the system deletes it
    ///     when its last handle closes, a killed process's included. Elsewhere the file is created
    ///     readable and writable by its owner alone and unlinked as soon as it is open, so its name never
    ///     outlives the call that created it and no other account can open it while it is.
    /// </summary>
    internal static class ScratchFiles
    {
        /// <summary>The buffer each scratch file stream holds.</summary>
        private const int BufferSize = 64 * 1024;

        /// <summary>Creates a scratch file.</summary>
        /// <param name="directory">The scratch directory; it must exist.</param>
        /// <returns>The file, open for reading and writing, gone when the stream is disposed.</returns>
        /// <exception cref="IOException">The file could not be created.</exception>
        public static FileStream Create(string directory)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(directory);
            string path = Path.Combine(directory, string.Concat("tellma-", Guid.NewGuid().ToString("N"), ".tmp"));

            // --- Windows: the system deletes the file when its last handle closes, however the process ends ---
            if (OperatingSystem.IsWindows())
            {
                return new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.ReadWrite,
                    Share = FileShare.None,
                    Options = FileOptions.DeleteOnClose | FileOptions.SequentialScan,
                    BufferSize = BufferSize,
                });
            }

            // --- Elsewhere: owner-only from creation, then unlinked while open, so a crash leaves nothing ---
            FileStream stream = new(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.None,
                Options = FileOptions.SequentialScan,
                BufferSize = BufferSize,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });

            try
            {
                File.Delete(path);
                return stream;
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }
    }
}
