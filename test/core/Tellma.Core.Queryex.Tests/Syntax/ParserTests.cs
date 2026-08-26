// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Syntax
{
    /// <summary>
    ///     Precedence decides what a stored expression means, and getting it wrong changes answers
    ///     without changing anything a reader would notice. Every grouping the language promises is
    ///     asserted here against the fully parenthesised print of the tree that was actually built.
    /// </summary>
    public class ParserTests
    {
        [Theory]
        [InlineData("-a * b", "(-a) * b")]
        [InlineData("a + b * c", "a + (b * c)")]
        [InlineData("a * b + c", "(a * b) + c")]
        [InlineData("not a = b", "not (a = b)")]
        [InlineData("a = b and c = d", "(a = b) and (c = d)")]
        [InlineData("a or b and c", "a or (b and c)")]
        [InlineData("a and b or c", "(a and b) or c")]
        [InlineData("not a and b", "(not a) and b")]
        [InlineData("a - b - c", "(a - b) - c")]
        [InlineData("a || b || c", "(a || b) || c")]
        [InlineData("-a + b", "(-a) + b")]
        [InlineData("a is null and b", "(a is null) and b")]
        [InlineData("not a is null", "not (a is null)")]
        [InlineData("a in (1, 2) or b", "(a in (1, 2)) or b")]
        [InlineData("a + b in (1)", "(a + b) in (1)")]
        public void Groups_operators_the_way_the_language_promises(string text, string grouped)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal(grouped, result.ExplicitText);
        }

        [Theory]
        [InlineData("Notes", "Path")]
        [InlineData("count", "Path")]
        [InlineData("[not]", "Path")]
        [InlineData("a.b.c", "Path")]
        [InlineData("count()", "Call")]
        [InlineData("today()", "Call")]
        public void Tells_a_call_from_a_path_by_the_parenthesis_alone(string text, string kind)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal(kind, result.Nodes[2].Kind);
        }

        [Fact]
        public void Reads_a_zero_argument_call()
        {
            ProbeSyntax result = QueryexProbe.Parse("count()");

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal("count", result.Nodes[2].Detail);
            Assert.Empty(result.Nodes[2].Children);
        }

        [Fact]
        public void Keeps_a_segments_own_range_so_a_diagnostic_can_point_at_it()
        {
            ProbeSyntax result = QueryexProbe.Parse("Customer.Region.Name");

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal("Customer.Region.Name", result.Nodes[2].Detail);
        }

        [Theory]
        [InlineData("a = b = c", "QX2006")]
        [InlineData("a < b < c", "QX2006")]
        [InlineData("a = b is null", "QX2006")]
        [InlineData("a in (1) = b", "QX2006")]
        [InlineData("a..b", "QX2001")]
        [InlineData("a.", "QX2001")]
        [InlineData("a +", "QX2001")]
        [InlineData("*", "QX2001")]
        [InlineData("(a", "QX2002")]
        [InlineData("f(a", "QX2002")]
        [InlineData(",a", "QX2003")]
        [InlineData("a,", "QX2003")]
        [InlineData("a,,b", "QX2003")]
        [InlineData("", "QX2003")]
        [InlineData("f(1,)", "QX2004")]
        [InlineData("f(1,,2)", "QX2004")]
        [InlineData("f(,1)", "QX2004")]
        [InlineData("()", "QX2005")]
        [InlineData("a in ()", "QX2005")]
        [InlineData("(a asc)", "QX2008")]
        [InlineData("a asc + 1", "QX2008")]
        [InlineData("f(a asc)", "QX2008")]
        public void Rejects_a_shape_the_grammar_does_not_allow(string text, string code)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Fact]
        public void Reports_one_problem_per_item_rather_than_one_in_total()
        {
            // Recovery to the next separator is what makes a list of stored expressions fixable in
            // one pass; without it the second mistake stays hidden behind the first.
            ProbeSyntax result = QueryexProbe.Parse("a = = b, c, d = = e");

            Assert.Equal(2, result.Diagnostics.Count);
        }

        [Fact]
        public void Reads_a_direction_suffix_on_an_item()
        {
            ProbeSyntax result = QueryexProbe.Parse("a desc, b asc, c");

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal("Descending", result.Nodes[1].Detail);
        }

        [Fact]
        public void Collects_every_parameter_the_input_mentions_once()
        {
            ProbeSyntax result = QueryexProbe.Parse("@From <= d and d <= @to and @FROM != @x");

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal(["From", "to", "x"], result.ReferencedParameters);
        }

        [Fact]
        public void Refuses_to_nest_more_deeply_than_the_call_site_permits()
        {
            QueryexLimits limits = QueryexLimits.Default with { MaxSyntaxDepth = 8 };

            ProbeSyntax result = QueryexProbe.Parse(new string('(', 40) + "a" + new string(')', 40), limits);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX5003");
        }

        [Fact]
        public void Refuses_more_items_than_the_call_site_permits()
        {
            QueryexLimits limits = QueryexLimits.Default with { MaxListItems = 3 };

            ProbeSyntax result = QueryexProbe.Parse("a, b, c, d, e", limits);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "QX5005");
        }

        [Fact]
        public void Survives_a_deeply_nested_input_without_running_out_of_stack()
        {
            // The nesting ceiling is what keeps a recursive parser off the end of its stack, so it
            // has to be reached rather than crashed into. Kept under the token ceiling so that the
            // nesting ceiling is what stops it, rather than the count of parentheses.
            ProbeSyntax result = QueryexProbe.Parse(new string('(', 2000) + "a" + new string(')', 2000));

            Assert.False(result.Succeeded);
            Assert.Equal("QX5003", result.Diagnostics[0].Code);
        }

        [Fact]
        public void Reports_the_ceiling_that_was_reached_and_nothing_downstream_of_it()
        {
            // An input past a ceiling has to report the ceiling, not a cascade of grammar
            // complaints from the parser unwinding through everything it had already opened.
            ProbeSyntax result = QueryexProbe.Parse(new string('(', 4000) + "a" + new string(')', 4000));

            Assert.False(result.Succeeded);
            Assert.Single(result.Diagnostics);
            Assert.Equal("QX5002", result.Diagnostics[0].Code);
        }

        [Theory]
        [InlineData("a")]
        [InlineData("-a * b")]
        [InlineData("not a = b")]
        [InlineData("a or b and c")]
        [InlineData("a.b.c + f(1, 'x', @p)")]
        [InlineData("x in (1, 2, 3)")]
        [InlineData("x is not null")]
        [InlineData("[not] = [and]")]
        [InlineData("'It''s fine' || 'x'")]
        [InlineData("0.500 + 1")]
        [InlineData("a desc, b asc, c")]
        [InlineData("((((a))))")]
        [InlineData("f()")]
        public void Printing_a_parse_and_reparsing_it_lands_on_the_same_tree(string text)
        {
            ProbeSyntax first = QueryexProbe.Parse(text);
            Assert.True(first.Succeeded, LexerTests.Describe(first));

            Assert.True(
                QueryexProbe.AreEquivalent(text, first.ExplicitText),
                $"'{text}' printed as '{first.ExplicitText}'");

            Assert.True(
                QueryexProbe.AreEquivalent(text, first.CanonicalText),
                $"'{text}' printed as '{first.CanonicalText}'");
        }

        [Theory]
        [InlineData("((((a))))", "a")]
        [InlineData("a + (b)", "a + b")]
        [InlineData("(a + b) * c", "(a + b) * c")]
        [InlineData("a + (b * c)", "a + b * c")]
        [InlineData("NOT A", "not A")]
        [InlineData("x IS NOT NULL", "x is not null")]
        [InlineData("[Notes]", "Notes")]
        [InlineData("[not]", "[not]")]
        public void Canonical_printing_drops_what_is_redundant_and_keeps_what_is_not(
            string text,
            string canonical)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.True(result.Succeeded, LexerTests.Describe(result));
            Assert.Equal(canonical, result.CanonicalText);
        }

        [Theory]
        [InlineData("a + b * c - -d")]
        [InlineData("not (a or b) and c is null")]
        [InlineData("f(g(h(1)), 'x') in (1, 2)")]
        public void Canonical_printing_is_idempotent(string text)
        {
            string once = QueryexProbe.Parse(text).CanonicalText;
            string twice = QueryexProbe.Parse(once).CanonicalText;

            Assert.Equal(once, twice);
        }
    }
}
