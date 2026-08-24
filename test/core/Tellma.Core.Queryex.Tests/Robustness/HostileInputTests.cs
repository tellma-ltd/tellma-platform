// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Robustness
{
    /// <summary>
    ///     Text a host would never write on purpose, held to the same promise as text it would: a
    ///     diagnostic, never an exception and never a statement the backend refuses.
    /// </summary>
    /// <remarks>
    ///     Every case here was once a way to take the process down or to smuggle an unrunnable
    ///     statement past validation, so each is pinned by the shape of the failure it produces
    ///     rather than only by the fact that something failed.
    /// </remarks>
    public sealed class HostileInputTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>
        ///     A run of one operator is parsed by a loop rather than by recursion, so counting how
        ///     deep the parser went says nothing about how deep the tree it built is — and every
        ///     later pass walks that tree by recursion. Long chains of each associativity, well
        ///     inside every other ceiling.
        /// </summary>
        /// <param name="connective">The connective the chain is built from.</param>
        [Theory]
        [InlineData("or")]
        [InlineData("and")]
        public void LongConnectiveChain_IsRefusedRatherThanWalked(string connective)
        {
            string text = string.Join(" " + connective + " ", Enumerable.Repeat("true", 800));

            Assert.Equal(["QX5003"], Codes(text, QueryexMode.Filter));
        }

        /// <summary>The same, for the chains built from operators rather than connectives.</summary>
        /// <param name="term">One term of the chain.</param>
        /// <param name="separator">What joins the terms.</param>
        [Theory]
        [InlineData("1", " + ")]
        [InlineData("1", " - ")]
        [InlineData("'a'", " || ")]
        public void LongOperatorChain_IsRefusedRatherThanWalked(string term, string separator)
        {
            string text = string.Join(separator, Enumerable.Repeat(term, 805));

            Assert.Equal(["QX5003"], Codes(text, QueryexMode.Value));
        }

        /// <summary>
        ///     Nesting and chaining add up. A chain inside groups is as deep as both together, so
        ///     neither may be measured without the other.
        /// </summary>
        [Fact]
        public void NestingAndChaining_AreCountedTogether()
        {
            const int Groups = 30;
            const int Terms = 40;
            string text = new string('(', Groups)
                + string.Join(" + ", Enumerable.Repeat("1", Terms))
                + new string(')', Groups);

            Assert.Equal(["QX5003"], Codes(text, QueryexMode.Value));
        }

        /// <summary>A chain that fits is still accepted, so the ceiling is a ceiling and not a ban.</summary>
        [Fact]
        public void AChainWithinTheCeiling_IsStillAccepted()
        {
            string text = string.Join(" + ", Enumerable.Repeat("1", 60));

            Assert.Empty(Codes(text, QueryexMode.Value));
        }

        /// <summary>
        ///     An operand that cannot meet what its operator demands is wrong under every overload,
        ///     so the search for one must not swallow the complaint. Left swallowed, the call bound
        ///     with an error inside it and nothing said about it, and what was stored on that
        ///     footing could never afterwards be compiled.
        /// </summary>
        /// <param name="text">The expression.</param>
        [Theory]
        [InlineData("endsWith(-Code, 'x')")]
        [InlineData("startsWith(-Code, 'x')")]
        [InlineData("contains(-Code, 'x')")]
        [InlineData("coalesce(-Code, 'x')")]
        [InlineData("replace(-Code, 'x', 'y')")]
        [InlineData("length(-Code)")]
        [InlineData("trim(-Code)")]
        [InlineData("abs(not Amount)")]
        [InlineData("round(not Amount, 2)")]
        [InlineData("year(-PostingDate)")]
        [InlineData("addDays(-PostingDate, 1)")]
        [InlineData("diffDays(-PostingDate, PostingDate)")]
        public void ABadArgumentInsideACall_IsReportedRatherThanAbsorbed(string text)
        {
            Assert.Equal(["QX3201"], Codes(text, QueryexMode.Value));
        }

        /// <summary>
        ///     What validation accepts must compile. The two run at different times — one when an
        ///     expression is saved, the other whenever it is read — so an expression that passes the
        ///     first and fails the second fails in production and nowhere else.
        /// </summary>
        /// <param name="text">The expression.</param>
        /// <param name="accepted">Whether validation is expected to accept it.</param>
        /// <remarks>
        ///     Each case says which side of the line it is on, so the two stay agreed about
        ///     accepted expressions as well as refused ones. Comparing the two answers alone would
        ///     hold just as well if every case were refused, and a suite of refusals says nothing
        ///     about the direction that actually reaches production.
        /// </remarks>
        [Theory]
        [InlineData("endsWith(-Code, 'x')", false)]
        [InlineData("coalesce(-Code, 'x')", false)]
        [InlineData("abs(not Amount)", false)]
        [InlineData("Amount + 1", true)]
        [InlineData("abs(Amount)", true)]
        [InlineData("coalesce(Memo, 'x')", true)]
        [InlineData("endsWith(Code, 'x')", true)]
        [InlineData("addDays(PostingDate, 1)", true)]
        public void WhatValidationAccepts_Compiles(string text, bool accepted)
        {
            QueryexResult<ValidatedExpression> validated = Engine.Validate(
                text,
                new ValidationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = QueryexMode.Value,
                });

            QueryexResult<CompiledQuery> compiled = Engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = text },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.Equal(accepted, validated.Succeeded);
            Assert.Equal(validated.Succeeded, compiled.Succeeded);
        }

        /// <summary>
        ///     Applying a written offset can carry a moment out of the domain even though the local
        ///     part beside it is inside it, and the platform answers that by throwing.
        /// </summary>
        /// <param name="literal">The written instant.</param>
        [Theory]
        [InlineData("0001-01-01T00:00+01:00")]
        [InlineData("0001-01-01T00:00:00+00:01")]
        [InlineData("0001-01-01T00:00:00+14:00")]
        [InlineData("9999-12-31T23:59-01:00")]
        [InlineData("9999-12-31T23:59:59-01:00")]
        [InlineData("9999-12-31T23:59:59.9999999-00:01")]
        [InlineData("9999-12-31T23:59:59.9999999-14:00")]
        public void AnInstantOutsideTheDomain_IsRefusedRatherThanThrown(string literal)
        {
            Assert.Equal(
                ["QX3200"],
                Codes("PostedAt = '" + literal + "'", QueryexMode.Filter));
        }

        /// <summary>One at each end that is inside the domain, so the check is not simply a ban.</summary>
        /// <param name="literal">The written instant.</param>
        [Theory]
        [InlineData("0001-01-01T00:00:00Z")]
        [InlineData("0001-01-01T15:00:00+14:00")]
        [InlineData("9999-12-31T23:59:59.9999999Z")]
        [InlineData("9999-12-31T09:59:59-14:00")]
        public void AnInstantInsideTheDomain_IsStillRead(string literal)
        {
            Assert.Empty(Codes("PostedAt = '" + literal + "'", QueryexMode.Filter));
        }

        /// <summary>
        ///     Fourteen hours is the whole offset's ceiling and not the hour field's, so an offset
        ///     past it is refused however ordinary the moment it is written against. The platform
        ///     answers such an offset by throwing, which no reader's input may provoke.
        /// </summary>
        /// <param name="literal">The written instant.</param>
        [Theory]
        [InlineData("2024-06-15T12:00+14:30")]
        [InlineData("2024-06-15T12:00:00+14:01")]
        [InlineData("2024-06-15T12:00:00-14:59")]
        [InlineData("2024-06-15T12:00:00+15:00")]
        [InlineData("2024-06-15T12:00:00+23:59")]
        public void AnOffsetPastTheCeiling_IsRefusedRatherThanThrown(string literal)
        {
            Assert.Equal(
                ["QX3200"],
                Codes("PostedAt = '" + literal + "'", QueryexMode.Filter));
        }

        /// <summary>The widest offset any zone runs is still read, so the ceiling is not a ban.</summary>
        /// <param name="literal">The written instant.</param>
        [Theory]
        [InlineData("2024-06-15T12:00+14:00")]
        [InlineData("2024-06-15T12:00:00-14:00")]
        [InlineData("2024-06-15T12:00:00+13:59")]
        public void AnOffsetAtTheCeiling_IsStillRead(string literal)
        {
            Assert.Empty(Codes("PostedAt = '" + literal + "'", QueryexMode.Filter));
        }

        /// <summary>
        ///     Every comma-separated list answers to the list ceiling, not only the outermost one.
        /// </summary>
        /// <param name="text">The expression.</param>
        [Theory]
        [InlineData("Id in (@LIST)")]
        [InlineData("coalesce(@LIST) = 1")]
        public void ALongInnerList_IsRefused(string text)
        {
            string elements = string.Join(", ", Enumerable.Range(0, 500));

            Assert.Equal(
                ["QX5005"],
                Codes(text.Replace("@LIST", elements, StringComparison.Ordinal), QueryexMode.Filter));
        }

        /// <summary>
        ///     Each list is measured on its own, which is what the ceiling says it means. Two lists
        ///     that each fit are not together an overflow.
        /// </summary>
        [Fact]
        public void TwoListsThatEachFit_AreBothAccepted()
        {
            string elements = string.Join(", ", Enumerable.Range(0, 100));

            Assert.Empty(Codes(
                "Id in (" + elements + ") and Id in (" + elements + ")",
                QueryexMode.Filter));
        }

        /// <summary>
        ///     A composed filter is a tree too, and the engine reads it by recursion in several
        ///     places — one of them before it has looked at the caller's limits at all.
        /// </summary>
        [Fact]
        public void ADeeplyNestedFilterTree_IsRefusedWhereItIsBuilt()
        {
            var deepest = FilterTree.Leaf("Id = 1");
            for (int level = 1; level < FilterTree.MaxDepth; level++)
            {
                deepest = FilterTree.Not(deepest);
            }

            Assert.Throws<ArgumentException>(() => FilterTree.Not(deepest));
            Assert.Throws<ArgumentException>(() => FilterTree.And([deepest]));
            Assert.Throws<ArgumentException>(() => FilterTree.Or([deepest]));
        }

        /// <summary>A tree at the ceiling still compiles, so the ceiling is not off by one.</summary>
        [Fact]
        public void AFilterTreeAtTheCeiling_StillCompiles()
        {
            var tree = FilterTree.Leaf("Id = 1");
            for (int level = 1; level < FilterTree.MaxDepth; level++)
            {
                tree = FilterTree.Not(tree);
            }

            QueryexResult<CompiledQuery> result = Engine.CompileQuery(
                new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id", Filter = tree },
                new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                });

            Assert.True(result.Succeeded);
        }

        /// <summary>
        ///     An optional option still has a contract. A null that surfaces deep inside the engine
        ///     reads as an engine defect rather than as the mistake it is.
        /// </summary>
        [Fact]
        public void ANullOption_IsRefusedWhereItIsSet()
        {
            Assert.Throws<ArgumentNullException>(
                () => new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Limits = null!,
                });

            Assert.Throws<ArgumentNullException>(
                () => new QueryCompilationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Parameters = null!,
                });

            Assert.Throws<ArgumentNullException>(() => new DiscoveryOptions { Limits = null! });
        }

        /// <summary>Runs one expression through validation and returns what it complained about.</summary>
        /// <param name="text">The expression.</param>
        /// <param name="mode">The position it is validated for.</param>
        /// <returns>The distinct codes, in the order they were first reported.</returns>
        private static string[] Codes(string text, QueryexMode mode)
        {
            QueryexResult<ValidatedExpression> result = Engine.Validate(
                text,
                new ValidationOptions
                {
                    LanguageVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Root = LedgerFixture.Invoice,
                    Mode = mode,
                });

            return [.. result.Diagnostics.Select(diagnostic => diagnostic.Code).Distinct()];
        }
    }
}
