// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     The cases about declared parameters a compilation leaves out.
    /// </summary>
    /// <remarks>
    ///     Every case here declares the same parameters and leaves some of them out. Both halves of
    ///     the differential check are given values for all of them, the omitted ones included, so a
    ///     reading that mistook an omitted parameter for a supplied one answers differently.
    /// </remarks>
    public static partial class QueryCorpus
    {
        /// <summary>The parameters every case about omission declares.</summary>
        /// <remarks>
        ///     Computed on each read rather than held in a field: the cases are built while the
        ///     class is still initializing, before a field declared in this file would be set.
        /// </remarks>
        private static QueryexParameterDeclaration[] ReportParameters =>
        [
            new QueryexParameterDeclaration("AsOf", QueryexType.QxDate, IsNotNull: false),
            new QueryexParameterDeclaration("Customer", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Main", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Other", QueryexType.QxNumeric, IsNotNull: false),
            new QueryexParameterDeclaration("Root", QueryexType.QxString, IsNotNull: false),
            new QueryexParameterDeclaration("Posted", QueryexType.QxBool, IsNotNull: false),
            new QueryexParameterDeclaration("Floor", QueryexType.QxNumeric, IsNotNull: false),
        ];

        /// <summary>The compilations that leave declared parameters out.</summary>
        /// <returns>The cases.</returns>
        /// <remarks>
        ///     Invoices 1 to 6 are dated forty days apart from 10 February 2024, amount to
        ///     (id - 3) × 125.25, are posted on the odd ids, and carry a customer on the odd ids only
        ///     (2, 1 and 3 for invoices 1, 3 and 5). The differential check supplies 15 June 2024 for
        ///     the date and 2, 1 and 3 for the customer parameters.
        /// </remarks>
        private static IEnumerable<QueryCase> Omitted()
        {
            // A conjunct that mentions an omitted parameter leaves the conjunction. The names are
            // matched ignoring case, like every other reference to a parameter.
            yield return Omitting("omit-conjunct", ["CUSTOMER", "other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("PostingDate <= @AsOf and CustomerId = @Customer"),
            });

            // Under a negation the atom reads as false, so the negation of it lets every row through
            // too: the whole filter is gone.
            yield return Omitting("omit-negated", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("not (CustomerId = @Customer)"),
            });

            // One disjunct that lets every row through settles the disjunction.
            yield return Omitting("omit-disjunct", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("Code = 'INV-1' or CustomerId = @Customer"),
            });

            // A bare element leaves its list, and what is left of the list still restricts.
            yield return Omitting("omit-list-element", ["Other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("CustomerId in (@Main, @Other)"),
            });

            // The condition of an aggregation is a predicate wherever the aggregation sits.
            yield return Omitting("omit-measure-condition", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount, CustomerId = @Customer), count(Id, CustomerId = @Main)",
                OrderBy = "Centre.Name",
            });

            // Absorbed through a conjunction, a negation and a disjunction until only the part that
            // mentions nothing omitted is left.
            yield return Omitting("omit-absorbed", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "IsPosted and (Amount > 1000 or not (CustomerId = @Customer and Amount < 100))"),
            });

            // Two negations above the atom put it back where it is asserted; the one that is left
            // above what remains is kept.
            yield return Omitting("omit-negated-twice", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("not (not (CustomerId = @Customer) or IsPosted)"),
            });

            // A negation composed around a leaf counts exactly as one written inside it. Counted, the
            // atom reads as false and leaves the disjunction; not counted, it would read as true and
            // the negation would then deny every row.
            yield return Omitting("omit-tree-negation", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("PostingDate <= @AsOf"),
                    FilterTree.Not(FilterTree.Leaf("CustomerId = @Customer or Amount > 0")),
                ]),
            });

            // Absorption crosses the composed connectives as it does the written ones: the second
            // disjunct is never looked at again, and the conjunction keeps only its last child.
            yield return Omitting("omit-tree-absorbed", ["Customer", "Other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Or([FilterTree.Leaf("CustomerId = @Customer"), FilterTree.Leaf("Amount > 1000")]),
                    FilterTree.Not(FilterTree.Not(FilterTree.Leaf("CustomerId in (@Other)"))),
                    FilterTree.Leaf("Amount >= 0"),
                ]),
            });

            // A whole tree that folds away leaves the query with no filter at all.
            yield return Omitting("omit-tree-gone", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Not(FilterTree.Not(FilterTree.Leaf("CustomerId = @Customer"))),
            });

            // A list every element has left settles the atom, at whichever polarity it stands.
            yield return Omitting("omit-list-emptied", ["Customer", "Other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "CustomerId in (@Other) and not (CustomerId in ((@Customer), @Other)) and IsPosted"),
            });

            // The keys of the hierarchy predicates are lists too: one key leaves the first, and the
            // second is left with none.
            yield return Omitting("omit-hierarchy-keys", ["Root"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf(
                    "descendantOf(Account.Concept, @Root, 'Equity') and ancestorOf(Account.Concept, @Root)"),
            });

            // The group-level predicate folds the same way, and so does a condition inside it.
            yield return Omitting("omit-having", ["Floor", "Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount)",
                Having = FilterTree.Leaf(
                    "sum(Amount) > @Floor and count(Id, CustomerId = @Customer) > 2"),
                OrderBy = "Centre.Name",
            });

            // A group-level predicate that folds away entirely leaves every group in.
            yield return Omitting("omit-having-gone", ["Floor"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count(Id)",
                Having = FilterTree.Not(FilterTree.Leaf("not (sum(Amount) > @Floor)")),
                OrderBy = "Centre.Name",
            });

            // Two ordering terms that differ only in a parameter each of them leaves out are one
            // term once both are folded. The second is left out of the statement rather than
            // reported: nothing was mistyped, and it could not change the order.
            yield return Omitting("omit-ordering-repeated", ["Customer", "Other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, sum(Amount, CustomerId = @Customer), sum(Amount, CustomerId = @Other)",
                OrderBy = "sum(Amount, CustomerId = @Customer) desc, sum(Amount, CustomerId = @Other),"
                    + " Centre.Name",
            });

            yield return Omitting("omit-ordering-repeated-values", ["Customer", "Other"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                OrderBy = "coalesce(@Customer, Amount) desc, coalesce(@Other, Amount), Id",
            });

            // Anywhere else an omitted parameter is the absent value of its declared type: a column
            // of that type, an operand, an element of a list read as a value, and a condition that
            // is not an aggregation's.
            yield return Omitting("omit-select-absent", ["Customer", "Other", "Posted"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, @Customer, coalesce(@Customer, CustomerId, 0), CustomerId in (@Main, @Other),"
                    + " if(CustomerId = @Customer, 1, 0), if(@Posted, 1, 0)",
                OrderBy = "coalesce(@Other, Amount) desc",
            });

            // A rebase and a semi-join are each one atom: something inside mentions an omitted
            // parameter, so the whole node stops deciding anything — the other restrictions composed
            // inside it included.
            yield return Omitting("omit-via", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("Amount > 0"),
                    FilterTree.Via(
                        "Customer",
                        FilterTree.And([FilterTree.Leaf("Id = @Customer"), FilterTree.Leaf("Name = 'Acme'")])),
                ]),
            });

            yield return Omitting("omit-exists-negated", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.And(
                [
                    FilterTree.Leaf("IsPosted"),
                    FilterTree.Not(FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("PartyId = @Customer"))),
                    FilterTree.Exists("InvoiceLine", "Invoice", FilterTree.Leaf("PartyId = @Main")),
                ]),
            });

            // Leaving out a parameter nothing mentions changes nothing about the statement.
            yield return Omitting("omit-unmentioned", ["Customer", "Floor"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("PostingDate <= @AsOf"),
            });
        }

        /// <summary>The compilations that leave parameters out and are still refused.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> OmittedRefusals()
        {
            // What is left after folding is bound like any other text, and a problem in it still
            // points into the text as it was written.
            yield return Omitting("refuse-beside-omitted", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("CustomerId = @Customer and Nothing = 1"),
            }) with
            {
                Diagnostics = ["QX3001"],
                Sites = [Site("QX3001", "Filter", 27, 7, ("name", "Nothing"), ("entity", "Invoice"))],
            };

            // The absent value an omitted parameter becomes carries the parameter's own span.
            yield return Omitting("refuse-omitted-operand", ["Root"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, Amount + @Root",
            }) with
            {
                Diagnostics = ["QX3201"],
                Sites = [Site("QX3201", "Select", 13, 5, ("type", "String"))],
            };

            // A text that is not one predicate is still refused when part of it folds away.
            yield return Omitting("refuse-omitted-list", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("CustomerId = @Customer, IsPosted"),
            }) with
            {
                Diagnostics = ["QX2009"],
                Sites = [Site("QX2009", "Filter", 24, 8)],
            };
        }

        /// <summary>One case that declares the shared parameters and leaves some of them out.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="omitted">The names left out.</param>
        /// <param name="spec">The query.</param>
        /// <returns>The case.</returns>
        private static QueryCase Omitting(string id, string[] omitted, QuerySpec spec)
        {
            return new QueryCase
            {
                Id = id,
                Spec = spec,
                Parameters = ReportParameters,
                OmittedParameters = omitted,
            };
        }
    }
}
