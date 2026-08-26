// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>
    ///     What an expression means is decided here: which column a name resolves to, what type the
    ///     result has, and which mistakes are refused rather than compiled into something that runs
    ///     and returns the wrong rows.
    /// </summary>
    public class BinderTests
    {
        [Theory]
        [InlineData("Amount", "Numeric")]
        [InlineData("Memo", "String")]
        [InlineData("PostingDate", "Date")]
        [InlineData("PostedAt", "DateTimeOffset")]
        [InlineData("ExternalId", "Guid")]
        [InlineData("IsPosted", "Bool")]
        [InlineData("Location", "Geography")]
        [InlineData("Customer.Name", "String")]
        [InlineData("Customer.Region.Name", "String")]
        [InlineData("Count", "Numeric")]
        [InlineData("[not]", "Bool")]
        public void Resolves_a_path_to_the_type_of_the_property_it_ends_at(string text, string type)
        {
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(type, result.Items[0].Type);
        }

        [Fact]
        public void Resolves_a_path_to_descriptors_rather_than_to_the_names_that_were_written()
        {
            // The logical name an author writes and the physical name the backend sees are separate
            // fields that are never interchangeable, which is what keeps a logical name out of SQL.
            ProbeBinding result = QueryexProbe.Bind("Code");

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("DocCode", result.Nodes[0].Physical);
            Assert.Equal(["Code"], result.Nodes[0].Path);
        }

        [Theory]
        [InlineData("Nonsense", "QX3001")]
        [InlineData("Customer.Nonsense", "QX3001")]
        [InlineData("Customer", "QX3002")]
        [InlineData("Customer.Region", "QX3002")]
        [InlineData("nosuchfunction(1)", "QX3003")]
        [InlineData("year()", "QX3004")]
        [InlineData("year(1, 2, 3, 4)", "QX3004")]
        [InlineData("@Undeclared", "QX3007")]
        public void Refuses_a_name_that_resolves_to_nothing(string text, string code)
        {
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Fact]
        public void Points_an_unknown_segment_diagnostic_at_that_segment_alone()
        {
            ProbeBinding result = QueryexProbe.Bind("Customer.Nonsense");

            Assert.Equal(new QueryexSpan(9, 8), result.Diagnostics[0].Span);
        }

        [Theory]
        [InlineData("'2024-01-01' = PostingDate", "Bool")]
        [InlineData("PostingDate = '2024-01-01'", "Bool")]
        [InlineData("cast(Memo, 'date') = PostingDate", "Bool")]
        [InlineData("ExternalId = 'a0eebc99-9c0b-4ef8-bb6d-6bb9bd380a11'", "Bool")]
        [InlineData("IsPosted = IsApproved", "Bool")]
        [InlineData("if(IsPosted, 1, null)", "Numeric")]
        [InlineData("year(PostingDate)", "Numeric")]
        [InlineData("year(local(PostedAt))", "Numeric")]
        [InlineData("addDays(PostedAt, 1)", "DateTimeOffset")]
        [InlineData("addDays(PostingDate, 1)", "Date")]
        [InlineData("coalesce(Rate, 0)", "Numeric")]
        [InlineData("coalesce(null, Amount)", "Numeric")]
        [InlineData("min(PostingDate)", "Date")]
        [InlineData("Memo || 'x'", "String")]
        [InlineData("Amount * 2", "Numeric")]
        [InlineData("-Amount", "Numeric")]
        [InlineData("Amount is null", "Bool")]
        [InlineData("Amount in (1, 2, 3)", "Bool")]
        [InlineData("length(Memo)", "Numeric")]
        [InlineData("contains(Memo, 'x')", "Bool")]
        [InlineData("startOfMonth(PostingDate)", "Date")]
        [InlineData("diffDays(PostingDate, PostingDate)", "Numeric")]
        public void Gives_an_expression_the_type_the_language_says_it_has(string text, string type)
        {
            ProbeBindOptions options = new() { Mode = QueryexMode.Aggregate, HasGroupingKeys = true };
            ProbeBinding result = QueryexProbe.Bind(text, options);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(type, result.Items[0].Type);
        }

        [Theory]
        [InlineData("Memo = PostingDate", "QX3200")]
        [InlineData("now() >= PostingDate", "QX3200")]
        [InlineData("PostingDate = PostedAt", "QX3200")]
        [InlineData("1 || 'a'", "QX3201")]
        [InlineData("Memo + 1", "QX3201")]
        [InlineData("Location = Location", "QX3201")]
        [InlineData("ExternalId < ExternalId", "QX3201")]
        [InlineData("Location in (Location)", "QX3201")]
        [InlineData("not Amount", "QX3201")]
        public void Refuses_operands_the_operator_has_nothing_to_do_with(string text, string code)
        {
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Theory]
        [InlineData("if(1, 2, 3)")]
        [InlineData("diffDays(PostingDate, now())")]
        [InlineData("addDays(PostingDate, Memo)")]
        [InlineData("length(Amount)")]
        [InlineData("round(Memo, 2)")]
        [InlineData("descendantOf(Account.Concept, Location)")]
        public void Refuses_a_call_no_overload_accepts(string text)
        {
            // Told apart from an argument count nothing accepts, because the two mean different
            // things to whoever has to fix it: one is the wrong shape, the other the wrong values.
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX3005");
        }

        [Theory]
        [InlineData("year(PostedAt)")]
        [InlineData("month(PostedAt)")]
        [InlineData("addMonths(PostedAt, 1)")]
        [InlineData("startOfYear(PostedAt)")]
        [InlineData("hour(PostedAt)")]
        [InlineData("diffMonths(PostedAt, PostedAt)")]
        public void Refuses_a_calendar_operation_on_an_instant_that_carries_its_own_offset(string text)
        {
            // The answer would otherwise vary row by row with the stored offset, in a way that is
            // nearly invisible in a report. One compile error with one obvious fix is far better.
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.False(result.Succeeded);
            Assert.Equal("QX3103", result.Diagnostics[0].Code);
        }

        [Theory]
        [InlineData("cast(Memo, 'numeric')", "Numeric")]
        [InlineData("cast(Memo, 'date')", "Date")]
        [InlineData("cast(Amount, 'string')", "String")]
        [InlineData("cast(Amount, 'bool')", "Bool")]
        [InlineData("cast(null, 'numeric')", "Numeric")]
        [InlineData("cast(PostedAt, 'datetime')", "DateTime")]
        public void Converts_between_the_types_the_language_says_it_can(string text, string type)
        {
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(type, result.Items[0].Type);
        }

        [Theory]
        [InlineData("cast(Amount, 'date')", "QX3102")]
        [InlineData("cast(ExternalId, 'numeric')", "QX3102")]
        [InlineData("cast(Memo, 'bool')", "QX3102")]
        [InlineData("cast(Amount, 'nonsense')", "QX3101")]
        [InlineData("cast(Amount, Memo)", "QX3100")]
        [InlineData("year(PostingDate, 'uq')", "QX3101")]
        [InlineData("year(PostingDate, 'et')", "QX3101")]
        [InlineData("year(PostingDate, Memo)", "QX3100")]
        [InlineData("local(PostedAt, 'Nowhere/Nothing')", "QX3101")]
        public void Refuses_a_conversion_or_a_selector_the_language_does_not_offer(string text, string code)
        {
            ProbeBinding result = QueryexProbe.Bind(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Fact]
        public void Resolves_a_zone_identifier_from_its_own_table_rather_than_from_the_machine()
        {
            ProbeBinding result = QueryexProbe.Bind("year(local(PostedAt, 'Africa/Nairobi'))");

            Assert.True(result.Succeeded, Describe(result));
        }

        [Theory]
        [InlineData("Amount", "NotNull")]
        [InlineData("Rate", "Nullable")]
        [InlineData("Centre.Name", "NotNull")]
        [InlineData("Customer.Name", "Nullable")]
        [InlineData("Customer.Region.Name", "Nullable")]
        [InlineData("Centre.Region.Name", "NotNull")]
        [InlineData("1", "NotNull")]
        [InlineData("Amount + Rate", "Nullable")]
        [InlineData("Amount + 1", "NotNull")]
        [InlineData("Memo || 'x'", "Nullable")]
        [InlineData("Amount = Rate", "NotNull")]
        [InlineData("Rate is null", "NotNull")]
        [InlineData("contains(Memo, 'x')", "NotNull")]
        [InlineData("coalesce(Rate, 0)", "NotNull")]
        [InlineData("coalesce(Rate, Rate)", "Nullable")]
        [InlineData("cast(coalesce(null, null), 'numeric')", "Null")]
        [InlineData("if(IsPosted, Amount, 0)", "NotNull")]
        [InlineData("if(IsPosted, Amount, null)", "Nullable")]
        [InlineData("if(true, Amount, null)", "NotNull")]
        [InlineData("if(false, Amount, null)", "Null")]
        [InlineData("today()", "NotNull")]
        [InlineData("now()", "NotNull")]
        [InlineData("me()", "NotNull")]
        [InlineData("upper(Memo)", "Nullable")]
        [InlineData("upper(Code)", "NotNull")]
        [InlineData("-Rate", "Nullable")]
        public void Says_whether_an_expression_can_evaluate_to_an_absent_value(string text, string nullity)
        {
            ProbeBinding result = QueryexProbe.Bind(text, new ProbeBindOptions { Mode = QueryexMode.Value });

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(nullity, result.Items[0].Nullity);
        }

        [Fact]
        public void Makes_the_current_user_provably_absent_when_there_will_not_be_one()
        {
            // Which is what makes a filter comparing against it provably false, so it folds away
            // rather than being evaluated for every row.
            ProbeBinding result = QueryexProbe.Bind("me()", new ProbeBindOptions { HasUser = false });

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Null", result.Items[0].Nullity);
        }

        [Theory]
        [InlineData("sum(Amount)", true, "NotNull")]
        [InlineData("sum(Amount)", false, "Nullable")]
        [InlineData("sum(Rate)", true, "Nullable")]
        [InlineData("sum(Amount, Gender = 'F')", true, "Nullable")]
        [InlineData("count(Amount, Gender = 'F')", true, "NotNull")]
        [InlineData("count()", false, "NotNull")]
        [InlineData("min(PostingDate)", true, "NotNull")]
        [InlineData("avg(Amount)", true, "NotNull")]
        public void Says_whether_an_aggregate_can_evaluate_to_an_absent_value(
            string text,
            bool grouped,
            string nullity)
        {
            // A filtered aggregate over a group where nothing matched, and an unfiltered one over a
            // query that matched nothing, both produce no value at all. Ignoring that would make a
            // negated comparison on a measure silently drop rows.
            ProbeBindOptions options = new()
            {
                Mode = QueryexMode.Aggregate,
                HasGroupingKeys = grouped,
            };

            ProbeBinding result = QueryexProbe.Bind(text, options);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal(nullity, result.Items[0].Nullity);
        }

        [Fact]
        public void Reports_a_declared_parameter_at_the_type_and_nullity_it_was_declared_with()
        {
            ProbeBindOptions options = new()
            {
                Parameters =
                [
                    new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: true),
                ],
            };

            ProbeBinding result = QueryexProbe.Bind("@From", options);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Date", result.Items[0].Type);
            Assert.Equal("NotNull", result.Items[0].Nullity);
        }

        [Fact]
        public void Matches_a_parameter_to_its_declaration_without_regard_to_case()
        {
            ProbeBindOptions options = new()
            {
                Parameters = [new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: false)],
            };

            ProbeBinding result = QueryexProbe.Bind("PostingDate >= @FROM", options);

            Assert.True(result.Succeeded, Describe(result));
        }

        /// <summary>Renders a result's diagnostics, so a failing assertion says what went wrong.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The rendered diagnostics.</returns>
        internal static string Describe(ProbeBinding result)
        {
            return string.Join(
                "; ",
                result.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}@{diagnostic.Span.Start}+{diagnostic.Span.Length}"));
        }
    }
}
