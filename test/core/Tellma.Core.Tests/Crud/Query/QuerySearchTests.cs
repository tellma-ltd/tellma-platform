// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The search lowering: the searchable columns by their kind, the id with or without its
    ///     hash, the display name's slots in the tenant's languages, its integer source and prefix
    ///     in Latin and Arabic-Indic digits, the prefix-and-name split, a service's branch in place of
    ///     the columns', and a search no branch applies to.
    /// </summary>
    public sealed class QuerySearchTests : IAsyncDisposable
    {
        /// <summary>The query model.</summary>
        private readonly QueryRig _rig = new();

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The searchable columns, by kind and by the tenant's languages ---

        [Fact]
        public void Each_searchable_column_matches_by_its_kind_and_a_display_name_branch_already_there_is_not_repeated()
        {
            SearchFilter search = _rig.Search(QueryModel.Accounts, "ab");

            Assert.Equal("or(startsWith(Code, @search) | contains(Name, @search))", QueryRig.Render(search.Tree));
            Assert.Equal("search:QxString=ab", Parameters(search));
        }

        [Fact]
        public void Every_twin_the_tenants_languages_admit_is_searched_and_none_beyond()
        {
            _rig.Shape = MultilingualShape.All;

            SearchFilter search = _rig.Search(QueryModel.Accounts, "ab");

            Assert.Equal(
                "or(startsWith(Code, @search) | contains(Name, @search) | contains(Name2, @search) | contains(Name3, @search))",
                QueryRig.Render(search.Tree));
        }

        [Fact]
        public void A_child_surface_searches_the_conventions_columns()
        {
            SearchFilter search = _rig.Search(QueryModel.Lines, "ab");

            Assert.Equal("or(contains(Name, @search))", QueryRig.Render(search.Tree));
        }

        // --- The id ---

        [Theory]
        [InlineData("7")]
        [InlineData("#7")]
        [InlineData(" #7 ")]
        public void An_integer_or_a_hash_and_an_integer_also_matches_the_id(string text)
        {
            SearchFilter search = _rig.Search(QueryModel.Accounts, text);

            Assert.Equal("or(startsWith(Code, @search) | contains(Name, @search) | Id = @searchId)", QueryRig.Render(search.Tree));
            Assert.Equal($"search:QxString={text}; searchId:QxNumeric=7", Parameters(search));
        }

        [Theory]
        [InlineData("#3000000000")]
        [InlineData("٧")]
        [InlineData("7.5")]
        public void An_id_the_key_cannot_hold_or_one_not_in_latin_digits_is_no_id(string text)
        {
            SearchFilter search = _rig.Search(QueryModel.Accounts, text);

            Assert.DoesNotContain("@searchId", QueryRig.Render(search.Tree), StringComparison.Ordinal);
            Assert.Equal($"search:QxString={text}", Parameters(search));
        }

        // --- The display name: the split, the integer source and prefix ---

        [Fact]
        public void A_prefix_and_a_name_split_at_the_first_separator_over_every_admitted_slot()
        {
            _rig.Shape = MultilingualShape.All;

            SearchFilter search = _rig.Search(QueryModel.Accounts, "1101 - Cash - Petty");

            Assert.Equal(
                "or(startsWith(Code, @search) | contains(Name, @search) | contains(Name2, @search) | contains(Name3, @search) | "
                + "Code = @left and (startsWith(Name, @right) or startsWith(Name2, @right) or startsWith(Name3, @right)))",
                QueryRig.Render(search.Tree));
            Assert.Equal("search:QxString=1101 - Cash - Petty; left:QxString=1101; right:QxString=Cash - Petty", Parameters(search));
        }

        [Theory]
        [InlineData(" - Cash")]
        [InlineData("1101 -Cash")]
        public void A_text_without_a_prefix_before_the_separator_does_not_split(string text)
        {
            SearchFilter search = _rig.Search(QueryModel.Accounts, text);

            Assert.DoesNotContain("@left", QueryRig.Render(search.Tree), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("12")]
        [InlineData("١٢")]
        [InlineData("۱۲")]
        public void An_integer_prefix_matches_by_equality_in_latin_and_arabic_indic_digits(string text)
        {
            SearchFilter search = _rig.Search(QueryModel.Tickets, text);

            // The id reads Latin digits only; the display name's integers read all three runs.
            string id = text == "12" ? "Id = @searchId | " : string.Empty;
            Assert.Equal($"or({id}contains(Subject, @search) | Number = @searchId)", QueryRig.Render(search.Tree));
            Assert.Equal($"search:QxString={text}; searchId:QxNumeric=12", Parameters(search));
        }

        [Fact]
        public void An_integer_prefix_splits_only_when_its_side_parses()
        {
            SearchFilter split = _rig.Search(QueryModel.Tickets, "١٢ - Printer");
            SearchFilter whole = _rig.Search(QueryModel.Tickets, "ab - Printer");

            Assert.Equal("or(contains(Subject, @search) | Number = @left and (startsWith(Subject, @right)))", QueryRig.Render(split.Tree));
            Assert.Equal("search:QxString=١٢ - Printer; left:QxNumeric=12; right:QxString=Printer", Parameters(split));
            Assert.Equal("or(contains(Subject, @search))", QueryRig.Render(whole.Tree));
        }

        [Fact]
        public void An_integer_source_matches_by_equality_and_a_text_matches_nothing_there()
        {
            SearchFilter number = _rig.Search(QueryModel.Counters, "٤٢");
            SearchFilter text = _rig.Search(QueryModel.Counters, "abc");

            Assert.Equal("or(Number = @searchId)", QueryRig.Render(number.Tree));
            Assert.Equal("searchId:QxNumeric=42", Parameters(number));

            // No branch applies: the search matches no row, and binds nothing.
            Assert.Equal("false", QueryRig.Render(text.Tree));
            Assert.Empty(text.Parameters);
        }

        // --- The service's branch ---

        [Fact]
        public void A_services_branch_replaces_the_searchable_columns_and_keeps_the_id_and_display_name_branches()
        {
            SearchFilter search = _rig.Search(QueryModel.Accounts, "7", FilterTree.Leaf("contains(Name, @search) and Price > 0"));

            Assert.Equal("or(contains(Name, @search) and Price > 0 | Id = @searchId | contains(Name, @search) | startsWith(Code, @search))", QueryRig.Render(search.Tree));
            Assert.Equal("search:QxString=7; searchId:QxNumeric=7", Parameters(search));
        }

        // --- The bidi strip ---

        [Theory]
        [InlineData("‎ab‏", "ab")]
        [InlineData("a‪b‬", "ab")]
        [InlineData("a‍b‌", "a‍b‌")]
        [InlineData("⁦⁩", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void The_bidi_controls_are_stripped_and_zwj_and_zwnj_kept(string? text, string? stripped)
        {
            Assert.Equal(stripped, QueryShape.Strip(text));
        }

        /// <summary>A search's parameters, each as its name, its type and its invariant value, in order.</summary>
        /// <param name="search">The search.</param>
        /// <returns>The text.</returns>
        private static string Parameters(SearchFilter search)
        {
            return string.Join("; ", search.Parameters.Select(static parameter => string.Create(
                CultureInfo.InvariantCulture, $"{parameter.Declaration.Name}:{parameter.Declaration.Type}={parameter.Value}")));
        }
    }
}
