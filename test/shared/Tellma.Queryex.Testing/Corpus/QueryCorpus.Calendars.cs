// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>
    ///     The queries that count in a calendar other than the Gregorian: the Ethiopian calendar's
    ///     inline arithmetic, and the Umm al-Qura calendar's lookups on the host's month table in
    ///     each of the three forms they take.
    /// </summary>
    public static partial class QueryCorpus
    {
        /// <summary>The calendar queries that compile.</summary>
        /// <returns>The cases.</returns>
        /// <remarks>
        ///     Read against the fixture: invoices are posted forty days apart from 10 February 2024,
        ///     the execution's today is 15 June 2024 (9 Dhu al-Hijjah 1445), and every other invoice
        ///     has no due date, so the absent operand is exercised beside the present one.
        /// </remarks>
        private static IEnumerable<QueryCase> Calendars()
        {
            // The quarter's first day, in each calendar.
            yield return Query("calendar-start-of-quarter", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "startOfQuarter(PostingDate), startOfQuarter(PostedOn, 'gc'),"
                    + " startOfQuarter(PostingDate, 'et'), startOfQuarter(PostingDate, 'uq')",
            });

            // Every Ethiopian function over a date column, each a column of its own.
            yield return Query("calendar-ethiopian", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "year(PostingDate, 'et'), quarter(PostingDate, 'et'), month(PostingDate, 'et'),"
                    + " day(PostingDate, 'et'), week(PostingDate, 'et'), startOfYear(PostingDate, 'et'),"
                    + " startOfQuarter(PostingDate, 'et'), startOfMonth(PostingDate, 'et'),"
                    + " addYears(PostingDate, 1, 'et'), addMonths(PostingDate, 7, 'et'),"
                    + " diffYears(PostingDate, DueDate, 'et'), diffMonths(PostingDate, DueDate, 'et')",
            });

            // A date with a time of day, which is counted by its date and keeps its time when moved,
            // and an operand that is neither a column nor a parameter, which is given a name first.
            yield return Query("calendar-ethiopian-operands", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "month(PostedOn, 'et'), addMonths(PostedOn, -13, 'et'), diffMonths(PostedOn, DueOn, 'et'),"
                    + " day(addDays(PostingDate, Count), 'et')",
            });

            // The Umm al-Qura calendar at row grain: one lateral lookup per date operand, shared by
            // every function that reads the same date.
            yield return Query("calendar-uq-lateral-date", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "year(PostingDate, 'uq'), quarter(PostingDate, 'uq'), month(PostingDate, 'uq'),"
                    + " day(PostingDate, 'uq'), startOfMonth(PostingDate, 'uq')",
            });

            // A date with a time of day is sought by its date, given a name of its own; an addition
            // moves the operand itself and keeps its time.
            yield return Query("calendar-uq-lateral-datetime", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "month(PostedOn, 'uq'), addMonths(PostedOn, 1, 'uq'), day(addDays(PostedOn, 1), 'uq')",
            });

            // The functions that read a second month: the year's first, the quarter's first, and
            // the month a count of units away.
            yield return Query("calendar-uq-lateral-second-lookup", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "startOfYear(PostingDate, 'uq'), week(PostingDate, 'uq'), startOfQuarter(PostingDate, 'uq'),"
                    + " addMonths(PostingDate, Count, 'uq'), addYears(PostingDate, 1, 'uq'),"
                    + " addMonths(PostingDate, -1.5, 'uq')",
            });

            // A grouping key read off a lookup through a join, as a report groups by Hijri month.
            yield return Query("calendar-uq-lateral-key", new QuerySpec
            {
                Root = LedgerFixture.InvoiceLine,
                Aggregate = true,
                Select = "month(Invoice.PostingDate, 'uq'), sum(Amount)",
            });

            // Differences between a date the row holds and one it does not: both are lateral, and
            // the absent due date leaves its row with an absent answer.
            yield return Query("calendar-uq-lateral-mixed", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "diffMonths(PostingDate, today(), 'uq'), diffYears(DueDate, addYears(PostingDate, 2), 'uq'),"
                    + " diffMonths(PostedOn, DueOn, 'uq')",
            });

            // A semi-join's lookups belong to its own subquery, beside its own joins.
            yield return Query("calendar-uq-lateral-exists", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Exists(
                    "InvoiceLine",
                    "Invoice",
                    FilterTree.Leaf("month(Invoice.DueDate, 'uq') >= month(Invoice.PostingDate, 'uq')")),
            });

            // A call that reads no row is computed once, ahead of the statement: in the row filter.
            yield return Query("calendar-uq-hoisted-where", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id",
                Filter = FilterTree.Leaf("PostingDate >= startOfMonth(today(), 'uq')"),
            });

            // In the group filter.
            yield return Query("calendar-uq-hoisted-having", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count()",
                Having = FilterTree.Leaf("max(PostingDate) >= addMonths(startOfYear(today(), 'uq'), 10, 'uq')"),
            });

            // In an aggregation's condition, which is where a period total puts its boundary.
            yield return Query("calendar-uq-hoisted-condition", new QuerySpec
            {
                Root = LedgerFixture.InvoiceLine,
                Aggregate = true,
                Select = "sum(Amount, Invoice.PostingDate >= startOfMonth(today(), 'uq'))",
            });

            // In a grouping key, which still reads the column it groups by.
            yield return Query("calendar-uq-hoisted-key", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "if(PostingDate >= startOfQuarter(today(), 'uq'), 'this quarter', 'earlier'), count()",
            });

            // The same call twice, in two clauses: one variable.
            yield return Query("calendar-uq-hoisted-twice", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Select = "Id, diffDays(startOfYear(today(), 'uq'), PostingDate)",
                Filter = FilterTree.Leaf("PostingDate >= startOfYear(today(), 'UQ')"),
            });

            // An operand that aggregates cannot be applied to a row, so the lookups are written in
            // place, nesting one level per month they read.
            yield return Query("calendar-uq-scalar-select", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "month(max(PostingDate), 'uq'), startOfYear(min(PostingDate), 'uq'),"
                    + " diffMonths(min(PostingDate), max(PostingDate), 'uq'), addMonths(max(PostedOn), 1, 'uq')",
            });

            // In the group filter, beside a grouping key.
            yield return Query("calendar-uq-scalar-having", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count()",
                Having = FilterTree.Leaf("year(max(PostingDate), 'uq') = 1446"),
            });

            // In a grouped statement's ordering, which no position refuses.
            yield return Query("calendar-uq-scalar-order", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count()",
                OrderBy = "month(max(PostingDate), 'uq')",
            });

            // The Ethiopian calendar over aggregations, nested, in a grouped item and in the group
            // filter: no binding can name an operand at group grain, so each call names the operand
            // its pattern writes many times in a subquery of its own and writes it once.
            yield return Query("calendar-et-aggregated", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, startOfMonth(addMonths(max(PostingDate), 1, 'et'), 'et'), count()",
                Having = FilterTree.Leaf("addMonths(max(PostingDate), 1, 'et') > min(PostingDate)"),
            });
        }
    }
}
