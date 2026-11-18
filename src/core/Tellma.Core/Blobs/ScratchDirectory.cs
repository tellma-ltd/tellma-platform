// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Configuration;
using System.Globalization;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     The host's directory for temporary files, such as a large upload on its way to the blob
    ///     store: <c>Tellma:ScratchPath</c> when configured, else the operating system's temporary
    ///     directory. The blobs startup check refuses a configured path that is not absolute.
    /// </summary>
    /// <param name="configuredPath">The configured path, or null or blank for the operating system's.</param>
    internal sealed class ScratchDirectory(string? configuredPath)
    {
        /// <summary>The configuration key of the scratch directory.</summary>
        public const string ConfigurationKey = "Tellma:ScratchPath";

        /// <summary>The configured path, or null when none is configured.</summary>
        public string? ConfiguredPath { get; } = string.IsNullOrWhiteSpace(configuredPath) ? null : configuredPath;

        /// <summary>The directory temporary files are created in.</summary>
        public string Path => ConfiguredPath ?? System.IO.Path.GetTempPath();

        /// <summary>The scratch directory a configuration names.</summary>
        /// <param name="configuration">The host's configuration.</param>
        /// <returns>The scratch directory.</returns>
        public static ScratchDirectory From(IConfiguration configuration)
        {
            ArgumentNullException.ThrowIfNull(configuration);
            return new ScratchDirectory(configuration[ConfigurationKey]);
        }

        /// <summary>
        ///     Opens a new, empty temporary file in the directory, created if absent. The file is
        ///     readable, writable and seekable, and is deleted when the stream is closed; on Windows the
        ///     operating system deletes it even when the process ends abruptly.
        /// </summary>
        /// <returns>The file's stream; the caller disposes it.</returns>
        public FileStream CreateTemporaryFile()
        {
            string directory = Path;
            _ = Directory.CreateDirectory(directory);

            // A random name, created exclusively, so two uploads never share a file and a planted
            // file is never opened.
            string path = System.IO.Path.Join(directory, "tellma-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
            return new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        }
    }
}
