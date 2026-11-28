// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Distro.Acme.Web.Tests.Infrastructure
{
    /// <summary>The source checkout's folders the suite reads: the repository and the distribution's projects.</summary>
    internal static class AcmePaths
    {
        /// <summary>The repository's root: the nearest folder above the suite's output that holds the solution.</summary>
        public static string Repository { get; } = FindRepository();

        /// <summary>The distribution's folder.</summary>
        public static string Distribution => Path.Combine(Repository, "distributions", "acme");

        /// <summary>The web project's folder.</summary>
        public static string Web => Path.Combine(Distribution, "src", "Tellma.Distro.Acme.Web");

        /// <summary>The migrator project's folder.</summary>
        public static string Migrator => Path.Combine(Distribution, "src", "Tellma.Distro.Acme.Migrator");

        /// <summary>Finds the repository's root.</summary>
        /// <returns>The folder.</returns>
        /// <exception cref="InvalidOperationException">The suite runs outside a source checkout.</exception>
        private static string FindRepository()
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Tellma.slnx")))
                {
                    return directory.FullName;
                }
            }

            throw new InvalidOperationException($"No folder above {AppContext.BaseDirectory} holds Tellma.slnx; the suite reads the source checkout it was built from.");
        }
    }
}
