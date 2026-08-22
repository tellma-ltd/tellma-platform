// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>One whole query, and what compiling it is supposed to produce.</summary>
    public sealed record QueryCase
    {
        /// <summary>
        ///     A stable name for this case, which is also the name of its stored snapshot.
        /// </summary>
        public required string Id { get; init; }

        /// <summary>The query.</summary>
        public required QuerySpec Spec { get; init; }

        /// <summary>The declared parameters.</summary>
        public IReadOnlyList<QueryexParameterDeclaration> Parameters { get; init; } = [];

        /// <summary>Whether execution will have a signed-in user.</summary>
        public bool HasUser { get; init; } = true;

        /// <summary>The ceilings, when this case is about one of them.</summary>
        public QueryexLimits Limits { get; init; } = QueryexLimits.Default;

        /// <summary>
        ///     The diagnostic codes the case expects. Empty means the query is expected to compile,
        ///     in which case its SQL is compared against a stored snapshot.
        /// </summary>
        public IReadOnlyList<string> Diagnostics { get; init; } = [];

        /// <summary>
        ///     Whether this case is expected to match no rows at all.
        /// </summary>
        /// <remarks>
        ///     Declared rather than discovered. Two implementations agree trivially on an empty
        ///     answer, so a case that matches nothing is evidence about nothing — and one becomes
        ///     that silently, the moment the fixture's rows drift away from what it asks for. A case
        ///     whose whole point is that it denies everything says so here; every other case is held
        ///     to returning something.
        /// </remarks>
        public bool MatchesNothing { get; init; }

        /// <summary>Returns the case's name, so a failing theory names itself.</summary>
        /// <returns>The identifier.</returns>
        public override string ToString()
        {
            return Id;
        }
    }

    /// <summary>
    ///     Every whole query the conformance suites check.
    /// </summary>
    /// <remarks>
    ///     Each case that compiles has its SQL, its parameter table, and its result columns written
    ///     to a file beside the tests. A change in any of the three then shows up as a diff of that
    ///     file, which is the only way a change in emission gets looked at rather than merely
    ///     re-recorded.
    /// </remarks>
    public static class QueryCorpus
    {
        /// <summary>Every case, in a stable order.</summary>
        public static ImmutableArray<QueryCase> All { get; } = Build();

        /// <summary>Builds the corpus.</summary>
        /// <returns>The cases.</returns>
        private static ImmutableArray<QueryCase> Build()
        {
            IEnumerable<QueryCase> all = Shapes().Concat(Emission()).Concat(Refusals());
            return [.. all];
        }

        /// <summary>The shapes a query can take.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> Shapes()
        {
            yield return Query("query-projection", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Code, Amount",
            });

            yield return Query("query-navigation", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Customer.Name, Centre.Name, Customer.Manager.Region.Name",
            });

            yield return Query("query-filtered", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Amount > 100 and IsPosted"),
            });

            yield return Query("query-composed-filter", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("IsPosted or Amount > 0"),
                    FilterTree.Not(FilterTree.Leaf("Count > 10")),
                ]),
            });

            yield return Query("query-empty-permissions", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Or([]),
            }) with
            {
                MatchesNothing = true,
            };

            yield return Query("query-grouped", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount), count(), avg(Count), min(PostingDate), max(IsPosted)",
                Having = FilterTree.Leaf("sum(Amount) > 100"),
                OrderBy = "sum(Amount) desc",
            });

            yield return Query("query-grand-total", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "count(), sum(Amount)",
            });

            yield return Query("query-distinct", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, Customer.Name",
            });

            yield return Query("query-paged", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code, PostingDate",
                OrderBy = "PostingDate desc",
                Skip = 2,
                Take = 3,
            });

            yield return Query("query-grouped-paged", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Customer.Name, Centre.Name, sum(Amount)",
                OrderBy = "sum(Amount) desc",
                Skip = 0,
                Take = 25,
            });

            yield return Query("query-declared-parameters", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("PostingDate >= @From and PostingDate <= @To"),
            }) with
            {
                Parameters =
                [
                    new QueryexParameterDeclaration("From", QueryexType.QxDate, IsNotNull: true),
                    new QueryexParameterDeclaration("To", QueryexType.QxDate, IsNotNull: false),
                ],
            };

            yield return Query("query-context-values", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "today(), now()",
                Filter = FilterTree.Leaf("CreatedById = me()"),
            });

            yield return Query("query-context-without-user", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("CreatedById = me()"),
            }) with
            {
                HasUser = false,
                MatchesNothing = true,
            };
        }

        /// <summary>The emissions that are easy to get subtly wrong.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> Emission()
        {
            yield return Query("emit-guards", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "Rate = 1.25 and Memo != Notes and Rate != Amount and Rate > Amount"),
            });

            yield return Query("emit-value-binding", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Rate + 1 > 2 and Memo || 'x' != Notes"),
            });

            yield return Query("emit-bool-realisation", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "IsPosted, IsApproved, Amount > 100, if(Amount > 0, IsPosted, false)",
                Filter = FilterTree.Leaf("if(Amount > 0, IsPosted, false)"),
            });

            yield return Query("emit-membership", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "Count in (3, 6, 9) and Rate in (1.25, 2, null) and Memo in (Notes, 'alpha memo')"),
            });

            yield return Query("emit-widening", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "sum(Count), avg(Count), sum(Amount), avg(Amount), Count / CreatedById, Amount / Rate",
            });

            yield return Query("emit-strings", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Memo || Notes, length(Code), upper(trim(Memo)), left(Memo, 3), replace(Memo, 'a', 'b')",
                Filter = FilterTree.Leaf(
                    "contains(Memo, 'em') and startsWith(Code, 'INV') and endsWith(Ref, ' ')"),
            });

            yield return Query("emit-calendar", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "year(PostingDate), quarter(PostingDate), month(PostingDate), day(PostingDate),"
                    + " week(PostingDate), weekday(PostingDate), startOfWeek(PostingDate),"
                    + " startOfMonth(PostingDate), startOfYear(PostingDate), startOfDay(PostedOn)",
            });

            yield return Query("emit-instants", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "local(PostedAt), local(PostedAt, 'Africa/Nairobi'), hour(local(PostedAt)),"
                    + " diffDays(PostingDate, DueDate), diffSeconds(PostedAt, ApprovedAt),"
                    + " diffYears(PostingDate, DueDate), addDays(PostedAt, 1), addMonths(PostingDate, 1)",
            });

            yield return Query("emit-conversions", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                // Converted back and forth rather than from an arbitrary column: conversion here
                // is the strict kind, so text that is not a number is an error rather than an
                // absent value, and a case that errors on the data would test nothing else.
                Select = "cast(Amount, 'string'), cast(cast(Amount, 'string'), 'numeric'),"
                    + " cast(PostingDate, 'string'), cast(IsPosted, 'string'),"
                    + " cast(ExternalId, 'string'), cast(Notes, 'date'), cast(null, 'numeric')",
            });

            yield return Query("emit-hierarchy", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "descendantOf(Account.Concept, 'Assets', 'Equity') or ancestorOf(Account.Concept, 'Cash')"),
            });

            yield return Query("emit-group-grain-guards", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Rate)",
                Having = FilterTree.Leaf("sum(Rate) = 3.75 and sum(Rate) > 0 and sum(Rate) != 2"),
            });

            yield return Query("emit-arithmetic", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Amount + 1, Amount - 1, Amount * 2, Amount % 2, -Amount, abs(Amount),"
                    + " round(Amount, 2), floor(Amount), ceiling(Amount)",
            });

            yield return Query("emit-join-kinds", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Centre.Region.Name, Customer.Region.Name",
            });

            yield return Query("emit-pruned-join", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Centre.Name is not null"),
            });
        }

        /// <summary>The queries that are refused, and why.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> Refusals()
        {
            yield return new QueryCase
            {
                Id = "refuse-ordering-widens-grouping",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "Centre.Name, sum(Amount)",
                    OrderBy = "Customer.Name",
                },
                Diagnostics = ["QX4005"],
            };

            yield return new QueryCase
            {
                Id = "refuse-paging-without-ordering",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Take = 10,
                },
                Diagnostics = ["QX4006"],
            };

            yield return new QueryCase
            {
                Id = "refuse-duplicate-ordering",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    OrderBy = "PostingDate, PostingDate desc",
                },
                Diagnostics = ["QX4008"],
            };

            yield return new QueryCase
            {
                Id = "refuse-too-many-joins",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Customer.Name, Centre.Name, Account.Label",
                },
                Limits = QueryexLimits.Default with { MaxJoins = 2 },
                Diagnostics = ["QX5006"],
            };

            yield return new QueryCase
            {
                Id = "refuse-too-many-parameters",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Leaf("Count in (1, 2, 3, 4)"),
                },
                Limits = QueryexLimits.Default with { MaxParameters = 2 },
                Diagnostics = ["QX5007"],
            };
        }

        /// <summary>A case that is expected to compile.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="spec">The query.</param>
        /// <returns>The case.</returns>
        private static QueryCase Query(string id, QuerySpec spec)
        {
            return new QueryCase { Id = id, Spec = spec };
        }
    }
}
