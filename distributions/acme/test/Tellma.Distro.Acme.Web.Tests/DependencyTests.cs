// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Xml.Linq;
using Tellma.Distro.Acme.Web.Tests.Infrastructure;

namespace Tellma.Distro.Acme.Web.Tests
{
    /// <summary>
    ///     The dependency rules of a distribution: the platform never depends on a distribution, and
    ///     the web host reaches neither the identity engine's migrations nor a design-time package.
    /// </summary>
    public sealed class DependencyTests
    {
        [Fact]
        public void No_platform_project_references_a_distribution()
        {
            string distributions = Path.Combine(AcmePaths.Repository, "distributions") + Path.DirectorySeparatorChar;
            List<string> violations = [];
            foreach (string project in Directory.EnumerateFiles(Path.Combine(AcmePaths.Repository, "src"), "*.csproj", SearchOption.AllDirectories))
            {
                foreach (string reference in ProjectReferences(project))
                {
                    if (reference.StartsWith(distributions, StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add($"{Path.GetFileName(project)} references {Path.GetFileName(reference)}");
                    }
                }
            }

            Assert.Empty(violations);
        }

        [Fact]
        public void The_web_host_s_project_references_reach_no_migrations_or_design_project()
        {
            // --- The closure of project references, walked from the web project ---
            // Only references that bring an assembly: the client app's references the Migrator for
            // its build order alone, and nothing of it reaches the web host.
            HashSet<string> closure = new(StringComparer.OrdinalIgnoreCase);
            Stack<string> pending = new([Path.Combine(AcmePaths.Web, "Tellma.Distro.Acme.Web.csproj")]);
            while (pending.TryPop(out string? project))
            {
                if (closure.Add(project))
                {
                    foreach (string reference in ProjectReferences(project, assembliesOnly: true))
                    {
                        pending.Push(reference);
                    }
                }
            }

            // --- And every package each of them references directly ---
            string[] names = [.. closure
                .Select(static project => Path.GetFileNameWithoutExtension(project))
                .Concat(closure.SelectMany(PackageReferences))
                .Order(StringComparer.Ordinal)];
            Assert.Contains("Tellma.Identity", names);
            Assert.DoesNotContain(names, IsForbidden);
        }

        [Fact]
        public void The_web_host_s_dependency_manifest_lists_no_migrations_or_design_assembly()
        {
            // The full transitive closure, packages included, as the web host's build resolved it.
            string manifest = Path.Combine(AppContext.BaseDirectory, "Tellma.Distro.Acme.Web.deps.json");
            Assert.True(File.Exists(manifest), $"The web host's dependency manifest is not beside the suite: {manifest}");

            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            string[] libraries = [.. document.RootElement.GetProperty("libraries").EnumerateObject()
                .Select(static library => library.Name.Split('/')[0])];

            Assert.Contains("Tellma.Identity", libraries);
            Assert.DoesNotContain(libraries, IsForbidden);
        }

        [Fact]
        public void The_web_host_s_build_output_holds_no_file_of_the_migrator()
        {
            // The web project's own output folder, in the configuration and framework this suite was
            // built in (the suite's folder ends in bin/<configuration>/<framework>/). The suite's
            // own folder cannot tell: it references the Migrator itself.
            DirectoryInfo suite = new(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            string output = Path.Combine(AcmePaths.Web, "bin", suite.Parent!.Name, suite.Name);
            Assert.True(File.Exists(Path.Combine(output, "Tellma.Distro.Acme.Web.dll")), $"The web host's build output is not where the suite expects it: {output}");

            // The client app's build references the Migrator; none of its files (the launcher, its
            // manifests) may follow that reference into the web host, which would ship them.
            string[] migrator = [.. Directory.EnumerateFiles(output)
                .Select(static file => Path.GetFileName(file))
                .Where(static name => name.StartsWith("Tellma.Distro.Acme.Migrator.", StringComparison.OrdinalIgnoreCase))
                .Order(StringComparer.Ordinal)];
            Assert.Empty(migrator);
        }

        /// <summary>Whether a project or package is one the web host must never reach.</summary>
        /// <param name="name">The project or package name.</param>
        /// <returns>True when it is.</returns>
        private static bool IsForbidden(string name)
        {
            return name.Equals("Tellma.Identity.Migrations", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith(".Design", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A project's project references, as full paths.</summary>
        /// <param name="project">The project file.</param>
        /// <param name="assembliesOnly">Whether to leave out the references that bring no assembly (<c>ReferenceOutputAssembly="false"</c>).</param>
        /// <returns>The referenced project files.</returns>
        private static IEnumerable<string> ProjectReferences(string project, bool assembliesOnly = false)
        {
            string folder = Path.GetDirectoryName(project)!;
            return XDocument.Load(project).Descendants("ProjectReference")
                .Where(reference => !assembliesOnly || !string.Equals((string?)reference.Attribute("ReferenceOutputAssembly"), "false", StringComparison.OrdinalIgnoreCase))
                .Select(static reference => (string?)reference.Attribute("Include"))
                .OfType<string>()
                .Select(include => Path.GetFullPath(Path.Combine(folder, include.Replace('\\', Path.DirectorySeparatorChar))));
        }

        /// <summary>A project's package references.</summary>
        /// <param name="project">The project file.</param>
        /// <returns>The package names.</returns>
        private static IEnumerable<string> PackageReferences(string project)
        {
            return XDocument.Load(project).Descendants("PackageReference")
                .Select(static reference => (string?)reference.Attribute("Include"))
                .OfType<string>();
        }
    }
}
