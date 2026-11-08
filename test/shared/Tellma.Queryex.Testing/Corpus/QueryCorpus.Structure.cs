// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     The queries whose filters a host composes with a rebase onto a navigation's target or with
    ///     a semi-join over another entity's rows.
    /// </summary>
    public static partial class QueryCorpus
    {
        /// <summary>The rebases and semi-joins that compile.</summary>
        /// <returns>The cases.</returns>
        /// <remarks>
        ///     Read against the fixture: invoices 1, 3 and 5 are posted, sit in the Sales centre and
        ///     have a customer (2, 1 and 3); invoices 2, 4 and 6 have none. Lines exist for invoices
        ///     1, 3, 4 and 5 only.
        /// </remarks>
        private static IEnumerable<QueryCase> Structure()
        {
            // A mandatory navigation: the target row always exists, so no presence test is needed.
            yield return Query("via-inner", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via("Centre", FilterTree.Leaf("Name = 'Sales'")),
            });

            // An optional one: the target's key is tested before the criterion is read.
            yield return Query("via-left", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via("Customer", FilterTree.Leaf("Name = 'Acme'")),
            });

            // The complement, rows with no customer included.
            yield return Query("via-left-negated", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Not(FilterTree.Via("Customer", FilterTree.Leaf("Name = 'Acme'"))),
            });

            // A dotted path behind an optional step stays optional all the way.
            yield return Query("via-path", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via("Customer.Region", FilterTree.Leaf("Name = 'North'")),
            });

            // The rebase and the select item read the same navigation through one join.
            yield return Query("via-shared-join", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Customer.Name",
                Filter = FilterTree.Via("Customer", FilterTree.Leaf("Name != 'beta'")),
                OrderBy = "Id",
            });

            yield return Query("via-nested", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via(
                    "Customer",
                    FilterTree.Via("Manager", FilterTree.Leaf("Name = 'Acme'"))),
            });

            // A criterion that holds for every target row leaves only the presence test.
            yield return Query("via-any", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via("Customer", FilterTree.And([])),
            });

            // The way a child is filtered by its owner's criterion.
            yield return Query("via-from-line", new QuerySpec
            {
                Root = LedgerFixture.InvoiceLine,
                Select = "Id, Amount",
                Filter = FilterTree.Via("Invoice", FilterTree.Leaf("IsPosted")),
            });

            yield return Query("via-under-or", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Or(
                [
                    FilterTree.Leaf("Amount < 0"),
                    FilterTree.Via("Customer", FilterTree.Leaf("Gender = 'F'")),
                ]),
            });

            yield return Query("exists-plain", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 10")),
            });

            // Any pointing row at all: the correlation alone.
            yield return Query("exists-any", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([])),
            });

            // The complement, invoices with no lines at all included.
            yield return Query("exists-negated", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Not(
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount < 0"))),
            });

            // A semi-join inside a semi-join, each with a subquery and an alias of its own.
            yield return Query("exists-nested", new QuerySpec
            {
                Root = LedgerFixture.Party,
                Select = "Id, Name",
                Filter = FilterTree.Exists(
                    "Invoice",
                    "Customer",
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount >= 20"))),
            });

            // Correlated with the rebase's target rather than with the statement's root.
            yield return Query("exists-inside-via", new QuerySpec
            {
                Root = LedgerFixture.InvoiceLine,
                Select = "Id",
                Filter = FilterTree.Via(
                    "Invoice",
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount < 0"))),
            });

            // Behind an optional rebase, and over the statement's own entity, which the subquery's
            // alias keeps apart from the statement's root.
            yield return Query("exists-inside-left-via", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Via(
                    "Customer",
                    FilterTree.Exists("Invoice", "Customer", FilterTree.Leaf("IsPosted"))),
            });

            // A rebase inside a subquery joins the subquery's own rows, and guards its optional step.
            yield return Query("exists-via-inside", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists(
                    "InvoiceLine",
                    "Invoice",
                    FilterTree.Via("Party", FilterTree.Leaf("Name = 'Acme'"))),
            });

            yield return Query("exists-under-or", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Or(
                [
                    FilterTree.Leaf("IsPosted"),
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Memo is null")),
                ]),
            });

            // The user is a parameter of the statement, so one statement serves every user.
            yield return Query("exists-me", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("CreatedById = me()")),
            });

            // A value the criterion shares with the rest of the query is one parameter.
            yield return Query("exists-shared-parameter", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("Amount > 7.5"),
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount > 7.5")),
                ]),
            });

            // Joins and bindings in two subqueries and in the statement, every alias distinct.
            yield return Query("exists-aliases", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Customer.Name",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("Rate + 1 > 2"),
                    FilterTree.Exists(
                        "InvoiceLine",
                        "Invoice",
                        FilterTree.And(
                        [
                            FilterTree.Leaf("Memo || 'x' != 'firstx'"),
                            FilterTree.Via("Party", FilterTree.Leaf("Name != 'beta'")),
                        ])),
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("PartyId + 1 > 2")),
                ]),
            });

            // A semi-join fans nothing out, so the totals count each invoice once.
            yield return Query("exists-in-aggregate", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count(), sum(Amount)",
                Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([])),
            });
        }

        /// <summary>
        ///     The problems found inside a rebase or a semi-join, each pinned to the location a host
        ///     attributes it to.
        /// </summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> StructureRefusals()
        {
            yield return new QueryCase
            {
                Id = "refuse-in-via",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Via("Customer", FilterTree.Leaf("Nothing = 1")),
                },
                Diagnostics = ["QX3001"],
                Sites = [Site("QX3001", "Filter.Via[Customer]", 0, 7, ("name", "Nothing"), ("entity", "Party"))],
            };

            yield return new QueryCase
            {
                Id = "refuse-in-exists",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Or(
                    [
                        FilterTree.Leaf("IsPosted"),
                        FilterTree.Exists(
                            "InvoiceLine",
                            "Invoice",
                            FilterTree.And([FilterTree.Leaf("Amount > 0"), FilterTree.Leaf("Nothing = 1")])),
                    ]),
                },
                Diagnostics = ["QX3001"],
                Sites =
                [
                    Site(
                        "QX3001",
                        "Filter.Or[1].Exists[InvoiceLine].And[1]",
                        0,
                        7,
                        ("name", "Nothing"),
                        ("entity", "InvoiceLine")),
                ],
            };

            // A join the criterion needs counts like any other, and is named by the criterion.
            yield return new QueryCase
            {
                Id = "refuse-joins-in-via",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Via("Customer", FilterTree.Leaf("Region.Name = 'North'")),
                },
                Limits = QueryexLimits.Default with { MaxJoins = 1 },
                Diagnostics = ["QX5006"],
                Sites = [Site("QX5006", "Filter.Via[Customer]", 0, 11, ("limit", "1"))],
            };

            // The subquery's source counts first, then its joins.
            yield return new QueryCase
            {
                Id = "refuse-joins-in-exists",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Party.Name = 'Acme'")),
                },
                Limits = QueryexLimits.Default with { MaxJoins = 1 },
                Diagnostics = ["QX5006"],
                Sites = [Site("QX5006", "Filter.Exists[InvoiceLine]", 0, 10, ("limit", "1"))],
            };

            // The source alone pushes the count over: nothing in the criterion is to blame, so the
            // problem names the semi-join and no range of any text.
            yield return new QueryCase
            {
                Id = "refuse-exists-source-over-ceiling",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Customer.Name",
                    Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.And([])),
                },
                Limits = QueryexLimits.Default with { MaxJoins = 1 },
                Diagnostics = ["QX5006"],
                Sites = [Site("QX5006", "Filter.Exists[InvoiceLine]", 0, 0, ("limit", "1"))],
            };

            // A plain leaf of a composed filter is named the same way.
            yield return new QueryCase
            {
                Id = "refuse-joins-in-composed-leaf",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.And(
                    [
                        FilterTree.Leaf("IsPosted"),
                        FilterTree.Leaf("Customer.Region.Name = 'North'"),
                    ]),
                },
                Limits = QueryexLimits.Default with { MaxJoins = 1 },
                Diagnostics = ["QX5006"],
                Sites = [Site("QX5006", "Filter.And[1]", 0, 20, ("limit", "1"))],
            };

            // The criterion's parameters are the statement's, and count toward its ceiling.
            yield return new QueryCase
            {
                Id = "refuse-parameters-in-exists",
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Select = "Id",
                    Filter = FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("Amount in (1, 2)")),
                },
                Limits = QueryexLimits.Default with { MaxParameters = 1 },
                Diagnostics = ["QX5007"],
                Sites = [Site("QX5007", "Filter.Exists[InvoiceLine]", 14, 1, ("limit", "1"))],
            };
        }

        /// <summary>One pinned diagnostic.</summary>
        /// <param name="code">The code.</param>
        /// <param name="location">The input it is attributed to.</param>
        /// <param name="start">Where its range starts within that input.</param>
        /// <param name="length">How long its range is.</param>
        /// <param name="arguments">Every argument it carries.</param>
        /// <returns>The expectation.</returns>
        private static DiagnosticSite Site(
            string code,
            string location,
            int start,
            int length,
            params (string Name, string Value)[] arguments)
        {
            return new DiagnosticSite(
                code,
                location,
                new QueryexSpan(start, length),
                arguments.ToDictionary(argument => argument.Name, argument => argument.Value, StringComparer.Ordinal));
        }
    }
}
