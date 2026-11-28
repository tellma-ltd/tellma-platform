// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The diagnostics' locations in the clauses the caller wrote: a ceiling in the platform's
    ///     filter text at the last filter, else the last named filter, else the select, with no span;
    ///     the ordering a sent select stands in for at that select; a default's ceiling at its clause;
    ///     and anything else in the platform's own text a defect.
    /// </summary>
    public sealed class QueryDiagnosticsTests
    {
        /// <summary>A ceiling's code: the parameters.</summary>
        private const string Ceiling = "QX5007";

        /// <summary>A code outside the ceilings: an unknown name.</summary>
        private const string Unknown = "QX3001";

        /// <summary>The span the engine reported, inside text the caller never sent.</summary>
        private static readonly QueryexSpan Reported = new(3, 5);

        [Fact]
        public void A_ceiling_in_the_platforms_filter_text_is_reported_at_the_last_filter_else_the_last_named_filter_else_the_select()
        {
            Conjunct platform = new(ConjunctKind.Platform, -1);
            CompositionMap filters = Map([new(ConjunctKind.UserFilter, 0), new(ConjunctKind.UserFilter, 3), new(ConjunctKind.NamedFilter, 1), platform]);
            CompositionMap named = Map([new(ConjunctKind.NamedFilter, 0), new(ConjunctKind.NamedFilter, 2), platform]);
            CompositionMap none = Map([platform, platform]);

            Assert.Equal(("filters[3]", new QueryexSpan(0, 0)), Located(Ceiling, "Filter.And[3].Or[1]", filters));
            Assert.Equal(("namedFilters[2]", new QueryexSpan(0, 0)), Located(Ceiling, "Filter.And[2]", named));
            Assert.Equal(("Select", new QueryexSpan(0, 0)), Located(Ceiling, "Filter.And[1]", none));
        }

        [Fact]
        public void A_diagnostic_in_the_callers_filter_or_named_filter_keeps_its_span_under_the_requests_index()
        {
            CompositionMap map = Map([new(ConjunctKind.UserFilter, 2), new(ConjunctKind.NamedFilter, 0)]);

            Assert.Equal(("filters[2].Or[1]", Reported), Located(Unknown, "Filter.And[0].Or[1]", map));
            Assert.Equal(("namedFilters[0]", Reported), Located(Unknown, "Filter.And[1]", map));
        }

        [Fact]
        public void An_ordering_a_sent_select_stands_in_for_is_reported_at_that_select_and_a_default_ones_ceiling_at_the_select()
        {
            Assert.Equal(("Select", Reported), Located(Unknown, "OrderBy", Map([], selectSent: true, OrderingSource.Select)));
            Assert.Equal(("Select", new QueryexSpan(0, 0)), Located(Ceiling, "OrderBy", Map([], selectSent: false, OrderingSource.Select)));
            Assert.Equal(("OrderBy", new QueryexSpan(0, 0)), Located(Ceiling, "OrderBy", Map([], selectSent: true, OrderingSource.Default)));
            Assert.Equal(("OrderBy", Reported), Located(Unknown, "OrderBy", Map([], selectSent: true, OrderingSource.Sent)));
        }

        [Theory]
        [InlineData("Filter.And[0]")]
        [InlineData("Select")]
        [InlineData("OrderBy")]
        public void Anything_but_a_ceiling_in_the_platforms_own_text_is_a_defect(string location)
        {
            CompositionMap map = Map([new(ConjunctKind.Platform, -1)], selectSent: false, location == "OrderBy" ? OrderingSource.Select : OrderingSource.Default);

            InvalidOperationException defect = Assert.Throws<InvalidOperationException>(
                () => QueryDiagnostics.Locate([new QueryexDiagnostic(Unknown, Reported, location, [])], map, "qm.QmAccount"));

            Assert.Contains("qm.QmAccount", defect.Message, StringComparison.Ordinal);
        }

        /// <summary>A composition's map.</summary>
        /// <param name="conjuncts">The conjuncts' origins.</param>
        /// <param name="selectSent">Whether the caller sent the select.</param>
        /// <param name="ordering">Where the ordering came from.</param>
        /// <returns>The map.</returns>
        private static CompositionMap Map(Conjunct[] conjuncts, bool selectSent = true, OrderingSource ordering = OrderingSource.Sent)
        {
            return new CompositionMap(conjuncts, selectSent, ordering);
        }

        /// <summary>Locates one diagnostic, reported at the engine's location and span.</summary>
        /// <param name="code">The diagnostic's code.</param>
        /// <param name="location">The engine's location.</param>
        /// <param name="map">The composition's map.</param>
        /// <returns>The location and the span the caller is told.</returns>
        private static (string? Location, QueryexSpan Span) Located(string code, string location, CompositionMap map)
        {
            QueryexDiagnostic located = Assert.Single(QueryDiagnostics.Locate([new QueryexDiagnostic(code, Reported, location, [])], map, "qm.QmAccount"));
            return (located.Location, located.Span);
        }
    }
}
