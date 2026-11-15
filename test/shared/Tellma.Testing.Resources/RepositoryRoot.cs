// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Testing.Resources
{
    /// <summary>
    ///     Finds the repository a suite runs from: the nearest directory at or above a start
    ///     directory that holds the solution file, whatever the operating system and however deep
    ///     the build output sits.
    /// </summary>
    public static class RepositoryRoot
    {
        /// <summary>The solution file that marks the repository's root.</summary>
        public const string SolutionFile = "Tellma.slnx";

        /// <summary>The repository root above a directory.</summary>
        /// <param name="start">The directory to start from; the running suite's output directory when null.</param>
        /// <returns>The root's full path.</returns>
        /// <exception cref="InvalidOperationException">No directory at or above the start holds the solution file.</exception>
        public static string Find(string? start = null)
        {
            // The solution file rather than .git: a worktree's .git is a file, and a source archive
            // has none, but both carry the solution.
            string origin = Path.GetFullPath(start ?? AppContext.BaseDirectory);
            for (DirectoryInfo? directory = new(origin); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException($"No directory at or above '{origin}' holds {SolutionFile}; the audit runs from a build inside the repository.");
        }

        /// <summary>A path under the repository root, its segments joined with the operating system's separator.</summary>
        /// <param name="segments">The path's segments below the root, such as <c>src</c>, <c>core</c>.</param>
        /// <returns>The full path.</returns>
        /// <exception cref="InvalidOperationException">The repository root is not found above the running suite.</exception>
        public static string Combine(params string[] segments)
        {
            ArgumentNullException.ThrowIfNull(segments);
            return Path.Combine([Find(), .. segments]);
        }
    }
}
