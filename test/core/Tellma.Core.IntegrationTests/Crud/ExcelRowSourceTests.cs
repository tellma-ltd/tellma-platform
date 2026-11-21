// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Queryex;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The Excel row source against a real tenant database, through the real connect prologue,
    ///     runner and evaluator: one round trip per page of the take ceiling with every collection
    ///     riding it, one more for a collection beyond the ceiling, the first page's capped count, the
    ///     query's clauses and its default ordering by id, rows tying on the ordering read once each
    ///     across the cuts, several lookups in one round trip with an
    ///     unreadable target yielding nothing, a grant revoked between pages refusing the next, and a
    ///     child surface exporting the rows its query reads.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class ExcelRowSourceTests(ExcelRowSourceDatabase fixture) : IClassFixture<ExcelRowSourceDatabase>
    {
        /// <summary>The widgets' resource, whose take ceiling is 50.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The widget parts' entity, a child collection and a child query surface.</summary>
        private const string Parts = "fixture.WidgetPart";

        /// <summary>The widget part notes' entity, a grandchild collection.</summary>
        private const string Notes = "fixture.WidgetPartNote";

        /// <summary>The nodes' resource, a lookup target.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The active widgets above the first: ids 2 to 120 but every tenth.</summary>
        private const int ActiveAboveFirst = 107;

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- The round trips ---

        [Fact]
        public async Task A_page_with_its_collections_at_every_depth_is_one_round_trip_once_the_caches_are_warm()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportSource source = new ExportSource.ByIds([9, 3, 6, 3], null);
            ExcelQuery query = Tree(3);

            // The first scope warms the caller's connection and set, the discovery and the plans.
            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await PagesAsync(WidgetService(cold).ExcelRowSource(source), query);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (List<ExcelPage> pages, int roundTrips) = await MeasureAsync(scope, () => PagesAsync(WidgetService(scope).ExcelRowSource(source), query));

            // The named rows in id order, the repeated id once, each part and note under them.
            Assert.Equal(1, roundTrips);
            ExcelPage page = Assert.Single(pages);
            Assert.Equal([3, 6, 9], Column<int>(page, 0));
            ExcelPage parts = Assert.Single(page.Dependents);
            Assert.Equal([3, 6, 9], Column<int>(parts, 0));
            Assert.Equal([ExcelRowSourceDatabase.PartOf(3), ExcelRowSourceDatabase.PartOf(6), ExcelRowSourceDatabase.PartOf(9)], Column<int>(parts, 1));
            Assert.Equal(["n3", "n6", "n9"], Column<string>(Assert.Single(parts.Dependents), 2));
        }

        [Fact]
        public async Task A_stream_reads_one_round_trip_per_page_of_the_take_ceiling_and_counts_on_the_first_page_only()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportSource source = new ExportSource.ByQuery(["Id > 1"], null, null, null, null, IncludeInactive: false);
            ExcelQuery query = new(Widgets, ["Id", "Code"], null, null, null, null, CountCap: 100_000, null);

            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await PagesAsync(WidgetService(cold).ExcelRowSource(source), query);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (List<ExcelPage> pages, int roundTrips) = await MeasureAsync(scope, () => PagesAsync(WidgetService(scope).ExcelRowSource(source), query));

            // Pages of 50, 50 and 7, the short one ending the stream; the count past the stack's own cap.
            Assert.Equal(3, roundTrips);
            Assert.Equal([50, 50, 7], pages.Select(static page => page.Rows.Count));
            Assert.Equal([ActiveAboveFirst, null, null], pages.Select(static page => page.Count));
            List<int> ids = [.. pages.SelectMany(static page => Column<int>(page, 0))];
            Assert.Equal([.. Enumerable.Range(2, ExcelRowSourceDatabase.WidgetCount - 1).Where(static id => !ExcelRowSourceDatabase.IsInactive(id))], ids);
        }

        [Fact]
        public async Task A_collection_beyond_the_take_ceiling_is_completed_by_one_more_round_trip_its_rows_in_order()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportSource source = new ExportSource.ByIds([1, 3], null);
            ExcelQuery query = Tree(2);

            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await PagesAsync(WidgetService(cold).ExcelRowSource(source), query);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            (List<ExcelPage> pages, int roundTrips) = await MeasureAsync(scope, () => PagesAsync(WidgetService(scope).ExcelRowSource(source), query));

            // The first widget's 60 parts and the third's one, in id order across the two reads,
            // and the notes of every part of both.
            Assert.Equal(2, roundTrips);
            ExcelPage page = Assert.Single(pages);
            ExcelPage parts = Assert.Single(page.Dependents);
            int[] expected = [.. Enumerable.Range(1001, ExcelRowSourceDatabase.FirstWidgetParts), ExcelRowSourceDatabase.PartOf(3)];
            Assert.Equal(expected, Column<int>(parts, 1));
            Assert.Equal(["m1", "m2", "m3", "m4", "m5", "m6", "n3"], Column<string>(Assert.Single(parts.Dependents), 2));
        }

        // --- The clauses ---

        [Fact]
        public async Task An_empty_query_reads_the_active_rows_in_id_order_and_the_count_includes_inactive_rows_on_request()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExcelQuery query = new(Widgets, ["Id"], null, null, null, Take: 15, CountCap: 1_000, null);

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            ExcelPage active = Assert.Single(await PagesAsync(WidgetService(scope).ExcelRowSource(new ExportSource.ByQuery(null, null, null, null, null, false)), query));
            ExcelPage all = Assert.Single(await PagesAsync(WidgetService(scope).ExcelRowSource(new ExportSource.ByQuery(["Id > 1"], null, null, null, null, true)), query));

            Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16], Column<int>(active, 0));
            Assert.Equal(ExcelRowSourceDatabase.WidgetCount - (ExcelRowSourceDatabase.WidgetCount / 10), active.Count);
            Assert.Equal(ExcelRowSourceDatabase.WidgetCount - 1, all.Count);
            Assert.Equal(10, Column<int>(all, 0)[8]);
        }

        [Fact]
        public async Task Rows_that_tie_on_the_ordering_across_the_cuts_are_read_once_each()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));

            // Every widget is of one kind, and every part of the first widget under one owner, so
            // the orderings tie on every row, past the take ceiling of the root and of the parts.
            ExportSource source = new ExportSource.ByQuery(null, null, null, "Kind", null, IncludeInactive: true);
            ExcelQuery parts = new(Parts, ["WidgetId", "Id"], "WidgetId", null, null, null, null, null);
            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            List<ExcelPage> pages = await PagesAsync(WidgetService(scope).ExcelRowSource(source), new ExcelQuery(Widgets, ["Id"], null, null, null, null, null, [parts]));

            // --- Each widget once, none missing, the key ordering the ties ---
            Assert.Equal([50, 50, 20], pages.Select(static page => page.Rows.Count));
            Assert.Equal(Enumerable.Range(1, ExcelRowSourceDatabase.WidgetCount), pages.SelectMany(static page => Column<int>(page, 0)));

            // --- The first page's parts once each, by owner then id, across the chunk and its follow-ups ---
            List<int> pageParts = Column<int>(Assert.Single(pages[0].Dependents), 1);
            int[] others = [.. Enumerable.Range(1, 50).Where(ExcelRowSourceDatabase.HasPart).Select(ExcelRowSourceDatabase.PartOf)];
            Assert.Equal([.. Enumerable.Range(1001, ExcelRowSourceDatabase.FirstWidgetParts), .. others], pageParts);
        }

        // --- The lookups ---

        [Fact]
        public async Task Several_lookups_are_one_round_trip_and_a_target_the_caller_may_not_read_yields_no_rows()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Nodes, AccessActions.Read, null));
            (_, string widgetsOnly) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExcelQuery widgets = Lookup(Widgets, "Code", ["X002", "x001", "X010", "X999"]);
            ExcelQuery nodes = Lookup(Nodes, "Code", ["N2"]);

            await using (AsyncServiceScope cold = await fixture.Connect.ScopeAsync(subject))
            {
                _ = await WidgetService(cold).ExcelRowSource(new ExportSource.All()).ReadAsync([widgets, nodes], Token);
            }

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>> answers =
                await WidgetService(scope).ExcelRowSource(new ExportSource.All()).ReadAsync([widgets, nodes], Token);

            // The codes the collation matches, an inactive row included, and the node.
            Assert.Equal(1, access.RoundTrips - before);
            Assert.Equal(["X001", "X002", "X010"], answers[0].Select(static row => (string?)row[0]).Order(StringComparer.Ordinal));
            Assert.Equal(["N2"], answers[1].Select(static row => (string?)row[0]));

            await using AsyncServiceScope denied = await fixture.Connect.ScopeAsync(widgetsOnly);
            answers = await WidgetService(denied).ExcelRowSource(new ExportSource.All()).ReadAsync([widgets, nodes], Token);
            Assert.Equal(3, answers[0].Count);
            Assert.Empty(answers[1]);
        }

        // --- The access ---

        [Fact]
        public async Task A_grant_revoked_between_pages_refuses_the_next_page()
        {
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null));
            ExportSource source = new ExportSource.ByQuery(null, null, null, null, null, IncludeInactive: true);

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            await using IAsyncEnumerator<ExcelPage> pages = WidgetService(scope).ExcelRowSource(source)
                .Pages(new ExcelQuery(Widgets, ["Id"], null, null, null, null, null, null), Token)
                .GetAsyncEnumerator(Token);
            Assert.True(await pages.MoveNextAsync());
            Assert.Equal(50, pages.Current.Rows.Count);

            await fixture.RevokeAsync(userId);

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(async () => await pages.MoveNextAsync());
            Assert.Equal((Widgets, AccessActions.Read), (refused.Resource, refused.Action));
        }

        [Fact]
        public async Task A_child_surface_exports_the_rows_its_query_reads_under_the_owners_filtered_grant()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, "Price > 60"));
            ExportSource source = new ExportSource.ByQuery(["Ordinal = 1"], null, null, null, null, IncludeInactive: false);

            await using AsyncServiceScope scope = await fixture.Connect.ScopeAsync(subject);
            IChildQueryService children = scope.ServiceProvider.GetRequiredService<IChildQueryService>();
            RowPage queried = await children.QueryAsync(Parts, new QueryRequest { Select = "Id, Widget.Subject", Filters = ["Ordinal = 1"], OrderBy = "Id", Take = 50 }, Token);
            ExcelPage exported = Assert.Single(await PagesAsync(children.ExcelRowSource(Parts, source), new ExcelQuery(Parts, ["Id", "Widget.Subject"], null, null, null, null, null, null)));

            // The parts of the widgets priced above 60, inactive widgets' included: a child surface has no activatable conjunct.
            int[] expected = [.. Enumerable.Range(61, 60).Where(ExcelRowSourceDatabase.HasPart).Select(ExcelRowSourceDatabase.PartOf)];
            Assert.Equal(expected, (int[])queried.Rows.GetBuffer(0));
            Assert.Equal(expected, Column<int>(exported, 0));
            Assert.Equal((string?[])queried.Rows.GetBuffer(1), Column<string>(exported, 1));
        }

        /// <summary>The widgets with their parts and the parts' notes, each collection ordered by id.</summary>
        /// <param name="take">The most widgets.</param>
        /// <returns>The query.</returns>
        private static ExcelQuery Tree(int take)
        {
            ExcelQuery notes = new(Notes, ["WidgetPartId", "Id", "Text"], null, null, null, null, null, null);
            ExcelQuery parts = new(Parts, ["WidgetId", "Id"], null, null, null, null, null, [notes]);
            return new ExcelQuery(Widgets, ["Id", "Code"], null, null, null, take, null, [parts]);
        }

        /// <summary>A lookup of a target's rows by one column's values.</summary>
        /// <param name="entity">The target.</param>
        /// <param name="column">The column the values match.</param>
        /// <param name="values">The values.</param>
        /// <returns>The query.</returns>
        private static ExcelQuery Lookup(string entity, string column, IReadOnlyList<object> values)
        {
            return new ExcelQuery(entity, [column, "Id"], null, new KeySetRestriction(column, "@lookup"), values, null, null, null);
        }

        /// <summary>Reads every page of a stream.</summary>
        /// <param name="rows">The row source.</param>
        /// <param name="query">The root query.</param>
        /// <returns>The pages.</returns>
        private static async Task<List<ExcelPage>> PagesAsync(IExcelRowSource rows, ExcelQuery query)
        {
            List<ExcelPage> pages = [];
            await foreach (ExcelPage page in rows.Pages(query, Token))
            {
                pages.Add(page);
            }

            return pages;
        }

        /// <summary>Runs a read and counts the round trips it took on the scope's data-access counters.</summary>
        /// <typeparam name="T">The read's result.</typeparam>
        /// <param name="scope">The scope.</param>
        /// <param name="read">The read.</param>
        /// <returns>The result and the round trips.</returns>
        private static async Task<(T Result, int RoundTrips)> MeasureAsync<T>(AsyncServiceScope scope, Func<Task<T>> read)
        {
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            T result = await read();
            return (result, access.RoundTrips - before);
        }

        /// <summary>One column's values of a page's rows.</summary>
        /// <typeparam name="T">The column's type.</typeparam>
        /// <param name="page">The page.</param>
        /// <param name="column">The column.</param>
        /// <returns>The values.</returns>
        private static List<T?> Column<T>(ExcelPage page, int column)
        {
            return [.. page.Rows.Select(row => (T?)row[column])];
        }

        /// <summary>A scope's widget service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Widget, int> WidgetService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Widget, int>>();
        }
    }
}
