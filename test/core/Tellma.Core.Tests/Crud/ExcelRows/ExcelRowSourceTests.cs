// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Crud.ExcelRows;
using Tellma.Core.Crud.Query;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Crud.Validation;

namespace Tellma.Core.Tests.Crud.ExcelRows
{
    /// <summary>
    ///     The Excel row source over the scripted batch: the read check on the first call, each
    ///     source's composition, the pages at the take ceiling with the count on the first alone, the
    ///     dependents riding each page and completed beyond the ceiling, the flat lookups in one
    ///     round trip, a lookup target's read decision, the traversal rule, and the queries refused
    ///     before any round trip.
    /// </summary>
    public sealed class ExcelRowSourceTests : IAsyncDisposable
    {
        /// <summary>The query model's account stack's take ceiling.</summary>
        private const int MaxTake = 100;

        /// <summary>The query model.</summary>
        private readonly QueryRig _rig = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The read check ---

        [Fact]
        public async Task Creating_a_row_source_runs_nothing_and_its_first_read_refuses_a_caller_without_the_read()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null));
            await using AsyncServiceScope scope = _rig.Scope();
            IExcelRowSource rows = Accounts(scope).ExcelRowSource(new ExportSource.All());
            Assert.Empty(_rig.Script.Batches);

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => PagesAsync(rows, Root("Id")));

            Assert.Equal((ForbiddenReason.NoGrant, QueryModel.Accounts, AccessActions.Read), (refused.Reason, refused.Resource, refused.Action));
            Assert.Empty(_rig.Script.Batches);
        }

        // --- The sources ---

        [Fact]
        public async Task A_by_query_source_conjoins_its_filters_its_named_filters_the_active_conjunct_its_search_and_the_access_filter_in_id_order()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 10"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1));
            ExportSource source = new ExportSource.ByQuery(["Code != ''", " "], ["priced"], "ab", OrderBy: null, Arguments: null, IncludeInactive: false);

            ScriptedStatement statement = await SingleRowsAsync(source, Root("Id"));

            FilterTree.AndNode filter = Assert.IsType<FilterTree.AndNode>(statement.Spec.Filter);
            Assert.Equal(
                ["Code != ''", QmAccountFilters.Priced, QueryPlanner.ActiveOnly, "or(startsWith(Code, @search) | contains(Name, @search))"],
                filter.Children.Take(4).Select(QueryRig.Render));
            Assert.Contains("Price > 10", QueryRig.Render(Assert.Single(filter.Children.Skip(4))), StringComparison.Ordinal);
            Assert.Equal(("Id", 0, MaxTake), (statement.Spec.OrderBy, statement.Spec.Skip, statement.Spec.Take));
            Assert.Empty(statement.Spec.Restrictions);
        }

        [Fact]
        public async Task A_by_query_source_that_includes_inactive_rows_and_names_an_ordering_reads_by_it_with_no_active_conjunct()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            ExportSource source = new ExportSource.ByQuery(null, null, null, "Code desc", null, IncludeInactive: true);

            ScriptedStatement sourceOrdered = await SingleRowsAsync(source, Root("Id"));
            ScriptedStatement queryOrdered = await SingleRowsAsync(source, Root("Id") with { OrderBy = "Name" });

            Assert.Null(sourceOrdered.Spec.Filter);
            Assert.Equal(("Code desc, Id", "Name, Id"), (sourceOrdered.Spec.OrderBy, queryOrdered.Spec.OrderBy));
        }

        [Fact]
        public async Task Every_ordering_of_a_page_and_its_dependents_is_ended_by_the_key()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            ExportSource source = new ExportSource.ByQuery(null, null, null, "Name", null, IncludeInactive: false);
            ExcelQuery lines = new(QueryModel.Lines, ["Id"], "Name", null, null, null, null, null);

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await PagesAsync(Accounts(scope).ExcelRowSource(source), Root("Id") with { Dependents = [lines] });

            ScriptedStatement[] reads = [.. Assert.Single(_rig.Script.Batches).Of(ScriptedKind.Rows)];
            Assert.Equal(["Name, Id", "Name, Id"], reads.Select(static read => read.Spec.OrderBy));
        }

        [Theory]
        [InlineData("Name", "Name, Id")]
        [InlineData("Id", "Id")]
        [InlineData("Id desc", "Id desc")]
        [InlineData("Name desc, id ASC", "Name desc, id ASC")]
        [InlineData("Id, Name", "Id, Name, Id")]
        [InlineData("coalesce(Code, Id)", "coalesce(Code, Id), Id")]
        [InlineData("Code = 'x, Id'", "Code = 'x, Id', Id")]
        public void An_ordering_that_does_not_end_with_the_key_is_ended_by_it(string orderBy, string expected)
        {
            Assert.Equal(expected, RowsNode.TieBroken(orderBy, "Id"));
        }

        [Fact]
        public async Task A_by_ids_source_restricts_on_its_distinct_ids_with_no_active_conjunct_and_every_row_reads_under_the_access_filter_alone()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 10"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement byIds = await SingleRowsAsync(new ExportSource.ByIds([3, 1, 3], null), Root("Id"));
            ScriptedStatement all = await SingleRowsAsync(new ExportSource.All(), Root("Id"));

            KeySetRestriction restriction = Assert.Single(byIds.Spec.Restrictions);
            Assert.Equal("Id", restriction.Path);
            Assert.Equal([3, 1], byIds.KeySet<IdList>().Select(static row => row.Id));
            Assert.Equal((QueryRig.Render(all.Spec.Filter), "Id"), (QueryRig.Render(byIds.Spec.Filter), byIds.Spec.OrderBy));
            Assert.Contains("Price > 10", QueryRig.Render(all.Spec.Filter), StringComparison.Ordinal);
            Assert.DoesNotContain(QueryPlanner.ActiveOnly, QueryRig.Render(byIds.Spec.Filter), StringComparison.Ordinal);
            Assert.Equal("Id", all.Spec.OrderBy);
            Assert.Empty(all.Spec.Restrictions);
        }

        [Fact]
        public async Task A_by_ids_source_beyond_the_key_range_or_with_no_id_reads_nothing_outside_the_range_check()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            await using AsyncServiceScope scope = _rig.Scope();

            _ = await Assert.ThrowsAsync<BadRequestException>(() => PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.ByIds([long.MaxValue], null)), Root("Id")));
            List<ExcelPage> empty = await PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.ByIds([], null)), Root("Id") with { CountCap = 10 });

            ExcelPage page = Assert.Single(empty);
            Assert.Equal((0, 0), (page.Rows.Count, page.Count));
            Assert.Empty(Assert.Single(_rig.Script.Batches).Of(ScriptedKind.Rows));
        }

        // --- The pages ---

        [Fact]
        public async Task Each_page_reads_the_take_ceiling_on_its_own_round_trip_with_the_capped_count_on_the_first_alone()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => new RowPage(Ids(statement.Spec.Skip == 0 ? MaxTake : 30), statement.Spec.Skip == 0 ? 130 : null, null, null));

            // A count cap beyond the stack's own, which binds a query alone.
            await using AsyncServiceScope scope = _rig.Scope();
            List<ExcelPage> pages = await PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.All()), Root("Id") with { CountCap = 100_000 });

            Assert.Equal([(MaxTake, 130), (30, null)], pages.Select(static page => (page.Rows.Count, page.Count)));
            ScriptedStatement[] reads = [.. _rig.Script.Batches.Select(static batch => Assert.Single(batch.Of(ScriptedKind.Rows)))];
            Assert.Equal([(0, MaxTake, 100_000), (MaxTake, MaxTake, null)], reads.Select(static read => (read.Spec.Skip, read.Spec.Take, Options(read).CountCap)));
            Assert.All(_rig.Script.Batches, static batch => Assert.Equal(BatchPurpose.Read, batch.Purpose));
        }

        [Fact]
        public async Task The_querys_take_caps_the_rows_across_the_pages()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => new RowPage(Ids(statement.Spec.Take!.Value), null, null, null));

            await using AsyncServiceScope scope = _rig.Scope();
            List<ExcelPage> pages = await PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.All()), Root("Id") with { Take = 150 });

            Assert.Equal([MaxTake, 50], pages.Select(static page => page.Rows.Count));
            Assert.Equal(2, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task Each_dependent_rides_its_owners_page_restricted_to_the_keys_the_owner_captured_at_every_depth()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => statement.Spec.Root.Name == QueryModel.Accounts
                ? new RowPage(ValidationRig.IntRows(["Price", "Id"], [7, 1], [8, 2]), null, null, null)
                : new RowPage(ValidationRig.IntRows(["AccountId", "Id"], [1, 10], [2, 11]), null, null, null));
            ExcelQuery lines = new(QueryModel.Lines, ["AccountId", "Id"], "AccountId, Id", null, null, null, null, null);

            await using AsyncServiceScope scope = _rig.Scope();
            ExcelPage page = Assert.Single(await PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.All()), Root("Price") with { Dependents = [lines] }));

            // --- The owner's key is read beside its columns, never answered among them ---
            Assert.Equal(["7", "8"], page.Rows.Select(Line));
            Assert.Equal(["1,10", "2,11"], Assert.Single(page.Dependents).Rows.Select(Line));

            // --- One round trip: the owner capturing its keys, the dependent restricted to them ---
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches);
            ScriptedStatement[] reads = [.. batch.Of(ScriptedKind.Rows)];
            Assert.Equal(2, reads.Length);
            Assert.True(Options(reads[0]).CaptureKeys);
            Assert.Equal("Price, Id", reads[0].Spec.Select);
            KeySetRestriction restriction = Assert.Single(reads[1].Spec.Restrictions);
            Assert.Equal(("AccountId", $"@tb{reads[0].Ordinal}_keys"), (restriction.Path, restriction.TableSource));
            Assert.Equal(("AccountId, Id", 0, MaxTake, false), (reads[1].Spec.OrderBy, reads[1].Spec.Skip, reads[1].Spec.Take, Options(reads[1]).CaptureKeys));
            Assert.Null(reads[1].Spec.Filter);
        }

        [Fact]
        public async Task A_dependent_that_reaches_the_ceiling_is_completed_by_follow_ups_over_the_owner_ids_before_its_page_is_yielded()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static statement => statement.Spec.Root.Name == QueryModel.Accounts
                ? new RowPage(Ids(2), null, null, null)
                : new RowPage(Ids(statement.Spec.Skip == 0 ? MaxTake : 5, from: statement.Spec.Skip!.Value), null, null, null));
            ExcelQuery lines = new(QueryModel.Lines, ["Id"], "AccountId, Id", null, null, null, null, null);

            await using AsyncServiceScope scope = _rig.Scope();
            ExcelPage page = Assert.Single(await PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.All()), Root("Id") with { Dependents = [lines] }));

            // --- The dependent's rows, complete and in order ---
            Assert.Equal(Enumerable.Range(1, MaxTake + 5).Select(static id => (int?)id), Assert.Single(page.Dependents).Rows.Select(static row => (int?)row[0]));

            // --- The follow-up: the next chunk over a bound list of the page's owner ids ---
            Assert.Equal(2, _rig.Script.RoundTrips);
            ScriptedStatement followUp = Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Rows));
            Assert.Equal((QueryModel.Lines, MaxTake, MaxTake), (followUp.Spec.Root.Name, followUp.Spec.Skip, followUp.Spec.Take));
            Assert.Equal("AccountId", Assert.Single(followUp.Spec.Restrictions).Path);
            Assert.Equal([1, 2], followUp.KeySet<IdList>().Select(static row => row.Id));
        }

        // --- The lookups ---

        [Fact]
        public async Task Several_lookups_read_in_one_round_trip_each_under_its_targets_decision_a_denied_one_reading_nothing()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null), (QueryModel.Tickets, AccessActions.Read, "Number > 0"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(4));
            ExcelQuery tickets = new(QueryModel.Tickets, ["Id"], null, new KeySetRestriction("Number", "@lookup"), [1L, 2, 2.0, 2.5, 1L << 40], null, null, null);
            ExcelQuery codes = new(QueryModel.Accounts, ["Id"], null, new KeySetRestriction("Code", "@lookup"), ["a", "A", "b"], null, null, null);
            ExcelQuery counters = new(QueryModel.Counters, ["Id"], null, new KeySetRestriction("Number", "@lookup"), [1], null, null, null);

            await using AsyncServiceScope scope = _rig.Scope();
            IReadOnlyList<IReadOnlyList<IReadOnlyList<object?>>> answers = await Accounts(scope).ExcelRowSource(new ExportSource.All()).ReadAsync([tickets, codes, counters], Token);

            // --- One round trip; the denied target composes nothing and answers no row ---
            Assert.Equal([1, 1, 0], answers.Select(static rows => rows.Count));
            ScriptedStatement[] reads = [.. Assert.Single(_rig.Script.Batches).Of(ScriptedKind.Rows)];
            Assert.Equal([QueryModel.Tickets, QueryModel.Accounts], reads.Select(static read => read.Spec.Root.Name));

            // --- Each restriction bound by its property's type, its values distinct as the store compares them ---
            Assert.Equal([1, 2], reads[0].KeySet<IdList>().Select(static row => row.Id));
            Assert.Equal(["a", "b"], reads[1].KeySet<StringList>().Select(static row => row.Id));
            Assert.Contains("Number > 0", QueryRig.Render(reads[0].Spec.Filter), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_sensitive_lookup_target_asks_for_the_step_up_a_direct_read_asks_for()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            _rig.Assurance = null;
            ExcelQuery counters = new(QueryModel.Counters, ["Number", "Id"], null, new KeySetRestriction("Number", "@lookup"), [1], null, null, null);

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => Accounts(scope).ExcelRowSource(new ExportSource.All()).ReadAsync([counters], Token));
        }

        // --- The rules ---

        [Fact]
        public async Task A_path_past_an_entitys_projection_is_refused_as_a_querys_is()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            await using AsyncServiceScope scope = _rig.Scope();
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(
                () => PagesAsync(Accounts(scope).ExcelRowSource(new ExportSource.All()), Root("Id", "CreatedBy.Email")));

            Assert.Equal((ForbiddenReason.RestrictedPath, CoreResources.User, "CreatedBy.Email"), (refused.Reason, refused.Resource, refused.Path));
        }

        [Fact]
        public async Task A_child_surfaces_row_source_reads_its_child_under_the_childs_read_with_the_surfaces_search()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 10"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(5));
            await using AsyncServiceScope scope = _rig.Scope();
            IChildQueryService children = scope.ServiceProvider.GetRequiredService<IChildQueryService>();
            IExcelRowSource rows = children.ExcelRowSource(QueryModel.Lines, new ExportSource.ByQuery(["Amount > 0"], null, "ab", null, null, false));

            ExcelPage page = Assert.Single(await PagesAsync(rows, new ExcelQuery(QueryModel.Lines, ["Id"], null, null, null, null, null, null)));

            Assert.Single(page.Rows);
            ScriptedStatement read = Assert.Single(Assert.Single(_rig.Script.Batches).Of(ScriptedKind.Rows));
            string filter = QueryRig.Render(read.Spec.Filter);
            Assert.Equal(QueryModel.Lines, read.Spec.Root.Name);
            Assert.StartsWith("and(Amount > 0 | or(contains(Name, @search))", filter, StringComparison.Ordinal);
            Assert.Contains("Price > 10", filter, StringComparison.Ordinal);
            _ = Assert.Throws<ArgumentException>(() => children.ExcelRowSource(QueryModel.Lines, new ExportSource.ByQuery(null, ["priced"], null, null, null, false)));
        }

        [Fact]
        public async Task A_query_the_row_source_does_not_read_is_refused_before_any_round_trip()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            await using AsyncServiceScope scope = _rig.Scope();
            IExcelRowSource rows = Accounts(scope).ExcelRowSource(new ExportSource.All());
            ExcelQuery tickets = new(QueryModel.Tickets, ["Id"], null, null, null, null, null, null);

            _ = Assert.Throws<ArgumentException>(() => rows.Pages(tickets, Token));
            _ = Assert.Throws<ArgumentException>(() => rows.Pages(Root("Id") with { Dependents = [tickets] }, Token));
            _ = Assert.Throws<ArgumentException>(() => rows.Pages(Root("Id") with { Dependents = [new ExcelQuery(QueryModel.Lines, ["Id"], null, null, null, Take: 5, null, null)] }, Token));
            _ = Assert.Throws<ArgumentException>(() => rows.Pages(Root(" "), Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => rows.ReadAsync([Root("Id") with { Dependents = [tickets] }], Token));
            _ = await Assert.ThrowsAsync<ArgumentException>(() => rows.ReadAsync([new ExcelQuery("qm.Nope", ["Id"], null, null, null, null, null, null)], Token));
            Assert.Empty(_rig.Script.Batches);
        }

        /// <summary>A root query of the account stack over some columns.</summary>
        /// <param name="columns">The columns.</param>
        /// <returns>The query.</returns>
        private static ExcelQuery Root(params string[] columns)
        {
            return new ExcelQuery(QueryModel.Accounts, columns, null, null, null, null, null, null);
        }

        /// <summary>A row's values, joined.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The line.</returns>
        private static string Line(IReadOnlyList<object?> row)
        {
            return string.Join(',', row);
        }

        /// <summary>A row set of consecutive ids.</summary>
        /// <param name="count">The rows.</param>
        /// <param name="from">The ids before the first.</param>
        /// <returns>The rows.</returns>
        private static QueryRowSet Ids(int count, int from = 0)
        {
            return ValidationRig.IntRows(["Id"], [.. Enumerable.Range(from + 1, count).Select(static id => new int?[] { id })]);
        }

        /// <summary>A row read's options.</summary>
        /// <param name="statement">The row read.</param>
        /// <returns>The options.</returns>
        private static RowQueryOptions Options(ScriptedStatement statement)
        {
            return Assert.IsType<RowQueryOptions>(statement.Options);
        }

        /// <summary>The account stack's service in a scope.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<QmAccount, int> Accounts(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<QmAccount, int>>();
        }

        /// <summary>Every page of a root query.</summary>
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

        /// <summary>Reads one page of the account stack's row source over a source, in a scope of its own, and returns its one row read.</summary>
        /// <param name="source">The source.</param>
        /// <param name="query">The root query.</param>
        /// <returns>The row read the page's batch held.</returns>
        private async Task<ScriptedStatement> SingleRowsAsync(ExportSource source, ExcelQuery query)
        {
            int before = _rig.Script.Batches.Count;
            await using AsyncServiceScope scope = _rig.Scope();
            _ = await PagesAsync(Accounts(scope).ExcelRowSource(source), query);
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches.Skip(before));
            return Assert.Single(batch.Of(ScriptedKind.Rows));
        }
    }
}
