// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.RegularExpressions;
using Tellma.Queryex.Testing.Corpus;
using Tellma.Queryex.Testing.Probe;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Core.Queryex.Tests.Emit
{
    /// <summary>
    ///     Rebasing a criterion onto a navigation's target, and searching another entity's rows with a
    ///     semi-join.
    /// </summary>
    /// <remarks>
    ///     The shapes the emission takes are pinned by the stored snapshots; these pin what the
    ///     snapshots cannot show: the caller errors, the depth both nodes count toward, the aliases
    ///     across every compiled statement, folding, the join ceiling, and the cache key both are part
    ///     of.
    /// </remarks>
    public sealed partial class FilterStructureTests
    {
        /// <summary>The engine under test.</summary>
        private static readonly QueryexEngine Engine = new();

        /// <summary>A rebase onto a path with nothing to resolve is refused when it is built.</summary>
        /// <param name="navigation">The path.</param>
        [Theory]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData(".Customer")]
        [InlineData("Customer.")]
        [InlineData("Customer..Region")]
        [InlineData("Customer. .Region")]
        public void AViaWithAnEmptySegment_IsRefused(string navigation)
        {
            ArgumentException thrown = Assert.Throws<ArgumentException>(
                () => FilterTree.Via(navigation, FilterTree.Leaf("IsPosted")));

            Assert.Equal("navigation", thrown.ParamName);
        }

        /// <summary>A missing argument is refused where it is supplied.</summary>
        [Fact]
        public void AMissingArgument_IsRefused()
        {
            var leaf = FilterTree.Leaf("IsPosted");

            Assert.Throws<ArgumentNullException>(() => FilterTree.Via(null!, leaf));
            Assert.Throws<ArgumentNullException>(() => FilterTree.Via("Customer", null!));
            Assert.Throws<ArgumentNullException>(() => FilterTree.Exists(null!, "Invoice", leaf));
            Assert.Throws<ArgumentNullException>(() => FilterTree.Exists("InvoiceLine", null!, leaf));
            Assert.Throws<ArgumentNullException>(() => FilterTree.Exists("InvoiceLine", "Invoice", null!));
        }

        /// <summary>A semi-join without a name is refused when it is built.</summary>
        [Fact]
        public void AnExistsWithAnEmptyName_IsRefused()
        {
            var leaf = FilterTree.Leaf("IsPosted");

            Assert.Equal("entity", Assert.Throws<ArgumentException>(() => FilterTree.Exists(" ", "Invoice", leaf)).ParamName);
            Assert.Equal("navigation", Assert.Throws<ArgumentException>(() => FilterTree.Exists("InvoiceLine", "", leaf)).ParamName);
        }

        /// <summary>The nodes keep what they were built from.</summary>
        [Fact]
        public void TheNodes_KeepWhatTheyWereBuiltFrom()
        {
            var leaf = FilterTree.Leaf("IsPosted");

            FilterTree.ViaNode via = Assert.IsType<FilterTree.ViaNode>(FilterTree.Via("Line.Invoice", leaf));
            Assert.Equal("Line.Invoice", via.Navigation);
            Assert.Same(leaf, via.Inner);

            FilterTree.ExistsNode exists = Assert.IsType<FilterTree.ExistsNode>(FilterTree.Exists("core.ReportShare", "Report", leaf));
            Assert.Equal("core.ReportShare", exists.Entity);
            Assert.Equal("Report", exists.Navigation);
            Assert.Same(leaf, exists.Inner);
        }

        /// <summary>Each node counts one level toward the depth ceiling, and refuses the one past it.</summary>
        [Fact]
        public void EachNode_CountsOneLevelOfDepth()
        {
            var tree = FilterTree.Leaf("IsPosted");
            for (int level = 1; level < FilterTree.MaxDepth - 2; level++)
            {
                tree = FilterTree.Not(tree);
            }

            // Two levels below the ceiling: one of each node takes it to the ceiling, and nothing may
            // wrap the result.
            var deepest = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Via("Invoice", tree));

            Assert.Throws<ArgumentException>(() => FilterTree.Via("Customer", deepest));
            Assert.Throws<ArgumentException>(() => FilterTree.Exists("InvoiceLine", "Invoice", deepest));
            Assert.Throws<ArgumentException>(() => FilterTree.Not(deepest));
        }

        /// <summary>A rebase whose path does not fit the schema is a caller error at compilation.</summary>
        /// <param name="navigation">The path.</param>
        /// <param name="segment">The segment the error names.</param>
        [Theory]
        [InlineData("Nothing", "Nothing")]
        [InlineData("Amount", "Amount")]
        [InlineData("Customer.Nothing", "Nothing")]
        [InlineData("Customer.Name", "Name")]
        public void AViaThatDoesNotFit_IsACallerError(string navigation, string segment)
        {
            QuerySpec spec = Spec(FilterTree.And([FilterTree.Leaf("IsPosted"), FilterTree.Via(navigation, FilterTree.Leaf("Id > 0"))]));

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(spec, Options()));

            Assert.Equal("spec", thrown.ParamName);
            Assert.Contains("Filter.And[1]", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("'" + segment + "'", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>A semi-join that does not fit the schema is a caller error at compilation.</summary>
        /// <param name="entity">The searched entity.</param>
        /// <param name="navigation">Its navigation.</param>
        /// <param name="named">What the error names.</param>
        [Theory]
        [InlineData("Nothing", "Invoice", "'Nothing'")]
        [InlineData("InvoiceLine", "Nothing", "'Nothing'")]
        [InlineData("InvoiceLine", "Party", "points at Party, not at Invoice")]
        [InlineData("Invoice", "Customer", "points at Party, not at Invoice")]
        public void AnExistsThatDoesNotFit_IsACallerError(string entity, string navigation, string named)
        {
            QuerySpec spec = Spec(FilterTree.Not(FilterTree.Exists(entity, navigation, FilterTree.And([]))));

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(spec, Options()));

            Assert.Equal("spec", thrown.ParamName);
            Assert.Contains("Filter.Not", thrown.Message, StringComparison.Ordinal);
            Assert.Contains(named, thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A semi-join inside a rebase resolves against the rebase's target, and one that points at
        ///     the query's root instead is a caller error.
        /// </summary>
        [Fact]
        public void AnExistsInsideAVia_ResolvesAgainstTheTarget()
        {
            QuerySpec wrong = Spec(FilterTree.Via("Customer", FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([]))));
            QuerySpec right = Spec(FilterTree.Via("Customer", FilterTree.Exists("Invoice", "Customer", FilterTree.And([]))));

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(wrong, Options()));
            Assert.Contains("Filter.Via[Customer]", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("not at Party", thrown.Message, StringComparison.Ordinal);

            Assert.True(Engine.CompileQuery(right, Options()).Succeeded);
        }

        /// <summary>A rebase inside a semi-join resolves against the searched entity.</summary>
        [Fact]
        public void AViaInsideAnExists_ResolvesAgainstTheSearchedEntity()
        {
            QuerySpec wrong = Spec(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Via("Customer", FilterTree.And([]))));

            ArgumentException thrown = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(wrong, Options()));
            Assert.Contains("Filter.Exists[InvoiceLine]", thrown.Message, StringComparison.Ordinal);
            Assert.Contains("not a navigation of InvoiceLine", thrown.Message, StringComparison.Ordinal);
        }

        /// <summary>Neither node may sit in a group-level predicate, however deep.</summary>
        [Fact]
        public void EitherNodeInAHaving_IsACallerError()
        {
            QuerySpec grouped = new()
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count()",
            };

            ArgumentException via = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(
                grouped with { Having = FilterTree.And([FilterTree.Leaf("count() > 1"), FilterTree.Via("Centre", FilterTree.And([]))]) },
                Options()));

            ArgumentException exists = Assert.Throws<ArgumentException>(() => Engine.CompileQuery(
                grouped with { Having = FilterTree.Not(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([]))) },
                Options()));

            Assert.Equal("spec", via.ParamName);
            Assert.Contains("Having.And[1]", via.Message, StringComparison.Ordinal);
            Assert.Contains("Having.Not", exists.Message, StringComparison.Ordinal);

            // The same trees as the row-level filter compile.
            Assert.True(Engine.CompileQuery(grouped with { Filter = FilterTree.Via("Centre", FilterTree.And([])) }, Options()).Succeeded);
        }

        /// <summary>Discovery refuses the same structure, and refuses either node in a group-level predicate with no schema at all.</summary>
        [Fact]
        public void Discovery_RefusesTheSameStructure()
        {
            DiscoveryOptions withSchema = new() { Schema = LedgerFixture.Schema, Root = LedgerFixture.Invoice };

            Assert.Throws<ArgumentException>(() => Engine.DiscoverQuery(
                new QueryDiscoverySpec { Filter = FilterTree.Via("Nothing", FilterTree.Leaf("Id > 0")) },
                withSchema));

            Assert.Throws<ArgumentException>(() => Engine.DiscoverQuery(
                new QueryDiscoverySpec
                {
                    Aggregate = true,
                    Having = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([])),
                },
                new DiscoveryOptions()));

            // Without a schema there is nothing to resolve a name against, so a rebase onto any
            // path is read for what its text shows.
            DiscoveryResult unresolved = Engine.DiscoverQuery(
                new QueryDiscoverySpec { Filter = FilterTree.Via("Nothing", FilterTree.Leaf("Id = @a")) },
                new DiscoveryOptions());

            Assert.Equal("a", Assert.Single(unresolved.Parameters).Name);
            Assert.Empty(unresolved.Paths);
        }

        /// <summary>
        ///     The host's structure is checked before any text: a caller error is raised whatever the
        ///     user's text beside it would have reported.
        /// </summary>
        [Fact]
        public void TheCallerError_ComesBeforeTheUsersDiagnostics()
        {
            QuerySpec spec = Spec(FilterTree.And([FilterTree.Leaf("Nothing = 1"), FilterTree.Via("Nope", FilterTree.Leaf("Id > 0"))]));

            Assert.Throws<ArgumentException>(() => Engine.CompileQuery(spec, Options()));
        }

        /// <summary>A rebase's segments resolve ignoring case, as a path's do.</summary>
        [Fact]
        public void AViaPath_ResolvesIgnoringCase()
        {
            string written = Compile(Spec(FilterTree.Via("Customer.Region", FilterTree.Leaf("Name = 'North'")))).Sql;
            string shouted = Compile(Spec(FilterTree.Via("CUSTOMER.region", FilterTree.Leaf("Name = 'North'")))).Sql;

            Assert.Equal(written, shouted);
        }

        /// <summary>
        ///     The names a host passes are resolved to the schema's descriptors and never reach the
        ///     SQL: only the physical sources and columns do.
        /// </summary>
        [Fact]
        public void TheNamesAHostPasses_NeverReachTheSql()
        {
            CompiledQuery query = Compile(Spec(FilterTree.And(
            [
                FilterTree.Via("customer", FilterTree.Leaf("Name = 'Acme'")),
                FilterTree.Exists("invoiceline", "INVOICE", FilterTree.Leaf("Amount > 0")),
            ])));

            Assert.Contains("[gl].[DocumentLines] AS [X1]", query.Sql, StringComparison.Ordinal);
            Assert.Contains("[X1].[DocumentId] = [T].[DocumentId]", query.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("customer", query.Sql, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("invoiceline", query.Sql, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A rebase and a clause reading the same navigation share one join, whichever comes first.</summary>
        [Fact]
        public void AVia_SharesItsJoinWithTheOtherClauses()
        {
            CompiledQuery query = Compile(new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Region.Name",
                Filter = FilterTree.Via("Customer", FilterTree.Leaf("Region.Code = 'N'")),
                OrderBy = "Customer.Name",
            });

            Assert.Single(Regex.Matches(query.Sql, @"JOIN \[dbo\]\.\[Agents\]"));
            Assert.Single(Regex.Matches(query.Sql, @"JOIN \[dbo\]\.\[Regions\]"));
        }

        /// <summary>
        ///     A rebased criterion that is already settled settles the node: a false one leaves no
        ///     presence test and no join, and a true one through a mandatory path reads nothing.
        /// </summary>
        [Fact]
        public void ASettledCriterion_FoldsTheVia()
        {
            CompiledQuery none = Compile(Spec(FilterTree.Via("Customer", FilterTree.Or([]))));
            CompiledQuery every = Compile(Spec(FilterTree.Via("Centre", FilterTree.Leaf("Id is not null"))));

            Assert.Contains("WHERE (1 = 0)\n", none.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("JOIN", none.Sql, StringComparison.Ordinal);
            Assert.Contains("WHERE (1 = 1)\n", every.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("JOIN", every.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A semi-join whose criterion holds for no row is false and writes no subquery, and the
        ///     ones that survive are numbered without a gap.
        /// </summary>
        [Fact]
        public void AnExistsOverAFalseCriterion_FoldsAndIsNotNumbered()
        {
            CompiledQuery query = Compile(Spec(FilterTree.Or(
            [
                FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Id is null")),
                FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 0")),
            ])));

            Assert.Single(Regex.Matches(query.Sql, "EXISTS"));
            Assert.Contains("AS [X1]", query.Sql, StringComparison.Ordinal);
            Assert.DoesNotContain("[X2]", query.Sql, StringComparison.Ordinal);

            CompiledQuery negated = Compile(Spec(FilterTree.Not(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Or([])))));
            Assert.Contains("WHERE (1 = 1)\n", negated.Sql, StringComparison.Ordinal);
        }

        /// <summary>
        ///     A semi-join's source counts toward the join ceiling, and so do its joins and a nested
        ///     semi-join's source.
        /// </summary>
        [Fact]
        public void ASemiJoin_CountsTowardTheJoinCeiling()
        {
            QuerySpec spec = new()
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Name",
                Filter = FilterTree.Exists(
                    "InvoiceLine",
                    "Invoice",
                    FilterTree.And(
                    [
                        FilterTree.Leaf("Party.Name = 'Acme'"),
                        FilterTree.Via("Party", FilterTree.Exists("Invoice", "Customer", FilterTree.And([]))),
                    ])),
            };

            // One join outside, then the first source and the one join its path and its rebase
            // share, and then the nested source: four.
            Assert.True(Engine.CompileQuery(spec, Limited(4)).Succeeded);

            QueryexDiagnostic problem = Assert.Single(Engine.CompileQuery(spec, Limited(3)).Diagnostics);
            Assert.Equal("QX5006", problem.Code);
            Assert.Equal("Filter.Exists[InvoiceLine].And[1].Via[Party].Exists[Invoice]", problem.Location);
            Assert.Equal(default, problem.Span);
        }

        /// <summary>
        ///     In every statement the corpus compiles, every alias is declared once, and each sequence
        ///     is numbered from one without a gap in the order it is written — pre-order across nested
        ///     subqueries.
        /// </summary>
        [Fact]
        public void EveryCompiledStatement_DeclaresEachAliasOnceInOrder()
        {
            foreach (QueryCase entry in QueryCorpus.All.Where(entry => entry.Diagnostics.Count == 0))
            {
                QueryexResult<CompiledQuery> result = Engine.CompileQuery(entry.Spec, new QueryCompilationOptions
                {
                    QueryexVersion = QueryexLanguage.Version,
                    Schema = LedgerFixture.Schema,
                    Parameters = entry.Parameters,
                    HasUser = entry.HasUser,
                    Limits = entry.Limits,
                });

                Assert.True(result.Succeeded, entry.Id);
                foreach (char sequence in "PXB")
                {
                    int[] declared = [.. Declared(result.Value.Sql, sequence)];
                    Assert.True(
                        declared.SequenceEqual(Enumerable.Range(1, declared.Length)),
                        entry.Id + ": " + sequence + " aliases are declared as " + string.Join(", ", declared));
                }
            }
        }

        /// <summary>A rebase and a semi-join are part of what a compilation is remembered under.</summary>
        [Fact]
        public void BothNodes_ArePartOfTheCacheKey()
        {
            var leaf = FilterTree.Leaf("Id > 0");
            QuerySpec plain = Spec(leaf);

            Assert.False(Shares(plain, Spec(FilterTree.Via("Customer", leaf))));
            Assert.False(Shares(Spec(FilterTree.Via("Customer", leaf)), Spec(FilterTree.Via("Customer.Manager", leaf))));
            Assert.False(Shares(Spec(FilterTree.Via("Customer", leaf)), Spec(FilterTree.Via("Centre", leaf))));
            Assert.False(Shares(
                Spec(FilterTree.Exists("InvoiceLine", "Invoice", leaf)),
                Spec(FilterTree.Exists("Invoice", "InvoiceLine", leaf))));
            Assert.False(Shares(Spec(FilterTree.Exists("InvoiceLine", "Invoice", leaf)), plain));
            Assert.False(Shares(
                Spec(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 0"))),
                Spec(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 1")))));

            // A name that is a prefix of another must not render like the other.
            Assert.False(Shares(
                Spec(FilterTree.Exists("Invoice", "Line", leaf)),
                Spec(FilterTree.Exists("InvoiceL", "ine", leaf))));

            Assert.True(Shares(
                Spec(FilterTree.Via("Customer", FilterTree.Exists("Invoice", "Customer", leaf))),
                Spec(FilterTree.Via("Customer", FilterTree.Exists("Invoice", "Customer", FilterTree.Leaf("Id > 0"))))));
        }

        /// <summary>A rebased leaf is bound and cached against the target, apart from the same text on the root.</summary>
        [Fact]
        public void ARebasedLeaf_BindsAgainstTheTarget()
        {
            // "Name" is a property of the party and not of the invoice: on the root it is a problem,
            // under the rebase it is not, and neither answer leaks into the other through a cache.
            QueryexResult<CompiledQuery> onRoot = Engine.CompileQuery(Spec(FilterTree.Leaf("Name = 'Acme'")), Options());
            QueryexResult<CompiledQuery> rebased = Engine.CompileQuery(
                Spec(FilterTree.Via("Customer", FilterTree.Leaf("Name = 'Acme'"))),
                Options());
            QueryexResult<CompiledQuery> again = Engine.CompileQuery(Spec(FilterTree.Leaf("Name = 'Acme'")), Options());

            Assert.Equal("QX3001", Assert.Single(onRoot.Diagnostics).Code);
            Assert.True(rebased.Succeeded);
            Assert.Equal("QX3001", Assert.Single(again.Diagnostics).Code);
        }

        /// <summary>The aliases one sequence declares, in the order they are written.</summary>
        /// <param name="sql">The statement.</param>
        /// <param name="sequence">The sequence's letter.</param>
        /// <returns>The numbers.</returns>
        private static IEnumerable<int> Declared(string sql, char sequence)
        {
            foreach (Match match in AliasDeclaration().Matches(sql))
            {
                if (match.Groups["sequence"].Value[0] == sequence)
                {
                    yield return int.Parse(match.Groups["number"].Value, System.Globalization.CultureInfo.InvariantCulture);
                }
            }
        }

        /// <summary>Where an alias is declared: after <c>AS</c>, as a source, a join or a binding.</summary>
        /// <returns>The pattern.</returns>
        [GeneratedRegex(@"AS \[(?<sequence>[PXB])(?<number>[0-9]+)\]")]
        private static partial Regex AliasDeclaration();

        /// <summary>Whether two queries share one compilation.</summary>
        /// <param name="first">One query.</param>
        /// <param name="second">The other.</param>
        /// <returns>True when they do.</returns>
        private static bool Shares(QuerySpec first, QuerySpec second)
        {
            return ProbeCacheKeys.ShareQueryEntry(first, second, Options());
        }

        /// <summary>A query over invoices with a given filter.</summary>
        /// <param name="filter">The filter.</param>
        /// <returns>The query.</returns>
        private static QuerySpec Spec(FilterTree filter)
        {
            return new QuerySpec { Root = LedgerFixture.Invoice, Select = "Id", Filter = filter };
        }

        /// <summary>Compiles a query, failing the test if it did not compile.</summary>
        /// <param name="spec">The query.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compile(QuerySpec spec)
        {
            QueryexResult<CompiledQuery> result = Engine.CompileQuery(spec, Options());
            Assert.True(
                result.Succeeded,
                string.Join(", ", result.Diagnostics.Select(d => d.Code + "@" + d.Location)));

            return result.Value;
        }

        /// <summary>The options every compilation here runs under.</summary>
        /// <returns>The options.</returns>
        private static QueryCompilationOptions Options()
        {
            return new QueryCompilationOptions
            {
                QueryexVersion = QueryexLanguage.Version,
                Schema = LedgerFixture.Schema,
            };
        }

        /// <summary>The options, with a join ceiling.</summary>
        /// <param name="joins">The ceiling.</param>
        /// <returns>The options.</returns>
        private static QueryCompilationOptions Limited(int joins)
        {
            return Options() with { Limits = QueryexLimits.Default with { MaxJoins = joins } };
        }
    }
}
