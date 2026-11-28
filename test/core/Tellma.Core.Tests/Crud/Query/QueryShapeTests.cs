// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     A request's admission: the default page, each ceiling with its limit and the value that
    ///     crossed it, the search measured once stripped, the shape refusals, the named filters
    ///     resolved in request order with every unknown name reported at its index, and the
    ///     ancestors kept for a page of a tree only.
    /// </summary>
    public sealed class QueryShapeTests : IAsyncDisposable
    {
        /// <summary>The query model.</summary>
        private readonly QueryRig _rig = new();

        /// <summary>The account stack's surface: take up to 100, a window of 1,000, a search of 20.</summary>
        private QuerySurface Accounts => _rig.Surfaces.Stack(QueryModel.Accounts)!;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The page and its ceilings ---

        [Fact]
        public void A_request_without_a_page_size_takes_fifty_rows()
        {
            var shape = QueryShape.Admit(new QueryRequest(), Accounts);

            Assert.Equal((0, QueryShape.DefaultTake), (shape.Skip, shape.Take));
        }

        [Fact]
        public void Each_ceiling_refuses_with_its_limit_its_value_and_its_maximum()
        {
            LimitExceededException take = Assert.Throws<LimitExceededException>(() => QueryShape.Admit(new QueryRequest { Take = 101 }, Accounts));
            LimitExceededException window = Assert.Throws<LimitExceededException>(() => QueryShape.Admit(new QueryRequest { Skip = 950, Take = 51 }, Accounts));
            LimitExceededException search = Assert.Throws<LimitExceededException>(() => QueryShape.Admit(new QueryRequest { Search = new string('a', 21) }, Accounts));

            Assert.Equal((LimitNames.MaxTake, 101, 100), (take.Limit, take.Actual, take.Maximum));
            Assert.Equal((LimitNames.SkipWindow, 1_001, 1_000), (window.Limit, window.Actual, window.Maximum));
            Assert.Equal((LimitNames.MaxSearchLength, 21, 20), (search.Limit, search.Actual, search.Maximum));
        }

        [Fact]
        public void A_page_at_every_ceiling_is_admitted_and_the_search_is_measured_once_stripped()
        {
            var shape = QueryShape.Admit(new QueryRequest { Skip = 900, Take = 100, Search = "‎" + new string('a', 20) + "‏" }, Accounts);

            Assert.Equal((900, 100, new string('a', 20)), (shape.Skip, shape.Take, shape.Search));
        }

        [Fact]
        public void A_window_past_the_integers_is_refused_and_reported_at_the_largest_integer()
        {
            LimitExceededException window = Assert.Throws<LimitExceededException>(() => QueryShape.Admit(new QueryRequest { Skip = int.MaxValue, Take = 1 }, Accounts));

            Assert.Equal((LimitNames.SkipWindow, int.MaxValue), (window.Limit, window.Actual));
        }

        [Theory]
        [InlineData(-1, 10, "skip")]
        [InlineData(0, 0, "take")]
        public void A_negative_skip_or_an_empty_page_is_a_bad_request(int skip, int take, string argument)
        {
            BadRequestException refused = Assert.Throws<BadRequestException>(() => QueryShape.Admit(new QueryRequest { Skip = skip, Take = take }, Accounts));

            Assert.True(refused.Arguments.ContainsKey(argument));
        }

        [Fact]
        public void A_search_of_bidi_controls_alone_is_no_search()
        {
            var shape = QueryShape.Admit(new QueryRequest { Search = "⁦⁩" }, Accounts);

            Assert.Null(shape.Search);
        }

        // --- The clauses ---

        [Fact]
        public void Blank_clauses_are_not_sent_and_each_filter_keeps_its_index()
        {
            var shape = QueryShape.Admit(new QueryRequest { Select = " ", Filters = ["", "Price > 0", " ", "Code != ''"], OrderBy = "\t" }, Accounts);

            Assert.Null(shape.Select);
            Assert.Null(shape.OrderBy);
            Assert.Equal([(1, "Price > 0"), (3, "Code != ''")], shape.Filters);
        }

        [Fact]
        public void A_having_clause_without_an_aggregation_is_a_bad_request()
        {
            _ = Assert.Throws<BadRequestException>(() => QueryShape.Admit(new QueryRequest { Having = "count() > 1" }, Accounts));
        }

        [Fact]
        public void Two_arguments_whose_names_differ_only_in_case_are_a_bad_request()
        {
            using var one = JsonDocument.Parse("1");
            Dictionary<string, JsonElement> arguments = new(StringComparer.Ordinal) { ["p"] = one.RootElement, ["P"] = one.RootElement };

            BadRequestException refused = Assert.Throws<BadRequestException>(() => QueryShape.Admit(new QueryRequest { Arguments = arguments }, Accounts));

            Assert.Equal("P", refused.Arguments["parameter"]);
        }

        // --- The named filters ---

        [Fact]
        public void The_named_filters_resolve_in_request_order()
        {
            var shape = QueryShape.Admit(new QueryRequest { NamedFilters = ["by-creator-email", "priced"] }, Accounts);

            Assert.Equal([(0, "by-creator-email"), (1, "priced")], shape.NamedFilters.Select(static filter => (filter.Index, filter.Name)));
            Assert.Equal(QmAccountFilters.Priced, Assert.IsType<FilterTree.LeafNode>(shape.NamedFilters[1].Tree).Text);
        }

        [Fact]
        public void Every_unknown_named_filter_is_one_diagnostic_at_its_index()
        {
            InvalidQueryException refused = Assert.Throws<InvalidQueryException>(() => QueryShape.Admit(new QueryRequest { NamedFilters = ["priced", "Priced", "nope"] }, Accounts));

            Assert.Equal(
                [
                    (ValidationCodes.QueryUnknownNamedFilter, "namedFilters[1]", "Priced"),
                    (ValidationCodes.QueryUnknownNamedFilter, "namedFilters[2]", "nope"),
                ],
                refused.Diagnostics.Select(static diagnostic => (diagnostic.Code, diagnostic.Location, Assert.Single(diagnostic.Arguments).Value)));
            Assert.All(refused.Diagnostics, static diagnostic => Assert.Equal("name", Assert.Single(diagnostic.Arguments).Key));
        }

        [Fact]
        public void A_child_surface_declares_no_named_filter()
        {
            QuerySurface lines = _rig.Surfaces.Child(QueryModel.Lines)!;

            InvalidQueryException refused = Assert.Throws<InvalidQueryException>(() => QueryShape.Admit(new QueryRequest { NamedFilters = ["priced"] }, lines));

            Assert.Equal("namedFilters[0]", Assert.Single(refused.Diagnostics).Location);
        }

        // --- The ancestors ---

        [Fact]
        public void The_ancestors_are_kept_for_a_page_of_a_tree_only()
        {
            QuerySurface nodes = _rig.Surfaces.Stack(QueryModel.Nodes)!;

            Assert.True(QueryShape.Admit(new QueryRequest { IncludeAncestors = true }, nodes).IncludeAncestors);
            Assert.False(QueryShape.Admit(new QueryRequest { IncludeAncestors = true, Aggregate = true, Select = "count()" }, nodes).IncludeAncestors);
            Assert.False(QueryShape.Admit(new QueryRequest { IncludeAncestors = true }, Accounts).IncludeAncestors);
        }
    }
}
