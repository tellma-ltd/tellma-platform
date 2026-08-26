// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Xml.Linq;

namespace Tellma.Core.Abstractions.Tests.Repository
{
    /// <summary>
    ///     A project missing from the solution file is silently never built, never tested, and never
    ///     format-checked — the quietest way for work to ship broken. This closes that gap for every
    ///     project, present and future, in both directions.
    /// </summary>
    public class SolutionCoverageTests
    {
        // The package-flow migrator asset is built by its own dedicated CI job against a local
        // package feed, with a nuget.config the rest of the solution must not inherit; it is left
        // out of the solution on purpose.
        private static readonly string[] DeliberatelyExcluded =
        [
            "test/core/assets/Tellma.Core.EntityFrameworkCore.MigrationsHost.Package/Tellma.Core.EntityFrameworkCore.MigrationsHost.Package.csproj",
        ];

        [Fact]
        public void Every_project_under_src_and_test_is_listed_in_the_solution()
        {
            DirectoryInfo root = FindRepositoryRoot();
            HashSet<string> listed = ListedProjects(root);

            List<string> missing = [];
            foreach (string project in ProjectsOnDisk(root))
            {
                if (!DeliberatelyExcluded.Contains(project, StringComparer.Ordinal) && !listed.Contains(project))
                {
                    missing.Add(project);
                }
            }

            Assert.Empty(missing);
        }

        [Fact]
        public void Every_project_the_solution_lists_exists_on_disk()
        {
            // The other direction: a path left behind by a rename builds nothing and fails nothing,
            // it just quietly stops covering what its name suggests.
            DirectoryInfo root = FindRepositoryRoot();

            List<string> dangling = [.. ListedProjects(root)
                .Where(project => !File.Exists(Path.Combine(root.FullName, project.Replace('/', Path.DirectorySeparatorChar))))];

            Assert.Empty(dangling);
        }

        private static HashSet<string> ListedProjects(DirectoryInfo root)
        {
            // Parsed as XML rather than scanned as text: a substring match is satisfied by a comment
            // and by any path that merely has the real one as a prefix.
            var solution = XDocument.Load(Path.Combine(root.FullName, "Tellma.slnx"));

            return [.. solution
                .Descendants("Project")
                .Select(static element => (string?)element.Attribute("Path"))
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(static path => path!.Replace('\\', '/'))];
        }

        private static IEnumerable<string> ProjectsOnDisk(DirectoryInfo root)
        {
            // eng/ is scanned too: it holds no shipped code, but a developer tool that drops
            // out of the solution stops being compiled and rots without anything going red.
            foreach (string area in new[] { "src", "test", "eng" })
            {
                string areaPath = Path.Combine(root.FullName, area);
                foreach (string project in Directory.EnumerateFiles(areaPath, "*.csproj", SearchOption.AllDirectories))
                {
                    // The solution stores forward-slashed paths relative to the repository root.
                    yield return Path.GetRelativePath(root.FullName, project).Replace('\\', '/');
                }
            }
        }

        private static DirectoryInfo FindRepositoryRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Tellma.slnx")))
            {
                directory = directory.Parent;
            }

            return directory
                ?? throw new InvalidOperationException(
                    "Could not find the repository root from the test output directory.");
        }
    }
}
