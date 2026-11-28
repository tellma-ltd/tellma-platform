// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Tests.Infrastructure;

namespace Tellma.Core.Tests.Composition.Checks
{
    /// <summary><c>core.taxonomy</c>: in Development, the repository's registry lists the slug.</summary>
    public sealed class TaxonomyCheckTests : IDisposable
    {
        private const string Check = "core.taxonomy";

        // A throwaway repository: <root>/.git and <root>/distributions/acme/src/Web as the content root.
        private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("tellma-taxonomy-");

        private string ContentRoot => Directory.CreateDirectory(Path.Combine(_root.FullName, "distributions", "acme", "src", "Web")).FullName;

        [Fact]
        public async Task A_listed_slug_passes()
        {
            MarkRepository(asWorktree: false);
            WriteTaxonomy(/*lang=json,strict*/ """{ "modules": [], "compliance": [], "reservedSlugs": ["acme"], "distributions": ["acme"] }""");

            Assert.Empty(await CheckAsync("Development"));
        }

        [Fact]
        public async Task An_unlisted_slug_is_reported_with_the_line_to_add()
        {
            MarkRepository(asWorktree: true);
            WriteTaxonomy(/*lang=json,strict*/ """{ "distributions": ["other"] }""");

            CompositionProblem problem = Assert.Single(await CheckAsync("Development"));
            Assert.Contains("'acme' is not listed", problem.Problem, StringComparison.Ordinal);
            Assert.Contains("Add \"acme\" to the \"distributions\" array", problem.Fix, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("not json")]
        [InlineData(/*lang=json,strict*/ """{ "distributions": "acme" }""")]
        [InlineData("""["acme"]""")]
        public async Task A_registry_that_cannot_be_read_is_reported(string content)
        {
            MarkRepository(asWorktree: false);
            WriteTaxonomy(content);

            Assert.Contains("cannot be read", Assert.Single(await CheckAsync("Development")).Problem, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Outside_Development_nothing_is_checked()
        {
            MarkRepository(asWorktree: false);
            WriteTaxonomy(/*lang=json,strict*/ """{ "distributions": [] }""");

            Assert.Empty(await CheckAsync("Production"));
        }

        [Fact]
        public async Task A_repository_without_the_registry_passes()
        {
            MarkRepository(asWorktree: false);

            Assert.Empty(await CheckAsync("Development"));
        }

        [Fact]
        public async Task A_registry_outside_any_repository_is_not_read()
        {
            // No .git: the file beside the content root's ancestors is not the repository's.
            WriteTaxonomy(/*lang=json,strict*/ """{ "distributions": [] }""");

            Assert.Empty(await CheckAsync("Development"));
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _root.Delete(recursive: true);
        }

        private void MarkRepository(bool asWorktree)
        {
            string git = Path.Combine(_root.FullName, ".git");
            if (asWorktree)
            {
                // A worktree's .git is a file pointing at the main checkout.
                File.WriteAllText(git, "gitdir: ../main/.git/worktrees/acme\n");
            }
            else
            {
                Directory.CreateDirectory(git);
            }
        }

        private void WriteTaxonomy(string content)
        {
            File.WriteAllText(Path.Combine(_root.FullName, "taxonomy.json"), content);
        }

        private async Task<List<CompositionProblem>> CheckAsync(string environment)
        {
            IReadOnlyList<CompositionProblem> problems = await TestComposition.CheckAsync(
                static _ => { },
                environment: new TestEnvironment(environment, ContentRoot));
            return problems.From(Check);
        }
    }
}
