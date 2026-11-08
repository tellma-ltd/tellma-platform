// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Binding
{
    /// <summary>What discovery reports back to a tool that is helping someone write an expression.</summary>
    public sealed class DiscoveryTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>Discovery against the fixture schema.</summary>
        private static DiscoveryOptions Options => new()
        {
            Schema = LedgerFixture.Schema,
            Root = LedgerFixture.Invoice,
            Mode = QueryexMode.Filter,
        };

        /// <summary>A parameter compared against a column takes that column's type.</summary>
        [Fact]
        public void Parameter_ComparedAgainstAColumn_TakesItsType()
        {
            DiscoveryResult result = Engine.Discover("PostingDate >= @From", Options);

            ParameterUse use = Assert.Single(result.Parameters);
            Assert.Equal("From", use.Name);
            Assert.Equal(QueryexType.QxDate, use.InferredType);
            Assert.Empty(use.Conflicts);
        }

        /// <summary>A parameter inside a call takes the type the call wants there.</summary>
        [Fact]
        public void Parameter_InsideACall_TakesTheTypeTheCallWants()
        {
            DiscoveryResult result = Engine.Discover("contains(Memo, @Q)", Options);

            ParameterUse use = Assert.Single(result.Parameters);
            Assert.Equal(QueryexType.QxString, use.InferredType);
        }

        /// <summary>Two parameters compared only against each other stay unknown.</summary>
        [Fact]
        public void Parameters_ComparedOnlyToEachOther_StayUnknown()
        {
            DiscoveryResult result = Engine.Discover("@a = @b", Options);

            Assert.Equal(2, result.Parameters.Count);
            Assert.All(result.Parameters, use => Assert.Null(use.InferredType));
        }

        /// <summary>A parameter used at two types reports both, with where each was demanded.</summary>
        [Fact]
        public void Parameter_UsedAtTwoTypes_ReportsBoth()
        {
            DiscoveryResult result = Engine.Discover("PostingDate >= @x and Amount > @x", Options);

            ParameterUse use = Assert.Single(result.Parameters);
            Assert.Equal(2, use.Occurrences.Count);
            Assert.NotEmpty(use.Conflicts);
            Assert.Contains(result.Diagnostics, d => d.Code == "QX3400");
        }

        /// <summary>Discovery reports what it can even where something else did not resolve.</summary>
        [Fact]
        public void Discovery_ReportsWhatItCan_DespiteAnUnresolvedName()
        {
            DiscoveryResult result = Engine.Discover("Nonesuch = 1 and PostingDate >= @From", Options);

            Assert.Contains(result.Diagnostics, d => d.Code == "QX3001");
            Assert.Contains(result.Parameters, use => use.Name == "From");
        }

        /// <summary>Without a schema, discovery still reports the lexical facts.</summary>
        [Fact]
        public void Discovery_WithoutASchema_StillReportsWhatTheGrammarShows()
        {
            DiscoveryResult result = Engine.Discover(
                "year(PostingDate) = @y and sum(Amount) > 0",
                new DiscoveryOptions());

            Assert.Contains("year", result.Functions);
            Assert.Contains("sum", result.Functions);
            Assert.True(result.UsesAggregation);
            Assert.Contains(result.Parameters, use => use.Name == "y");
            Assert.Empty(result.Paths);
        }

        /// <summary>With a schema, discovery reports the paths that resolved.</summary>
        [Fact]
        public void Discovery_WithASchema_ReportsResolvedPaths()
        {
            DiscoveryResult result = Engine.Discover("Customer.Manager.Name = 'x'", Options);

            PathUse path = Assert.Single(result.Paths);
            Assert.Equal(["Customer", "Manager", "Name"], path.Segments);
        }

        /// <summary>
        ///     A parameter typed in one clause and equated to another in a second is solved for both.
        /// </summary>
        [Fact]
        public void DiscoverQuery_LinksParametersAcrossClauses()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "Id",
                    Filter = FilterTree.Leaf("@a = @b"),
                    OrderBy = "if(PostingDate >= @a, 1, 2)",
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            Assert.Equal(2, result.Parameters.Count);
            Assert.All(result.Parameters, use => Assert.Equal(QueryexType.QxDate, use.InferredType));
        }

        /// <summary>Every occurrence is reported with the clause it was written in.</summary>
        [Fact]
        public void DiscoverQuery_NamesTheClauseEachUseIsIn()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Select = "Id",
                    Filter = FilterTree.And([FilterTree.Leaf("Amount > @a"), FilterTree.Leaf("Count > @a")]),
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            ParameterUse use = Assert.Single(result.Parameters);
            Assert.Equal(
                ["Filter.And[0]", "Filter.And[1]"],
                use.Occurrences.Select(o => o.Location).Order(StringComparer.Ordinal));
        }

        /// <summary>
        ///     A rebased criterion's paths are reported with the rebase's segments in front, so every
        ///     reported path reads from the query's root, nested rebases included.
        /// </summary>
        [Fact]
        public void DiscoverQuery_PrefixesARebasedCriterionsPaths()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Filter = FilterTree.And(
                    [
                        FilterTree.Leaf("Amount > 0"),
                        FilterTree.Via("Customer", FilterTree.Leaf("Name = @n and Region.Name = 'North'")),
                        FilterTree.Via("customer", FilterTree.Via("Manager", FilterTree.Leaf("Gender = 'F'"))),
                    ]),
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            Assert.Empty(result.Diagnostics);
            Assert.Equal(
                ["Amount", "Customer.Name", "Customer.Region.Name", "customer.Manager.Gender"],
                result.Paths.Select(path => string.Join('.', path.Segments)));

            // Each keeps the site of the text it was written in.
            Assert.Equal(
                ["Filter.And[0]", "Filter.And[1].Via[Customer]", "Filter.And[1].Via[Customer]", "Filter.And[2].Via[customer].Via[Manager]"],
                result.Paths.Select(path => path.Site.Location));

            // The parameter takes the type of the column it meets under the rebase.
            Assert.Equal(QueryexType.QxString, Assert.Single(result.Parameters).InferredType);
        }

        /// <summary>
        ///     A semi-join's criterion is bound for its problems and its parameters, but none of its
        ///     paths is reported: they read another entity's rows, not the query's.
        /// </summary>
        [Fact]
        public void DiscoverQuery_ReportsNoPathFromInsideASemiJoin()
        {
            DiscoveryResult result = Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Filter = FilterTree.And(
                    [
                        FilterTree.Leaf("Amount > 0"),
                        FilterTree.Exists(
                            "InvoiceLine",
                            "Invoice",
                            FilterTree.And(
                            [
                                FilterTree.Leaf("Memo = @m and Party.Name = 'Acme'"),
                                FilterTree.Via("Party", FilterTree.Leaf("Missing = 1")),
                            ])),
                    ]),
                },
                new DiscoveryOptions { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice });

            Assert.Equal(["Amount"], result.Paths.Select(path => string.Join('.', path.Segments)));

            ParameterUse use = Assert.Single(result.Parameters);
            Assert.Equal(QueryexType.QxString, use.InferredType);
            Assert.Equal("Filter.And[1].Exists[InvoiceLine].And[0]", Assert.Single(use.Occurrences).Location);

            QueryexDiagnostic problem = Assert.Single(result.Diagnostics);
            Assert.Equal("QX3001", problem.Code);
            Assert.Equal("Filter.And[1].Exists[InvoiceLine].And[1].Via[Party]", problem.Location);
        }

        /// <summary>A root without its schema is a caller mistake, not a diagnostic.</summary>
        [Fact]
        public void Discovery_WithARootButNoSchema_IsACallerMistake()
        {
            Assert.Throws<ArgumentException>(
                () => Engine.Discover("Amount", new DiscoveryOptions { Root = LedgerFixture.Invoice }));
        }
    }
}
