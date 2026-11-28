// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>The row-level predicate's top-level restrictions, as discovery reports them.</summary>
    public sealed class ConjunctTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Discovery against the fixture schema, from invoices.</summary>
        private static DiscoveryOptions Options => new()
        {
            Schema = LedgerFixture.Schema,
            Root = LedgerFixture.Invoice,
        };

        /// <summary>
        ///     Each comparison operator reads as itself with the literal on the right, and mirrored
        ///     with it on the left, so a restriction always reads from the compared side.
        /// </summary>
        /// <param name="text">The leaf.</param>
        /// <param name="op">The operator expected.</param>
        [Theory]
        [InlineData("Amount = 5", FilterConjunctOperator.Equal)]
        [InlineData("5 = Amount", FilterConjunctOperator.Equal)]
        [InlineData("Amount != 5", FilterConjunctOperator.NotEqual)]
        [InlineData("5 != Amount", FilterConjunctOperator.NotEqual)]
        [InlineData("Amount < 5", FilterConjunctOperator.Less)]
        [InlineData("5 < Amount", FilterConjunctOperator.Greater)]
        [InlineData("Amount <= 5", FilterConjunctOperator.LessOrEqual)]
        [InlineData("5 <= Amount", FilterConjunctOperator.GreaterOrEqual)]
        [InlineData("Amount > 5", FilterConjunctOperator.Greater)]
        [InlineData("5 > Amount", FilterConjunctOperator.Less)]
        [InlineData("Amount >= 5", FilterConjunctOperator.GreaterOrEqual)]
        [InlineData("5 >= Amount", FilterConjunctOperator.LessOrEqual)]
        public void EachOperator_ReadsFromTheComparedSide(string text, FilterConjunctOperator op)
        {
            FilterConjunct conjunct = Assert.Single(Conjuncts(FilterTree.Leaf(text)));

            Assert.Equal(["Amount"], conjunct.Path);
            Assert.Null(conjunct.Expression);
            Assert.Equal(op, conjunct.Operator);
            Assert.Equal(5m, conjunct.Value);
            Assert.Equal(QueryexType.QxNumeric, conjunct.Type);
        }

        /// <summary>Each literal form reads as the CLR value of its type.</summary>
        /// <param name="text">The leaf.</param>
        /// <param name="path">The compared path, dotted.</param>
        /// <param name="type">The compared side's type.</param>
        /// <param name="expected">The value, by a name the switch below reads.</param>
        [Theory]
        [InlineData("CustomerId = 1012", "CustomerId", QueryexType.QxNumeric, "1012")]
        [InlineData("Amount > -12.50", "Amount", QueryexType.QxNumeric, "-12.50")]
        [InlineData("Amount > (-(12.5))", "Amount", QueryexType.QxNumeric, "-12.5")]
        [InlineData("Code = 'O''Brien'", "Code", QueryexType.QxString, "O'Brien")]
        [InlineData("IsPosted = true", "IsPosted", QueryexType.QxBool, "true")]
        [InlineData("false = IsApproved", "IsApproved", QueryexType.QxBool, "false")]
        [InlineData("PostingDate >= cast('2026-07-01', 'date')", "PostingDate", QueryexType.QxDate, "date")]
        [InlineData("PostingDate >= '2026-07-01'", "PostingDate", QueryexType.QxDate, "date")]
        [InlineData("PostedOn < cast('2026-07-01T10:30:00.5', 'datetime')", "PostedOn", QueryexType.QxDateTime, "datetime")]
        [InlineData("ApprovedAt >= cast('2026-07-01T10:30:00+03:00', 'datetimeoffset')", "ApprovedAt", QueryexType.QxDateTimeOffset, "datetimeoffset")]
        [InlineData("ExternalId = cast('0f8fad5b-d9cb-469f-a165-70867728950e', 'guid')", "ExternalId", QueryexType.QxGuid, "guid")]
        [InlineData("ExternalId = '0F8FAD5B-D9CB-469F-A165-70867728950E'", "ExternalId", QueryexType.QxGuid, "guid")]
        [InlineData("Memo = cast('x', 'string')", "Memo", QueryexType.QxString, "x")]
        [InlineData("CustomerId = null", "CustomerId", QueryexType.QxNumeric, "null")]
        [InlineData("PostingDate != cast(null, 'date')", "PostingDate", QueryexType.QxDate, "null")]
        [InlineData("Customer.Name = 'Acme'", "Customer.Name", QueryexType.QxString, "Acme")]

        // The widest numbers a decimal holds: every digit of its 96-bit magnitude, at the least
        // and the most scale.
        [InlineData("Amount = 79228162514264337593543950335", "Amount", QueryexType.QxNumeric, "decimal-max")]
        [InlineData("Amount = -7.9228162514264337593543950335", "Amount", QueryexType.QxNumeric, "decimal-max-scaled")]
        public void EachLiteralForm_ReadsAsItsValue(string text, string path, QueryexType type, string expected)
        {
            FilterConjunct conjunct = Assert.Single(Conjuncts(FilterTree.Leaf(text)));

            Assert.Equal(path, string.Join('.', conjunct.Path!));
            Assert.Equal(type, conjunct.Type);
            Assert.Equal(Expected(expected), conjunct.Value);
        }

        /// <summary>
        ///     Absence and presence tests read as equality and inequality with a null value, and a
        ///     hierarchy predicate with one literal key as a restriction of its own.
        /// </summary>
        [Fact]
        public void AbsenceAndHierarchy_ReadAsTheirOwnOperators()
        {
            IReadOnlyList<FilterConjunct> conjuncts = Conjuncts(FilterTree.Leaf(
                "CustomerId is null and DueDate is not null and descendantOf(Account.Concept, 'Assets')"
                    + " and descendantOf(Account.Concept, 'Assets', 'Equity') and ancestorOf(Account.Concept, 'Cash')"));

            Assert.Equal(3, conjuncts.Count);
            Assert.Equal((FilterConjunctOperator.Equal, null), (conjuncts[0].Operator, conjuncts[0].Value));
            Assert.Equal(["CustomerId"], conjuncts[0].Path);
            Assert.Equal((FilterConjunctOperator.NotEqual, null), (conjuncts[1].Operator, conjuncts[1].Value));
            Assert.Equal(QueryexType.QxDate, conjuncts[1].Type);
            Assert.Equal((FilterConjunctOperator.DescendantOf, (object?)"Assets"), (conjuncts[2].Operator, conjuncts[2].Value));
            Assert.Equal(["Account", "Concept"], conjuncts[2].Path);
            Assert.Equal(QueryexType.QxString, conjuncts[2].Type);
        }

        /// <summary>
        ///     The worked example: three restrictions, one of them on an expression printed
        ///     canonically, and none from the disjunction.
        /// </summary>
        [Fact]
        public void TheExample_YieldsThreeConjuncts()
        {
            const string Text = "CustomerId = 1012 and PostingDate >= cast('2026-07-01', 'date')"
                + " and (Amount * 2) > 100 and (Code = 'East' or Code = 'West')";

            IReadOnlyList<FilterConjunct> conjuncts = Conjuncts(FilterTree.Leaf(Text));

            Assert.Collection(
                conjuncts,
                first =>
                {
                    Assert.Equal(["CustomerId"], first.Path);
                    Assert.Equal((FilterConjunctOperator.Equal, (object?)1012m), (first.Operator, first.Value));
                    Assert.Equal(new QueryexTextSite("Filter", new QueryexSpan(0, 17)), first.Site);
                },
                second =>
                {
                    Assert.Equal(["PostingDate"], second.Path);
                    Assert.Equal(
                        (FilterConjunctOperator.GreaterOrEqual, (object?)new DateOnly(2026, 7, 1)),
                        (second.Operator, second.Value));
                },
                third =>
                {
                    Assert.Null(third.Path);
                    Assert.Equal("Amount * 2", third.Expression);
                    Assert.Equal((FilterConjunctOperator.Greater, (object?)100m), (third.Operator, third.Value));
                    Assert.Equal(QueryexType.QxNumeric, third.Type);
                    Assert.Equal(Text.IndexOf("(Amount", StringComparison.Ordinal), third.Site.Span.Start);
                });
        }

        /// <summary>An expression, grouped or not, is printed canonically.</summary>
        /// <param name="text">The leaf.</param>
        /// <param name="expression">The expression's text.</param>
        [Theory]
        [InlineData("((quarter(PostingDate))) = 3", "quarter(PostingDate)")]
        [InlineData("upper( Code ) = 'INV-1'", "upper(Code)")]
        [InlineData("(Amount + Rate) * 2 <= 7", "(Amount + Rate) * 2")]
        [InlineData("3 < Amount  -  1", "Amount - 1")]
        [InlineData("year(PostingDate) is null", "year(PostingDate)")]
        public void AnExpression_IsPrintedCanonically(string text, string expression)
        {
            FilterConjunct conjunct = Assert.Single(Conjuncts(FilterTree.Leaf(text)));

            Assert.Null(conjunct.Path);
            Assert.Equal(expression, conjunct.Expression);
        }

        /// <summary>Atoms that restrict nothing every row must satisfy, or that compare with no literal, yield none.</summary>
        /// <param name="text">The leaf.</param>
        [Theory]
        [InlineData("Amount > 0 or Amount < -5")]
        [InlineData("not (Amount > 0)")]
        [InlineData("not Amount > 0")]
        [InlineData("true")]
        [InlineData("IsPosted")]
        [InlineData("Amount > @floor")]
        [InlineData("@floor < Amount")]
        [InlineData("Amount > Rate")]
        [InlineData("1 = 1")]
        [InlineData("Amount in (1, 2)")]
        [InlineData("contains(Memo, 'x')")]
        [InlineData("descendantOf(Account.Concept, Memo)")]
        [InlineData("ancestorOf(Account.Concept, 'Cash')")]
        [InlineData("PostingDate >= cast(Notes, 'date')")]
        [InlineData("Amount > cast('12', 'numeric')")]
        [InlineData("Nonesuch = 1")]

        // Numbers a decimal cannot hold exactly: a scale above 28, and magnitudes wider than 96
        // bits, whether or not the value itself is within a decimal's range.
        [InlineData("Amount = 0.00000000000000000000000000001")]
        [InlineData("Amount = 99.999999999999999999999999999")]
        [InlineData("Amount = 12345678901234567890.1234567890")]
        [InlineData("Amount = 79228162514264337593543950336")]
        [InlineData("Amount = -12345678901234567890123456789012345678")]

        // An ordering or a subtree measured against the absent value admits no row.
        [InlineData("Amount < null")]
        [InlineData("Amount >= cast(null, 'numeric')")]
        [InlineData("null > Amount")]
        [InlineData("descendantOf(Account.Concept, null)")]
        public void AnAtomThatIsNotARestriction_YieldsNone(string text)
        {
            Assert.Empty(Conjuncts(FilterTree.Leaf(text)));
        }

        /// <summary>An atom that does not bind yields none, and takes nothing else in its leaf with it.</summary>
        [Fact]
        public void AnAtomThatDoesNotBind_YieldsNoneOfItsOwn()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "Id",
                    Filter = FilterTree.Leaf("Amount > 0 and Nonesuch = 1 and Code = upper(1)"),
                    Conjuncts = true,
                },
                Options);

            Assert.NotEmpty(result.Diagnostics);
            FilterConjunct conjunct = Assert.Single(result.Conjuncts!);
            Assert.Equal(["Amount"], conjunct.Path);
        }

        /// <summary>
        ///     Atoms under a disjunction, a negation, a rebase or a semi-join of the tree yield none;
        ///     conjunctions pass them through, and the sites order by leaf and then by span.
        /// </summary>
        [Fact]
        public void TheTree_PassesOnlyConjunctionsThrough()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "CustomerId, sum(Amount)",
                    Aggregate = true,
                    Filter = FilterTree.And(
                    [
                        FilterTree.Leaf("IsPosted = true and Amount > 0"),
                        FilterTree.Or([FilterTree.Leaf("Code = 'A'"), FilterTree.Leaf("Code = 'B'")]),
                        FilterTree.Not(FilterTree.Leaf("Code = 'C'")),
                        FilterTree.Via("Customer", FilterTree.Leaf("Name = 'Acme'")),
                        FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 0")),
                        FilterTree.And([FilterTree.Leaf("Code != 'D'")]),
                    ]),
                    Having = FilterTree.Leaf("sum(Amount) > 0"),
                    Conjuncts = true,
                },
                Options);

            Assert.Empty(result.Diagnostics);
            Assert.NotNull(result.Conjuncts);
            Assert.Equal(
                ["Filter.And[0]@0 IsPosted", "Filter.And[0]@20 Amount", "Filter.And[5].And[0]@0 Code"],
                result.Conjuncts.Select(c => c.Site.Location + "@"
                    + c.Site.Span.Start.ToString(CultureInfo.InvariantCulture) + " " + string.Join('.', c.Path!)));

            // The rebase's path is still reported among the paths, behind its navigation; the
            // semi-join's is not.
            Assert.Contains(result.Paths, p => string.Join('.', p.Segments) == "Customer.Name");
            Assert.DoesNotContain(result.Paths, p => p.Site.Location!.Contains("Exists", StringComparison.Ordinal));
        }

        /// <summary>A single leaf is read too, and its site names the clause.</summary>
        [Fact]
        public void ALeafAtTheTop_IsReadWithTheClauseAsItsLocation()
        {
            FilterConjunct conjunct = Assert.Single(Conjuncts(FilterTree.Leaf("Amount > 0")));

            Assert.Equal(new QueryexTextSite("Filter", new QueryexSpan(0, 10)), conjunct.Site);
        }

        /// <summary>Not asked for, nothing is reported and nothing else changes.</summary>
        [Fact]
        public void NotAskedFor_DiscoveryAnswersAsBefore()
        {
            QueryDiscoverySpec spec = new()
            {
                Select = "Id, Amount",
                Filter = FilterTree.And([FilterTree.Leaf("Amount > @floor and Code = 'x'"), FilterTree.Leaf("IsPosted")]),
                OrderBy = "Code",
            };

            DiscoveryResult without = Engine.DiscoverQuery(spec, Options);
            DiscoveryResult with = Engine.DiscoverQuery(spec with { Conjuncts = true }, Options);

            Assert.Null(without.Conjuncts);
            Assert.Equal(["Code"], Assert.Single(with.Conjuncts!).Path);
            Assert.Equal(
                without.Paths.Select(p => (string.Join('.', p.Segments), p.Site)),
                with.Paths.Select(p => (string.Join('.', p.Segments), p.Site)));
            Assert.Equal(without.Parameters.Select(p => p.Name), with.Parameters.Select(p => p.Name));
            Assert.Equal(without.Functions, with.Functions);
            Assert.Equal(without.Diagnostics, with.Diagnostics);
            Assert.Null(Engine.Discover("Amount > 0", new DiscoveryOptions()).Conjuncts);
        }

        /// <summary>Without a schema nothing is bound, so nothing is found, but the answer is still a list.</summary>
        [Fact]
        public void WithoutASchema_NothingIsFound()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec { Filter = FilterTree.Leaf("Amount > 0"), Conjuncts = true },
                new DiscoveryOptions());

            Assert.NotNull(result.Conjuncts);
            Assert.Empty(result.Conjuncts);
        }

        /// <summary>Discovers a filter's conjuncts.</summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The conjuncts.</returns>
        private static IReadOnlyList<FilterConjunct> Conjuncts(FilterTree filter)
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec { Select = "Id", Filter = filter, Conjuncts = true },
                Options);

            return result.Conjuncts!;
        }

        /// <summary>The value a vector names.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The value.</returns>
        private static object? Expected(string name)
        {
            return name switch
            {
                "1012" => 1012m,
                "-12.50" => -12.50m,
                "-12.5" => -12.5m,
                "decimal-max" => decimal.MaxValue,
                "decimal-max-scaled" => -7.9228162514264337593543950335m,
                "true" => true,
                "false" => false,
                "date" => new DateOnly(2026, 7, 1),
                "datetime" => new DateTime(2026, 7, 1, 10, 30, 0).AddMilliseconds(500),
                "datetimeoffset" => new DateTimeOffset(2026, 7, 1, 10, 30, 0, TimeSpan.FromHours(3)),
                "guid" => Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
                "null" => null,
                _ => name,
            };
        }
    }
}
