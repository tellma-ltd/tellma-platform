// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Queryex;
using Tellma.Queryex.Testing.Schema;

namespace Tellma.Queryex.Testing.Corpus
{
    /// <summary>The queries that carry measures beside their select list.</summary>
    public static partial class QueryCorpus
    {
        /// <summary>The grouped queries whose measures compile, and the partial columns they add.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> Measures()
        {
            // An average is a sum over a count of the same values; the measure's sum is the first
            // measure's, so it is one partial, and the count of values is not the count of rows.
            yield return Query("measure-average-split", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name",
                Measures = "sum(Amount), sum(Amount) / count(), avg(Amount)",
            });

            // A partial the select list already computes is read from the select item's column,
            // however it is spelled.
            yield return Query("measure-reuses-select", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, SUM(amount), max(PostingDate)",
                Measures = "sum(Amount) * 2, max(PostingDate), min(PostingDate)",
            });

            // A condition belongs to its aggregation: the same value under two conditions is two
            // partials, and an average's condition reaches both halves.
            yield return Query("measure-conditional", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name",
                Measures = "sum(Amount, IsPosted) - sum(Amount, not IsPosted), avg(Rate, IsPosted), count(Rate, IsPosted)",
            });

            // The subset a host computes outside the aggregations: conditionals, rounding, calendar
            // components and day counts, and the identity conversions.
            yield return Query("measure-subset", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name",
                Measures = "if(sum(Amount) > 0 and count() in (1, 2, 3), round(sum(Amount) / count(), 2), coalesce(max(Rate), -1)),"
                    + " year(max(PostingDate)) * 100 + month(max(PostingDate), 'gc'),"
                    + " diffDays(min(PostingDate), max(DueDate)),"
                    + " cast(abs(min(Amount)), 'numeric'), coalesce(max(DueDate), cast(null, 'date')),"
                    + " floor(sum(Amount)) + ceiling(min(Count) * 1.5), max(Code), min(Memo), max(IsPosted)",
            });

            // The other clauses are unaffected: the group-level predicate, the ordering and the
            // paging run in the statement over the groups, and the partials follow every select item.
            yield return Query("measure-beside-having", new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name, count()",
                Having = FilterTree.Leaf("sum(Amount) > -1000"),
                OrderBy = "Centre.Name",
                Take = 10,
                Measures = "count(), max(Amount) - min(Amount)",
            });

            // An omitted parameter folds inside a partial's condition as in any aggregation. Partials
            // are told apart as written, so the folded condition stays a partial of its own beside
            // the unconditional one, and a declared parameter left in still binds inside its partial.
            yield return Omitting("measure-omitted-condition", ["Customer"], new QuerySpec
            {
                Root = LedgerFixture.Invoice,
                Aggregate = true,
                Select = "Centre.Name",
                Measures = "sum(Amount, CustomerId = @Customer), sum(Amount), count(Amount, PostingDate <= @AsOf)",
            });
        }

        /// <summary>The measures that are refused, each pinned to the item and node it is reported at.</summary>
        /// <returns>The cases.</returns>
        private static IEnumerable<QueryCase> MeasureRefusals()
        {
            // A stray path is the measure rule's alone: the select list's mixing rule never fires
            // beside it.
            yield return Refused("refuse-measure-stray-path", "sum(Amount) + Rate", Site("QX4011", "Measures[0]", 0, 18));

            // The site is the item's own, with the span inside the whole text.
            yield return Refused("refuse-measure-path-only", "sum(Amount), Amount * 2", Site("QX4011", "Measures[1]", 13, 10));
            yield return Refused("refuse-measure-no-aggregation", "count(), 1 + 2", Site("QX4011", "Measures[1]", 9, 5));

            // Outside the subset, each refusal names the construct at the node it is.
            yield return Refused("refuse-measure-remainder", "sum(Amount) % 2", Construct(0, 15, "%"));
            yield return Refused("refuse-measure-concatenation", "max(Code) || 'x'", Construct(0, 16, "||"));
            yield return Refused("refuse-measure-text-equality", "if(max(Code) = 'A', 1, 0)", Construct(3, 15, "text comparison"));
            yield return Refused("refuse-measure-text-membership", "if(max(Memo) in ('a', 'b'), 1, 0)", Construct(3, 23, "text comparison"));
            yield return Refused("refuse-measure-position-equality", "if(max(AltNode) = min(AltNode), 1, 0)", Construct(3, 27, "text comparison"));
            yield return Refused("refuse-measure-zone", "local(max(PostedAt))", Construct(0, 20, "local"));
            yield return Refused("refuse-measure-conversion", "cast(sum(Amount), 'string')", Construct(0, 27, "cast"));
            yield return Refused("refuse-measure-instant-component", "month(max(PostedOn))", Construct(0, 20, "month"));
            yield return Refused("refuse-measure-other-calendar", "year(max(PostingDate), 'uq')", Construct(0, 28, "year"));
            yield return Refused("refuse-measure-text-function", "upper(max(Code))", Construct(0, 16, "upper"));
            yield return Refused("refuse-measure-instant-difference", "diffDays(min(PostedOn), max(PostedOn))", Construct(0, 38, "diffDays"));
            yield return Refused("refuse-measure-date-arithmetic", "addDays(max(PostingDate), 1)", Construct(0, 28, "addDays"));
            yield return Refused("refuse-measure-identifier", "if(count() > 0, cast(null, 'guid'), cast(null, 'guid'))", Construct(0, 55, "guid"));

            // Only the refused node is reported, in the item it sits in.
            yield return Refused(
                "refuse-measure-nested",
                "sum(Amount) / count(), 2 * (max(Rate) % 3)",
                Site("QX4010", "Measures[1]", 28, 13, ("construct", "%")));
        }

        /// <summary>A grouped query whose only measure list is refused.</summary>
        /// <param name="id">The case's name.</param>
        /// <param name="measures">The measures.</param>
        /// <param name="site">The one diagnostic expected, exactly.</param>
        /// <returns>The case.</returns>
        private static QueryCase Refused(string id, string measures, DiagnosticSite site)
        {
            return new QueryCase
            {
                Id = id,
                Spec = new QuerySpec
                {
                    Root = LedgerFixture.Invoice,
                    Aggregate = true,
                    Select = "Centre.Name",
                    Measures = measures,
                },
                Diagnostics = [site.Code],
                Sites = [site],
            };
        }

        /// <summary>A construct refused in the first measure.</summary>
        /// <param name="start">Where its node starts.</param>
        /// <param name="length">How long its node is.</param>
        /// <param name="construct">The construct reported.</param>
        /// <returns>The expectation.</returns>
        private static DiagnosticSite Construct(int start, int length, string construct)
        {
            return Site("QX4010", "Measures[0]", start, length, ("construct", construct));
        }
    }
}
