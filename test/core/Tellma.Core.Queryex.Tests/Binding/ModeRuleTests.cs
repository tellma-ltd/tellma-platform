// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>
    ///     The position an expression occupies decides what it is allowed to be. Every rule here
    ///     follows from one of two independent facts about that position — whether the result has to
    ///     be a truth value, and whether the expression sees rows or groups — and none of them from
    ///     the combination.
    /// </summary>
    public class ModeRuleTests
    {
        [Fact]
        public void A_predicate_position_insists_on_a_truth_value()
        {
            ProbeBinding result = QueryexProbe.Bind("Amount", Mode(QueryexMode.Filter));

            Assert.False(result.Succeeded);
            Assert.Equal("QX4001", result.Diagnostics[0].Code);
        }

        [Fact]
        public void A_predicate_position_takes_one_expression_rather_than_a_list()
        {
            ProbeBinding result = QueryexProbe.Bind("IsPosted, IsApproved", Mode(QueryexMode.Filter));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX2009");
        }

        [Theory]
        [InlineData("count()")]
        [InlineData("sum(Amount)")]
        [InlineData("Amount + sum(Amount)")]
        public void A_row_position_refuses_an_aggregation(string text)
        {
            ProbeBinding result = QueryexProbe.Bind(text, Mode(QueryexMode.Value));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX4002");
        }

        [Theory]
        [InlineData("sum(count())")]
        [InlineData("sum(Amount + max(Amount))")]
        public void An_aggregation_cannot_contain_another_one(string text)
        {
            ProbeBinding result = QueryexProbe.Bind(text, Mode(QueryexMode.Aggregate));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX4003");
        }

        [Fact]
        public void A_group_level_predicate_cannot_read_a_column_from_outside_an_aggregation()
        {
            // Whoever wrote it meant either a grouping key or the row-level filter, and there is no
            // grouping under which the thing they wrote is well defined.
            ProbeBinding result = QueryexProbe.Bind("Amount > 1", Mode(QueryexMode.AggregateFilter));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX4004");
        }

        [Fact]
        public void A_group_level_predicate_over_aggregations_is_fine()
        {
            ProbeBinding result = QueryexProbe.Bind(
                "sum(Amount) > 100 and count() > 2",
                Mode(QueryexMode.AggregateFilter));

            Assert.True(result.Succeeded, BinderTests.Describe(result));
        }

        [Theory]
        [InlineData("Amount * sum(Amount)")]
        [InlineData("if(IsPosted, sum(Amount), Amount)")]
        public void An_item_cannot_read_columns_both_inside_and_outside_an_aggregation(string text)
        {
            // Neither a grouping key nor a measure: reading a column beside an aggregate over the
            // same rows has no meaning under any grouping.
            ProbeBinding result = QueryexProbe.Bind(text, Mode(QueryexMode.Aggregate));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX4007");
        }

        [Theory]
        [InlineData("year(PostingDate), sum(Amount)")]
        [InlineData("Customer.Name, count()")]
        [InlineData("sum(Amount) / count()")]
        [InlineData("1, sum(Amount)")]
        public void A_grouped_select_list_may_mix_keys_and_measures(string text)
        {
            ProbeBinding result = QueryexProbe.Bind(text, Mode(QueryexMode.Aggregate));

            Assert.True(result.Succeeded, BinderTests.Describe(result));
        }

        [Fact]
        public void A_grouping_key_has_to_be_something_the_backend_can_group_by()
        {
            ProbeBinding result = QueryexProbe.Bind("Location", Mode(QueryexMode.Aggregate));

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX3201");
        }

        [Fact]
        public void An_item_whose_only_possible_value_is_absent_has_no_column_type()
        {
            ProbeBinding result = QueryexProbe.Bind("null", Mode(QueryexMode.Value));

            Assert.False(result.Succeeded);
            Assert.Equal("QX3202", result.Diagnostics[0].Code);
        }

        [Fact]
        public void Casting_the_absent_value_is_how_it_gets_a_column_type()
        {
            ProbeBinding result = QueryexProbe.Bind("cast(null, 'numeric')", Mode(QueryexMode.Value));

            Assert.True(result.Succeeded, BinderTests.Describe(result));
            Assert.Equal("Null", result.Items[0].Nullity);
        }

        [Fact]
        public void A_direction_is_refused_where_the_position_has_no_ordering()
        {
            ProbeBinding result = QueryexProbe.Bind("Amount desc", Mode(QueryexMode.Value));

            Assert.False(result.Succeeded);
            Assert.Equal("QX2007", result.Diagnostics[0].Code);
        }

        [Fact]
        public void An_ordering_term_has_to_be_something_with_an_order()
        {
            ProbeBindOptions options = new() { Mode = QueryexMode.Value, Directions = true };

            ProbeBinding result = QueryexProbe.Bind("ExternalId asc", options);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX3201");
        }

        [Fact]
        public void An_ordering_list_reads_its_directions()
        {
            ProbeBindOptions options = new() { Mode = QueryexMode.Value, Directions = true };

            ProbeBinding result = QueryexProbe.Bind("Amount desc, PostingDate", options);

            Assert.True(result.Succeeded, BinderTests.Describe(result));
            Assert.Equal("Descending", result.Items[0].Direction);
            Assert.Equal("None", result.Items[1].Direction);
        }

        [Theory]
        [InlineData("descendantOf(Account.Concept, 'Assets')", "Invoice")]
        [InlineData("descendantOf(Account.Concept, 'Assets', 'Equity')", "Invoice")]
        [InlineData("ancestorOf(Id, 1, 2, 3)", "Account")]
        [InlineData("ancestorOf(Concept, 'Assets')", "Account")]
        [InlineData("not descendantOf(Account.Concept, 'Assets')", "Invoice")]
        public void Accepts_a_hierarchy_predicate_that_can_actually_look_a_node_up(string text, string root)
        {
            ProbeBindOptions options = new() { Mode = QueryexMode.Filter, Root = root };

            ProbeBinding result = QueryexProbe.Bind(text, options);

            Assert.True(result.Succeeded, BinderTests.Describe(result));
        }

        [Theory]
        [InlineData("descendantOf(1, 1)", "Invoice", "QX3300")]
        [InlineData("descendantOf(upper(Code), 'x')", "Invoice", "QX3300")]
        [InlineData("descendantOf(Amount, 1)", "Invoice", "QX3301")]
        [InlineData("descendantOf(Customer.Name, 'x')", "Invoice", "QX3301")]
        [InlineData("descendantOf(Account.Label, 'x')", "Invoice", "QX3302")]
        [InlineData("descendantOf(Account.Concept, Memo)", "Invoice", "QX3303")]
        [InlineData("descendantOf(Account.Concept, upper(Memo))", "Invoice", "QX3303")]
        public void Refuses_a_hierarchy_predicate_that_could_not_mean_one_thing(
            string text,
            string root,
            string code)
        {
            ProbeBindOptions options = new() { Mode = QueryexMode.Filter, Root = root };

            ProbeBinding result = QueryexProbe.Bind(text, options);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        /// <summary>Options that bind in one position with everything else left alone.</summary>
        /// <param name="mode">The position.</param>
        /// <returns>The options.</returns>
        private static ProbeBindOptions Mode(QueryexMode mode)
        {
            return new ProbeBindOptions { Mode = mode };
        }
    }
}
