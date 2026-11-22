// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Excel;
using Tellma.Core.Tests.Localization;
using Tellma.Testing.Entities;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     The header index: the normal form headers are matched in, keys that collide across paths,
    ///     the bounded edit distance behind suggestions, and the candidates an entity's labels give
    ///     in each candidate culture.
    /// </summary>
    /// <param name="composition">The fixture model with its labels, composed once for the class.</param>
    public sealed class HeaderIndexTests(FixtureLabelsComposition composition) : IClassFixture<FixtureLabelsComposition>
    {
        [Theory]
        [InlineData("  Title  ", "Title")]
        [InlineData("Due \t\n on", "Due on")]
        [InlineData("Parent node  /  Id", "Parent node/Id")]
        [InlineData("Title ( EN )", "Title(EN)")]
        [InlineData("Café", "Café")]
        [InlineData(" Price ", "Price")]
        [InlineData("", "")]
        public void A_header_is_composed_trimmed_and_its_white_space_collapsed_and_dropped_beside_separators(string header, string expected)
        {
            Assert.Equal(expected, HeaderIndex.Normalize(header));
        }

        [Fact]
        public void A_header_that_is_not_well_formed_text_is_matched_as_written()
        {
            Assert.Equal("a\ud800b", HeaderIndex.Normalize("a\ud800b"));
        }

        [Fact]
        public void A_key_two_paths_give_collides_and_matches_neither_while_a_key_one_path_gives_twice_stays_unique()
        {
            var index = HeaderIndex.Merge(
            [
                new("Name", new("Name", null)),
                new("NAME", new("Name", null)),
                new("Title", new("Name", null)),
                new("Title", new("Subject", null)),
                new("Title", new("Code", null)),
                new(string.Empty, new("Code", null)),
            ]);

            Assert.True(index.TryMatch(" name ", out HeaderTarget target));
            Assert.Equal(new HeaderTarget("Name", null), target);
            Assert.False(index.TryMatch("title", out _));
            Assert.Equal([new("Name", null), new("Subject", null), new("Code", null)], index.Collisions("TITLE"));
            Assert.Empty(index.Collisions("Name"));
            Assert.Equal(2, index.Count);
        }

        [Theory]
        [InlineData("code", "code", 2, 0)]
        [InlineData("code", "cod", 2, 1)]
        [InlineData("price", "pirce", 1, 1)]
        [InlineData("kitten", "sitting", 3, 3)]
        [InlineData("kitten", "sitting", 2, null)]
        [InlineData("ab", "abcdef", 3, null)]
        [InlineData("", "abc", 3, 3)]
        public void The_distance_is_the_optimal_string_alignment_within_the_bound(string a, string b, int bound, int? expected)
        {
            Assert.Equal(expected, HeaderIndex.Distance(a, b, bound));
            Assert.Equal(expected, HeaderIndex.Distance(b, a, bound));
        }

        [Fact]
        public void The_nearest_keys_come_nearest_first_within_a_third_of_the_headers_length_and_never_the_exact_key()
        {
            var index = HeaderIndex.Merge(
            [
                new("Price", new("Price", null)),
                new("Prices", new("OptionalKind", null)),
                new("Pride", new("Kind", null)),
                new("Code", new("Code", null)),
            ]);

            Assert.Equal(["Price"], index.Nearest("Pirce").Select(static target => target.Path));
            Assert.Equal(["OptionalKind", "Kind"], index.Nearest("price").Select(static target => target.Path));
            Assert.Empty(index.Nearest("Something else entirely"));
            Assert.Empty(index.Nearest("   "));
        }

        [Fact]
        public void A_header_longer_than_the_suggested_length_is_measured_on_its_prefix()
        {
            string key = new string('a', HeaderIndex.MaxSuggestedHeaderLength - 1) + "b";
            var index = HeaderIndex.Merge([new(key, new("Code", null))]);

            Assert.Equal("Code", Assert.Single(index.Nearest(new string('a', HeaderIndex.MaxSuggestedHeaderLength + 500))).Path);
        }

        [Fact]
        public void An_entitys_headers_are_its_labels_in_every_candidate_culture_with_symbols_and_technical_paths()
        {
            using ExportScope scope = new(composition.Services, "en", "en", "ar");
            using ImportKit kit = new(composition.Services);
            HeaderIndexCache cache = new();

            HeaderIndex parent = cache.Headers(kit.Entity<Widget>(), collection: null, scope.Context);

            // The technical paths, the English and Arabic labels, and each with a catalogued symbol.
            Assert.Equal(new HeaderTarget("Code", null), Match(parent, "Code"));
            Assert.Equal(new HeaderTarget("Name", null), Match(parent, "Title"));
            Assert.Equal(new HeaderTarget("Name", null), Match(parent, "العنوان"));
            Assert.Equal(new HeaderTarget("Name", "ar"), Match(parent, $"Title ({scope.Symbol("ar")})"));
            Assert.Equal(new HeaderTarget("Name", "am"), Match(parent, $"العنوان ({scope.Symbol("am")})"));
            Assert.Equal(new HeaderTarget("Name", null), Match(parent, "Title (Name)"));
            Assert.Equal(new HeaderTarget("Name2", null), Match(parent, "name2"));
            Assert.Equal(new HeaderTarget("Id", null), Match(parent, "Id"));
            Assert.Equal(new HeaderTarget("Stamp", null), Match(parent, "Stamp"));

            // The same context reads the same index from the cache.
            Assert.Same(parent, cache.Headers(kit.Entity<Widget>(), collection: null, scope.Context));
        }

        [Fact]
        public void A_child_sheets_headers_name_its_parent_link_by_label_path_and_foreign_key_and_never_a_stamp()
        {
            using ExportScope scope = new(composition.Services, "en", "en");
            using ImportKit kit = new(composition.Services);
            ImportCollection parts = ImportPaths.Collections(kit.Entity<Widget>())[0];

            HeaderIndex index = new HeaderIndexCache().Headers(parts.Collection.Child, parts.Collection, scope.Context);

            Assert.Equal("Widget.Id", Match(index, "Widget.Id").Path);
            Assert.Equal("Widget.Id", Match(index, "WidgetId").Path);
            Assert.False(index.TryMatch("Stamp", out _));
        }

        [Fact]
        public void The_candidate_cultures_are_the_readers_then_the_tenants_then_english_each_once()
        {
            using ExportScope scope = new(composition.Services, "ar", "en", "ar");

            Assert.Equal(["ar", "en"], HeaderIndexCache.Cultures(scope.Context).Select(static culture => culture.Language));
        }

        /// <summary>A header's unique match.</summary>
        /// <param name="index">The index.</param>
        /// <param name="header">The header.</param>
        /// <returns>What it names.</returns>
        private static HeaderTarget Match(HeaderIndex index, string header)
        {
            Assert.True(index.TryMatch(header, out HeaderTarget target), $"The header '{header}' matches nothing.");
            return target;
        }
    }
}
