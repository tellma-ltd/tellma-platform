// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Composition.Checks;

namespace Tellma.Core.Tests.Repository
{
    /// <summary>
    ///     The repository's <c>taxonomy.json</c> agrees with what the repository hosts: every
    ///     distribution folder is registered, every registered slug is well formed, no distribution
    ///     takes a reserved slug but the reference distribution, which reserves its own, and the
    ///     module folders and the modules registry name each other.
    /// </summary>
    public class TaxonomyTests
    {
        // The reference distribution's slug is reserved because the platform takes it.
        private const string ReferenceDistribution = "acme";

        // A module's projects are Tellma.Module.<M> and Tellma.Module.<M>.Abstractions.
        private const string ModulePrefix = "Tellma.Module.";

        [Fact]
        public void Every_distribution_folder_is_registered()
        {
            var taxonomy = Taxonomy.Read();
            string distributions = Path.Combine(taxonomy.Root, "distributions");

            string[] folders = Directory.Exists(distributions)
                ? [.. Directory.EnumerateDirectories(distributions).Select(static path => Path.GetFileName(path)!)]
                : [];

            Assert.All(folders, folder => Assert.Contains(folder, taxonomy.Distributions));
        }

        [Fact]
        public void Every_registered_slug_is_lowercase_letter_first_and_at_most_15_characters()
        {
            Assert.All(Taxonomy.Read().Distributions, static slug =>
            {
                Assert.InRange(slug.Length, 1, 15);
                Assert.InRange(slug[0], 'a', 'z');
                Assert.All(slug, static c => Assert.True(c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-', $"'{c}' is not allowed in a slug."));
            });
        }

        [Fact]
        public void No_distribution_takes_a_reserved_slug_except_the_reference_distribution()
        {
            var taxonomy = Taxonomy.Read();

            Assert.Contains(ReferenceDistribution, taxonomy.ReservedSlugs);
            Assert.DoesNotContain(taxonomy.Distributions, slug => slug != ReferenceDistribution && taxonomy.ReservedSlugs.Contains(slug));
        }

        [Fact]
        public void Every_module_folder_holds_a_module_project_whose_segment_is_registered()
        {
            var taxonomy = Taxonomy.Read();

            Assert.All(ModuleFolders(taxonomy.Root), folder =>
            {
                // --- The folder's module project, Tellma.Module.<M> beside its Abstractions ---
                string[] projects = [.. Directory.EnumerateDirectories(folder)
                    .Select(static path => Path.GetFileName(path)!)
                    .Where(static name => name.StartsWith(ModulePrefix, StringComparison.Ordinal) && !name.EndsWith(".Abstractions", StringComparison.Ordinal))];
                string module = Assert.Single(projects)[ModulePrefix.Length..];

                // --- Its segment is registered, and names the folder ---
                Assert.Contains(module, taxonomy.Modules);
                Assert.Equal(FolderName(module), Path.GetFileName(folder));
                Assert.True(File.Exists(Path.Combine(folder, ModulePrefix + module, ModulePrefix + module + ".csproj")), $"{ModulePrefix}{module} has no project file.");
            });
        }

        [Fact]
        public void Every_registered_module_is_a_pascal_case_segment_with_a_folder()
        {
            var taxonomy = Taxonomy.Read();
            string[] folders = [.. ModuleFolders(taxonomy.Root).Select(static path => Path.GetFileName(path)!)];

            Assert.All(taxonomy.Modules, module =>
            {
                Assert.Matches("^[A-Z][a-z0-9]*([A-Z][a-z0-9]*)*$", module);
                Assert.Contains(FolderName(module), folders);
            });
        }

        [Fact]
        public void Every_registry_is_an_array_of_distinct_names()
        {
            var taxonomy = Taxonomy.Read();

            IReadOnlyList<string>[] registries = [taxonomy.Modules, taxonomy.Compliance, taxonomy.ReservedSlugs, taxonomy.Distributions];
            Assert.All(registries, static names => Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count()));
        }

        /// <summary>The module folders under <c>src/module</c>; none when the category does not exist yet.</summary>
        /// <param name="root">The repository root.</param>
        /// <returns>The folders' paths.</returns>
        private static string[] ModuleFolders(string root)
        {
            string modules = Path.Combine(root, "src", "module");
            return Directory.Exists(modules) ? [.. Directory.EnumerateDirectories(modules)] : [];
        }

        /// <summary>
        ///     The folder a module's projects sit in: its PascalCase segments lowercased and joined by
        ///     hyphens, as every grouping folder is named (<c>Gl</c> in <c>gl</c>).
        /// </summary>
        /// <param name="module">The registered segment.</param>
        /// <returns>The folder name.</returns>
        private static string FolderName(string module)
        {
            return string.Concat(module.Select(static (c, i) => char.IsUpper(c) && i > 0 ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
        }

        /// <summary>The parsed registry and the repository root it was read from.</summary>
        /// <param name="Root">The repository root.</param>
        /// <param name="Modules">The modules registry.</param>
        /// <param name="Compliance">The compliance registry.</param>
        /// <param name="ReservedSlugs">The reserved slugs.</param>
        /// <param name="Distributions">The distributions the repository hosts.</param>
        private sealed record Taxonomy(
            string Root,
            IReadOnlyList<string> Modules,
            IReadOnlyList<string> Compliance,
            IReadOnlyList<string> ReservedSlugs,
            IReadOnlyList<string> Distributions)
        {
            public static Taxonomy Read()
            {
                string root = TaxonomyCheck.FindRepositoryRoot(AppContext.BaseDirectory)
                    ?? throw new InvalidOperationException("The tests run from inside the repository.");

                using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, TaxonomyCheck.FileName)));
                return new Taxonomy(
                    root,
                    Names(document.RootElement, "modules"),
                    Names(document.RootElement, "compliance"),
                    Names(document.RootElement, "reservedSlugs"),
                    Names(document.RootElement, "distributions"));
            }

            private static string[] Names(JsonElement root, string property)
            {
                // A missing or mistyped registry fails here, by name.
                JsonElement array = root.GetProperty(property);
                Assert.Equal(JsonValueKind.Array, array.ValueKind);
                return [.. array.EnumerateArray().Select(static element => element.GetString()!)];
            }
        }
    }
}
