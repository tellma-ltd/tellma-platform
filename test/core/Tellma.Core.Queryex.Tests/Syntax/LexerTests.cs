// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Probe;

namespace Tellma.Core.Queryex.Tests.Syntax
{
    /// <summary>
    ///     The lexical layer decides what a stored expression even means before anything else looks
    ///     at it, so every shape it accepts and every shape it turns away are pinned here.
    /// </summary>
    public class LexerTests
    {
        [Theory]
        [InlineData("Notes", "Identifier")]
        [InlineData("Ordering", "Identifier")]
        [InlineData("Internal", "Identifier")]
        [InlineData("count", "Identifier")]
        [InlineData("and", "And")]
        [InlineData("AND", "And")]
        [InlineData("Not", "Not")]
        [InlineData("null", "Null")]
        [InlineData("desc", "Desc")]
        public void Classifies_an_identifier_by_looking_it_up_rather_than_by_its_shape(string text, string kind)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.Equal(kind, result.Tokens[0].Kind);
        }

        [Fact]
        public void Brackets_escape_the_reserved_words()
        {
            ProbeSyntax result = QueryexProbe.Parse("[not]");

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Identifier", result.Tokens[0].Kind);
            Assert.Equal("not", result.Tokens[0].Text);
        }

        [Theory]
        [InlineData("''", "")]
        [InlineData("'Hello'", "Hello")]
        [InlineData("'It''s fine'", "It's fine")]
        [InlineData(@"'a\b'", @"a\b")]
        public void Reads_a_string_literal_with_only_the_doubled_quote_as_an_escape(string text, string value)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("String", result.Tokens[0].Kind);
            Assert.Equal(value, result.Tokens[0].Text);
        }

        [Theory]
        [InlineData("1", "1")]
        [InlineData("0", "0")]
        [InlineData("3.14", "3.14")]
        [InlineData("0.500", "0.500")]
        public void Reads_a_numeric_literal_and_keeps_the_scale_it_was_written_with(string text, string value)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Number", result.Tokens[0].Kind);
            Assert.Equal(value, result.Tokens[0].Text);
        }

        [Fact]
        public void Accepts_a_literal_at_the_full_supported_precision()
        {
            ProbeSyntax result = QueryexProbe.Parse(new string('9', 38));

            Assert.True(result.Succeeded, Describe(result));
        }

        [Theory]
        [InlineData("'unterminated", "QX1001")]
        [InlineData("[unterminated", "QX1002")]
        [InlineData(".5", "QX1003")]
        [InlineData("5.", "QX1003")]
        [InlineData("1e3", "QX1003")]
        [InlineData("0x0A", "QX1003")]
        [InlineData("1_000", "QX1003")]
        [InlineData("[]", "QX1005")]
        [InlineData("#", "QX1005")]
        [InlineData("!", "QX1005")]
        [InlineData("|", "QX1005")]
        [InlineData("@", "QX1005")]
        [InlineData("@ 1", "QX1005")]
        public void Rejects_a_malformed_token(string text, string code)
        {
            ProbeSyntax result = QueryexProbe.Parse(text);

            Assert.False(result.Succeeded);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Fact]
        public void Rejects_a_literal_past_the_supported_precision()
        {
            ProbeSyntax result = QueryexProbe.Parse(new string('9', 39));

            Assert.False(result.Succeeded);
            Assert.Equal("QX1004", result.Diagnostics[0].Code);
            Assert.Equal(new QueryexSpan(0, 39), result.Diagnostics[0].Span);
        }

        [Fact]
        public void Reports_every_unexpected_character_rather_than_only_the_first()
        {
            // Skipping the character and carrying on is what lets someone fix a stored expression in
            // one pass instead of one character per attempt.
            ProbeSyntax result = QueryexProbe.Parse("a # b # c");

            Assert.Equal(2, result.Diagnostics.Count(diagnostic => diagnostic.Code == "QX1005"));
        }

        [Fact]
        public void Spans_an_unterminated_string_from_its_opening_quote_to_the_end()
        {
            ProbeSyntax result = QueryexProbe.Parse("a = 'oops");

            Assert.Equal(new QueryexSpan(4, 5), result.Diagnostics[0].Span);
        }

        [Fact]
        public void Reads_a_parameter_as_one_token()
        {
            ProbeSyntax result = QueryexProbe.Parse("@From");

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Parameter", result.Tokens[0].Kind);
            Assert.Equal("From", result.Tokens[0].Text);
            Assert.Equal(new QueryexSpan(0, 5), new QueryexSpan(result.Tokens[0].Start, result.Tokens[0].Length));
        }

        [Fact]
        public void Reads_a_letter_outside_the_basic_plane_as_an_identifier()
        {
            // Decoded as a rune rather than a single character, so a supplementary-plane letter is a
            // name rather than two unexpected characters.
            ProbeSyntax result = QueryexProbe.Parse("\U00020000x");

            Assert.True(result.Succeeded, Describe(result));
            Assert.Equal("Identifier", result.Tokens[0].Kind);
        }

        [Fact]
        public void Rejects_a_lone_surrogate_without_throwing()
        {
            ProbeSyntax result = QueryexProbe.Parse("\ud800");

            Assert.False(result.Succeeded);
            Assert.Equal("QX1005", result.Diagnostics[0].Code);
        }

        [Theory]
        [InlineData("!=")]
        [InlineData("<=")]
        [InlineData(">=")]
        [InlineData("||")]
        public void Prefers_the_longer_operator_when_both_would_match(string text)
        {
            ProbeSyntax result = QueryexProbe.Parse("a " + text + " b");

            Assert.Equal(2, result.Tokens[1].Length);
        }

        [Fact]
        public void Refuses_an_input_longer_than_the_call_site_permits()
        {
            QueryexLimits limits = QueryexLimits.Default with { MaxInputLength = 8 };

            ProbeSyntax result = QueryexProbe.Parse("aaaaaaaaaaaa", limits);

            Assert.False(result.Succeeded);
            Assert.Equal("QX5001", result.Diagnostics[0].Code);
        }

        [Fact]
        public void Refuses_more_tokens_than_the_call_site_permits()
        {
            QueryexLimits limits = QueryexLimits.Default with { MaxTokens = 3 };

            ProbeSyntax result = QueryexProbe.Parse("a + b + c", limits);

            Assert.False(result.Succeeded);
            Assert.Equal("QX5002", result.Diagnostics[0].Code);
        }

        /// <summary>Renders a result's diagnostics, so a failing assertion says what went wrong.</summary>
        /// <param name="result">The result.</param>
        /// <returns>The rendered diagnostics.</returns>
        internal static string Describe(ProbeSyntax result)
        {
            return string.Join(
                "; ",
                result.Diagnostics.Select(static diagnostic =>
                    $"{diagnostic.Code}@{diagnostic.Span.Start}+{diagnostic.Span.Length}"));
        }
    }
}
