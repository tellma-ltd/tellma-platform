// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>
    ///     What a diagnostic points at and what it carries, not merely which code it is.
    /// </summary>
    /// <remarks>
    ///     A code alone tells an author that something is wrong somewhere. What makes the report
    ///     usable is the range it underlines and the names it hands the host's message catalogue, and
    ///     neither is checked by asserting the code — a diagnostic pointing at the whole input, or
    ///     carrying no name at all, passes that check unchanged.
    /// </remarks>
    public sealed class DiagnosticShapeTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     Each diagnostic underlines the text it is about — not the clause it was found in, and
        ///     not the whole expression.
        /// </summary>
        /// <param name="text">The expression.</param>
        /// <param name="code">The code it should report.</param>
        /// <param name="underlined">The text the span should cover.</param>
        [Theory]
        [InlineData("Amount + Missing", "QX3001", "Missing")]
        [InlineData("Amount + Customer", "QX3002", "Customer")]
        [InlineData("nosuch(Amount)", "QX3003", "nosuch")]
        [InlineData("abs(1, 2)", "QX3004", "abs(1, 2)")]
        [InlineData("Amount + @Unknown", "QX3007", "@Unknown")]
        [InlineData("not Amount", "QX3201", "Amount")]
        [InlineData("Memo + 1", "QX3201", "Memo")]
        [InlineData("Amount = PostingDate", "QX3200", "Amount")]
        [InlineData("'abc", "QX1001", "'abc")]
        [InlineData("Amount # 1", "QX1005", "#")]
        [InlineData("1e3", "QX1003", "1e3")]
        [InlineData("Amount +", "QX2001", "")]
        [InlineData("(Amount", "QX2002", "")]
        public void ADiagnostic_UnderlinesWhatItIsAbout(string text, string code, string underlined)
        {
            QueryexDiagnostic diagnostic = Only(text, code);

            Assert.Equal(
                underlined,
                text.Substring(
                    Math.Min(diagnostic.Span.Start, text.Length),
                    Math.Min(diagnostic.Span.Length, text.Length - Math.Min(diagnostic.Span.Start, text.Length))));
        }

        /// <summary>
        ///     Each diagnostic hands the host the names its message needs, spelled the way the
        ///     catalogue expects.
        /// </summary>
        /// <param name="text">The expression.</param>
        /// <param name="code">The code it should report.</param>
        /// <param name="key">The argument the message needs.</param>
        /// <param name="value">What that argument should hold.</param>
        [Theory]
        [InlineData("Amount + Missing", "QX3001", "name", "Missing")]
        [InlineData("Amount + Missing", "QX3001", "entity", "Invoice")]
        [InlineData("Amount + Customer", "QX3002", "name", "Customer")]
        [InlineData("Amount + Customer", "QX3002", "entity", "Invoice")]
        [InlineData("nosuch(Amount)", "QX3003", "function", "nosuch")]
        [InlineData("abs(1, 2)", "QX3004", "function", "abs")]
        [InlineData("abs(1, 2)", "QX3004", "actual", "2")]
        [InlineData("Amount + @Unknown", "QX3007", "name", "Unknown")]
        [InlineData("not Amount", "QX3201", "type", "Numeric")]
        [InlineData("Memo + 1", "QX3201", "type", "String")]
        [InlineData("Amount = PostingDate", "QX3200", "type", "Numeric")]
        public void ADiagnostic_CarriesWhatAMessageNeeds(
            string text,
            string code,
            string key,
            string value)
        {
            QueryexDiagnostic diagnostic = Only(text, code);

            Assert.Contains(
                diagnostic.Arguments,
                argument => argument.Key == key && argument.Value == value);
        }

        /// <summary>
        ///     A ceiling says which ceiling it was, because the author has no other way to tell one
        ///     refusal from another.
        /// </summary>
        [Fact]
        public void ACeiling_SaysWhichCeilingItWas()
        {
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                new string('(', 200) + "1" + new string(')', 200),
                new ValidationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            QueryexDiagnostic diagnostic = result.Diagnostics.First(d => d.Code == "QX5003");
            Assert.Contains(diagnostic.Arguments, argument => argument.Key == "limit" && argument.Value == "64");
        }

        /// <summary>
        ///     Every diagnostic names the clause it came from, so a host that compiled four clauses
        ///     can put the report beside the right input box.
        /// </summary>
        [Fact]
        public void EveryDiagnostic_NamesItsClause()
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Missing",
                    Filter = FilterTree.Leaf("AlsoMissing = 1"),
                    OrderBy = "StillMissing",
                },
                new QueryCompilationOptions { Schema = LedgerFixture.Schema });

            Assert.False(result.Succeeded);
            Assert.All(result.Diagnostics, d => Assert.False(string.IsNullOrEmpty(d.Location)));

            Assert.Equal(
                ["Filter", "OrderBy", "Select"],
                [.. result.Diagnostics.Select(d => d.Location!.Split('[')[0]).Distinct().Order(StringComparer.Ordinal)]);
        }

        /// <summary>
        ///     A span always lies inside the text it is about, whatever went wrong. A host highlights
        ///     with these, and one that ran past the end would throw in the host rather than here.
        /// </summary>
        /// <param name="text">The expression.</param>
        [Theory]
        [InlineData("Amount +")]
        [InlineData("(((")]
        [InlineData("'abc")]
        [InlineData("[abc")]
        [InlineData("abs(")]
        [InlineData("Id in (")]
        [InlineData(",,,,")]
        [InlineData("Id = = 1")]
        [InlineData("Amount # 1")]
        [InlineData("Id.")]
        public void ASpan_StaysInsideTheText(string text)
        {
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                text,
                new ValidationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            Assert.NotEmpty(result.Diagnostics);
            Assert.All(result.Diagnostics, diagnostic =>
            {
                Assert.InRange(diagnostic.Span.Start, 0, text.Length);
                Assert.InRange(diagnostic.Span.Length, 0, text.Length - diagnostic.Span.Start);
            });
        }

        /// <summary>Validates an expression and returns the one diagnostic with a given code.</summary>
        /// <param name="text">The expression.</param>
        /// <param name="code">The code.</param>
        /// <returns>The diagnostic.</returns>
        private static QueryexDiagnostic Only(string text, string code)
        {
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                text,
                new ValidationOptions
                {
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            QueryexDiagnostic[] matching = [.. result.Diagnostics.Where(d => d.Code == code)];
            Assert.True(
                matching.Length > 0,
                text + " reported " + string.Join(", ", result.Diagnostics.Select(d => d.Code)));

            return matching[0];
        }
    }
}
