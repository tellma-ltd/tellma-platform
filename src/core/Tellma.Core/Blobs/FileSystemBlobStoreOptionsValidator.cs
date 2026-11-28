// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;

namespace Tellma.Core.Blobs
{
    /// <summary>
    ///     Validates <see cref="FileSystemBlobStoreOptions" /> after the Development default has been
    ///     applied: the root is set, absolute and, on Windows, short enough.
    /// </summary>
    /// <remarks>
    ///     The store applies the same rules when it is constructed, so it never runs against a
    ///     relative root (which would resolve against the process's working directory).
    /// </remarks>
    internal sealed class FileSystemBlobStoreOptionsValidator : IValidateOptions<FileSystemBlobStoreOptions>
    {
        /// <inheritdoc />
        public ValidateOptionsResult Validate(string? name, FileSystemBlobStoreOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            IReadOnlyList<string> failures = Describe(options.RootPath, OperatingSystem.IsWindows());
            return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
        }

        /// <summary>Every rule <paramref name="rootPath" /> breaks.</summary>
        /// <param name="rootPath">The configured root, after the Development default.</param>
        /// <param name="isWindows">Whether the Windows length rule applies.</param>
        /// <returns>One message per broken rule; empty when the root is valid.</returns>
        public static IReadOnlyList<string> Describe(string? rootPath, bool isWindows)
        {
            // Unset outside Development (in Development the default has already filled it, unless
            // the composition registers no deployment identity to name the folder after). A
            // drive-relative ("C:blobs") or rooted-but-unqualified ("\blobs") path is as relative
            // as "blobs": each resolves against process state.
            return rootPath switch
            {
                _ when string.IsNullOrWhiteSpace(rootPath) =>
                    [$"The file-system blob store's root path is not set; set {FileSystemBlobStoreOptions.SectionName}:RootPath to an absolute directory (it defaults only in Development)."],
                _ when !Path.IsPathFullyQualified(rootPath) =>
                    [$"The file-system blob store's root path '{rootPath}' is not absolute; set {FileSystemBlobStoreOptions.SectionName}:RootPath to a fully qualified directory."],
                _ when isWindows && rootPath.Length > FileSystemBlobStoreOptions.MaxWindowsRootPathLength =>
                    [$"The file-system blob store's root path is {rootPath.Length} characters long; on Windows it must be at most {FileSystemBlobStoreOptions.MaxWindowsRootPathLength}, so the deepest blob path stays within 260 characters."],
                _ => [],
            };
        }
    }
}
