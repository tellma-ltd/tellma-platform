// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Immutable;
using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;
using Tellma.Queryex.Testing.Semantics;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     Every whole query the conformance suites check.
    /// </summary>
    /// <remarks>
    ///     Each case that compiles has its SQL, its parameter table, and its result columns written
    ///     to a file beside the tests. A change in any of the three then shows up as a diff of that
    ///     file, which is the only way a change in emission gets looked at rather than merely
    ///     re-recorded.
    /// </remarks>
    public static partial class QueryCorpus
    {
        /// <summary>Every case, in a stable order.</summary>
        public static ImmutableArray<QueryCase> All { get; } = Build();

        /// <summary>Builds the corpus.</summary>
        /// <returns>The cases.</returns>
        private static ImmutableArray<QueryCase> Build()
        {
            IEnumerable<QueryCase> all = Shapes()
                .Concat(Emission())
                .Concat(Composition())
                .Concat(Structure())
                .Concat(Omitted())
                .Concat(Refusals())
                .Concat(StructureRefusals())
                .Concat(OmittedRefusals());
            return [.. all];
        }

        /// <summary>
        ///     The members a host composes a query from beside its text: restrictions to key sets it
        ///     holds, paging, and the columns its schema leaves out.
        /// </summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> Composition()
        {
            // Invoices 1 to 6; the optional customer is present on the odd ones only (2, 1 and 3
            // for invoices 1, 3 and 5), and the posted ones are the odd ones.
            KeySet invoices = Numbers(1, 2, 4);
            KeySet parties = Numbers(2, 3);

            yield return Query("query-restriction", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Code",
                Restrictions = [new KeySetRestriction("Id", "@tb0_invoices")],
            }) with
            {
                KeySets = KeySets(("@tb0_invoices", invoices)),
            };

            // An optional key: the rows with no customer are never in the set.
            yield return Query("query-restriction-filtered", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("IsPosted"),
                Restrictions = [new KeySetRestriction("CustomerId", "@tb0_parties")],
            }) with
            {
                KeySets = KeySets(("@tb0_parties", parties)),
            };

            // Through a navigation the select list also reads, so the two share one join.
            yield return Query("query-restrictions-two", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Customer.Name",
                Filter = FilterTree.Or([FilterTree.Leaf("Amount > 0"), FilterTree.Leaf("IsPosted")]),
                OrderBy = "Customer.Name desc",
                Restrictions =
                [
                    new KeySetRestriction("customer.id", "@tb0_parties"),
                    new KeySetRestriction("Id", "@tb0_invoices"),
                ],
            }) with
            {
                KeySets = KeySets(("@tb0_parties", parties), ("@tb0_invoices", Numbers(1, 3, 5, 6))),
            };

            // In a grouped query a restriction filters the rows that enter the groups.
            yield return Query("query-restriction-aggregate", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count(), sum(Amount)",
                Restrictions = [new KeySetRestriction("Id", "@tb0_invoices")],
            }) with
            {
                KeySets = KeySets(("@tb0_invoices", invoices)),
            };

            // A filter that is already false keeps every row out, so the restriction and the join
            // its path needs are left out of the statement.
            yield return Query("query-restriction-denied", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Or([]),
                Restrictions = [new KeySetRestriction("Customer.Id", "@tb0_parties")],
            }) with
            {
                KeySets = KeySets(("@tb0_parties", parties)),
                MatchesNothing = true,
            };

            yield return Query("query-take-only", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code",
                OrderBy = "Code desc",
                Take = 2,
            });

            yield return Query("query-undeclared", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Amount, Customer.Name",
                UndeclaredColumns = ["ExtrasJson"],
            });

            yield return Query("query-undeclared-paged", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Code, PostingDate",
                OrderBy = "PostingDate desc",
                Skip = 1,
                Take = 4,
                UndeclaredColumns = ["ExtrasJson", "AuditJson"],
            });

            yield return Query("emit-level", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "level(Account.Node), level(AltNode), level(Account.Parent.Node)",
                Filter = FilterTree.Leaf("level(Account.Node) <= 2"),
            });

            yield return Query("emit-level-root", new QuerySpec
            {
                Root = LedgerFixture.Account,
                Select = "Concept, level(Node)",
                Filter = FilterTree.Leaf("level(Node) <= 1"),
            });
        }

        /// <summary>A table of whole-number keys.</summary>
        /// <param name="keys">The keys.</param>
        /// <returns>The table.</returns>
        private static KeySet Numbers(params int[] keys)
        {
            return new KeySet(QueryexStoreType.QxInt, [.. keys.Select(key => QxValue.Number(key))]);
        }

        /// <summary>The tables a case's restrictions read, by name.</summary>
        /// <param name="tables">The names and the tables.</param>
        /// <returns>The map.</returns>
        private static Dictionary<string, KeySet> KeySets(params (string Name, KeySet Keys)[] tables)
        {
            return tables.ToDictionary(table => table.Name, table => table.Keys, StringComparer.OrdinalIgnoreCase);
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
