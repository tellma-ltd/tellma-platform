// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Query;
using Tellma.Core.Data;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Query
{
    /// <summary>
    ///     The query operation over the scripted batch: the composed filter and its order, the
    ///     defaults, the options, the pipeline's ceilings, the diagnostics located in the caller's
    ///     clauses, the arguments, the traversal rule with its projection, owner path and trusted
    ///     trees, the select-type rule, the arithmetic mapping, the operation's name and
    ///     instruments, the child surfaces and the pipeline behind a service.
    /// </summary>
    public sealed class QueryOperationTests : IAsyncDisposable
    {
        /// <summary>The query model.</summary>
        private readonly QueryRig _rig = new();

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- The composition ---

        [Fact]
        public async Task The_filter_conjoins_the_callers_filters_the_named_filters_the_active_conjunct_the_search_and_the_access_filter_in_order()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 10"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1));

            ScriptedStatement statement = await RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["Code != ''", " ", "Price < 100"],
                NamedFilters = ["priced"],
                Search = "ab",
            });

            FilterTree.AndNode filter = Assert.IsType<FilterTree.AndNode>(Spec(statement).Filter);
            Assert.Equal(
                ["Code != ''", "Price < 100", QmAccountFilters.Priced, QueryPlanner.ActiveOnly, "or(startsWith(Code, @search) | contains(Name, @search))"],
                filter.Children.Take(5).Select(QueryRig.Render));
            Assert.Contains("Price > 10", QueryRig.Render(Assert.Single(filter.Children.Skip(5))), StringComparison.Ordinal);
            Assert.Equal(["search"], statement.Arguments!.Declarations.Select(static declaration => declaration.Name));
        }

        [Fact]
        public async Task An_empty_conjunct_is_dropped_and_a_query_with_none_has_no_filter()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null), (QueryModel.Tickets, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement inactive = await RowsAsync(QueryModel.Accounts, new QueryRequest { IncludeInactive = true });
            ScriptedStatement plain = await RowsAsync(QueryModel.Tickets, new QueryRequest());

            Assert.Null(Spec(inactive).Filter);
            Assert.Null(Spec(plain).Filter);
        }

        [Fact]
        public async Task The_ordering_is_the_callers_else_the_stacks_else_the_id_descending_and_an_aggregate_orders_by_its_select()
        {
            _rig.Grant(("*", AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            Assert.Equal("Name", Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest { OrderBy = "Name" })).OrderBy);
            Assert.Equal("Code", Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest())).OrderBy);
            Assert.Equal("Id desc", Spec(await RowsAsync(QueryModel.Tickets, new QueryRequest())).OrderBy);
            Assert.Equal("Id desc", Spec(await RowsAsync(QueryModel.Lines, new QueryRequest())).OrderBy);
            Assert.Equal("Code, count()", Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest { Aggregate = true, Select = "Code, count()" })).OrderBy);
        }

        [Fact]
        public async Task The_default_select_reads_only_the_twins_the_tenants_languages_admit()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            string primary = Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest())).Select;
            _rig.Shape = MultilingualShape.All;
            string all = Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest())).Select;

            Assert.Equal("Id, Name, Code, IsActive, CreatedAt, CreatedById, ModifiedAt, ModifiedById", primary);
            Assert.Equal("Id, Name, Name2, Name3, Code, IsActive, CreatedAt, CreatedById, ModifiedAt, ModifiedById", all);
        }

        [Fact]
        public async Task The_count_is_capped_by_the_stack_and_the_ancestors_of_a_tree_read_through_the_access_filter_alone()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null), (QueryModel.Nodes, AccessActions.Read, "Code != 'x'"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement accounts = await RowsAsync(QueryModel.Accounts, new QueryRequest { IncludeCount = true, IncludeAncestors = true, Skip = 10, Take = 20 });
            ScriptedStatement nodes = await RowsAsync(QueryModel.Nodes, new QueryRequest { IncludeCount = true, IncludeAncestors = true, Filters = ["Name != ''"] });

            RowQueryOptions flat = Options(accounts);
            Assert.Equal((500, false, false), (flat.CountCap, flat.IncludeAncestors, flat.CaptureKeys));
            Assert.Null(flat.AncestorsFilter);
            Assert.Equal((10, 20), (Spec(accounts).Skip, Spec(accounts).Take));

            RowQueryOptions tree = Options(nodes);
            FilterTree.AndNode filter = Assert.IsType<FilterTree.AndNode>(Spec(nodes).Filter);
            Assert.Equal((10_000, true, true), (tree.CountCap, tree.IncludeAncestors, tree.CaptureKeys));
            Assert.Same(filter.Children[^1], tree.AncestorsFilter);
            Assert.Contains("Code != 'x'", QueryRig.Render(tree.AncestorsFilter), StringComparison.Ordinal);
        }

        // --- The pipeline's ceilings and the diagnostics' locations ---

        [Fact]
        public async Task The_batch_compiles_under_the_pipelines_ceilings_which_admit_more_than_the_defaults_and_refuse_beyond_their_own()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            string within = "Id in (" + string.Join(", ", Enumerable.Range(1, QueryexLimits.Default.MaxListItems + 1)) + ")";
            string beyond = "Id in (" + string.Join(", ", Enumerable.Range(1, QueryexProfiles.PipelineLimits.MaxListItems + 1)) + ")";

            ScriptedStatement admitted = await RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = [within] });
            InvalidQueryException refused = await Assert.ThrowsAsync<InvalidQueryException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = ["", beyond] }));

            Assert.Same(QueryexProfiles.PipelineLimits, admitted.Batch.Limits);
            QueryexDiagnostic ceiling = Assert.Single(refused.Diagnostics);
            Assert.StartsWith("QX5", ceiling.Code, StringComparison.Ordinal);
            Assert.Equal("filters[1]", ceiling.Location);
            Assert.Equal("limit", Assert.Single(ceiling.Arguments).Key);
        }

        [Fact]
        public async Task A_diagnostic_names_the_filter_or_named_filter_it_came_from_and_the_clause_the_caller_wrote()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            InvalidQueryException filters = await Assert.ThrowsAsync<InvalidQueryException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["", "Price > 0", "Nope = 1"],
                NamedFilters = ["priced"],
                Select = "Id, Nope",
                OrderBy = "Code, Nope",
            }));

            Assert.Equal(["OrderBy", "Select", "filters[2]"], filters.Diagnostics.Select(static diagnostic => diagnostic.Location).Order(StringComparer.Ordinal));
            Assert.Equal(new QueryexSpan(4, 4), filters.Diagnostics.Single(static diagnostic => diagnostic.Location == "Select").Span);
            Assert.Equal(new QueryexSpan(6, 4), filters.Diagnostics.Single(static diagnostic => diagnostic.Location == "OrderBy").Span);
        }

        [Fact]
        public async Task An_aggregates_ordering_taken_from_its_select_reports_at_the_select_once()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));

            InvalidQueryException refused = await Assert.ThrowsAsync<InvalidQueryException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest { Aggregate = true, Select = "Code, Nope" }));

            QueryexDiagnostic diagnostic = Assert.Single(refused.Diagnostics);
            Assert.Equal(("Select", new QueryexSpan(6, 4)), (diagnostic.Location, diagnostic.Span));
        }

        [Fact]
        public async Task An_aggregate_without_a_select_groups_and_orders_by_the_surfaces_default_select()
        {
            _rig.Grant(("*", AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            QuerySpec accounts = Spec(await RowsAsync(QueryModel.Accounts, new QueryRequest { Aggregate = true }));
            QuerySpec nodes = Spec(await RowsAsync(QueryModel.Nodes, new QueryRequest { Aggregate = true }));

            // Both compiled as grouped queries before the batch was handed over.
            Assert.Equal(Surface(QueryModel.Accounts).DefaultSelectFor(_rig.Schema, accounts.Root), accounts.Select);
            Assert.Equal(Surface(QueryModel.Nodes).DefaultSelectFor(_rig.Schema, nodes.Root), nodes.Select);
            QuerySpec[] specs = [accounts, nodes];
            Assert.All(specs, static spec => Assert.Equal((true, spec.Select), (spec.Aggregate, spec.OrderBy)));
        }

        [Fact]
        public async Task A_ceiling_the_platforms_filter_text_crosses_is_reported_at_the_callers_last_filter_with_no_span()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            // The page's skip and take are two slots of the statement, so the caller's parameters
            // fill the ceiling with two to spare.
            int half = (QueryexProfiles.PipelineLimits.MaxParameters - 2) / 2;
            string first = "Id in (" + string.Join(", ", Enumerable.Range(0, half).Select(static index => string.Create(CultureInfo.InvariantCulture, $"@a{index}"))) + ")";
            string second = "Id in (" + string.Join(", ", Enumerable.Range(0, half).Select(static index => string.Create(CultureInfo.InvariantCulture, $"@b{index}"))) + ")";

            // The caller's own parameters reach the ceiling; the split search's three cross it in the platform's conjunct.
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = [first, second] });
            InvalidQueryException refused = await Assert.ThrowsAsync<InvalidQueryException>(
                () => RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = [first, "", second], Search = "C - ab" }));

            QueryexDiagnostic ceiling = Assert.Single(refused.Diagnostics);
            Assert.Equal(("QX5007", "filters[2]", new QueryexSpan(0, 0)), (ceiling.Code, ceiling.Location, ceiling.Span));
        }

        [Fact]
        public async Task A_platform_text_that_does_not_compile_is_a_defect_not_the_callers_error()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            await using AsyncServiceScope scope = _rig.Scope();

            InvalidOperationException defect = await Assert.ThrowsAsync<InvalidOperationException>(
                () => Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest { Search = "ab" }, static _ => FilterTree.Leaf("Nope = 1"), Token));

            Assert.Contains(QueryModel.Accounts, defect.Message, StringComparison.Ordinal);
            Assert.Empty(_rig.Script.Batches.SelectMany(static batch => batch.Of(ScriptedKind.Rows)));
        }

        // --- The arguments ---

        [Fact]
        public async Task An_argument_converts_to_its_parameters_type_a_null_or_a_missing_one_binds_null_and_a_wrong_one_is_refused()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement bound = await RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["Price > @min and (Code = @code or @code = null) and Name != @name"],
                Arguments = Arguments("""{ "MIN": "12.5", "code": null }"""),
            });
            BadRequestException refused = await Assert.ThrowsAsync<BadRequestException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["Price > @min"],
                Arguments = Arguments("""{ "min": "twelve" }"""),
            }));

            QueryArguments arguments = bound.Arguments!;
            Assert.Equal(
                [("min", QueryexType.QxNumeric), ("code", QueryexType.QxString), ("name", QueryexType.QxString)],
                arguments.Declarations.Select(static declaration => (declaration.Name, declaration.Type)));
            Assert.Equal(12.5m, arguments.Values["min"]);
            Assert.Null(arguments.Values["code"]);
            Assert.Null(arguments.Values["name"]);
            Assert.Equal("min", refused.Arguments["parameter"]);
        }

        [Fact]
        public async Task A_callers_parameter_named_as_the_searchs_is_refused_while_a_search_applies()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            BadRequestException refused = await Assert.ThrowsAsync<BadRequestException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["Code = @Search"],
                Search = "ab",
                Arguments = Arguments("""{ "search": "x" }"""),
            }));

            // A name this text's lowering does not bind is refused alike, so the outcome never turns on the text.
            BadRequestException unbound = await Assert.ThrowsAsync<BadRequestException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest
            {
                Filters = ["Code = @left"],
                Search = "cash",
                Arguments = Arguments("""{ "left": "x" }"""),
            }));
            ScriptedStatement alone = await RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = ["Code = @search"], Arguments = Arguments("""{ "search": "x" }""") });

            Assert.Equal("Search", refused.Arguments["parameter"]);
            Assert.Equal("left", unbound.Arguments["parameter"]);
            Assert.Equal("x", alone.Arguments!.Values["search"]);
        }

        [Fact]
        public async Task A_search_no_branch_applies_to_matches_no_row_and_still_compiles()
        {
            _rig.Grant((QueryModel.Counters, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement statement = await RowsAsync(QueryModel.Counters, new QueryRequest { Search = "abc" });

            Assert.Equal("and(false)", QueryRig.Render(Spec(statement).Filter));
            Assert.Empty(statement.Arguments!.Declarations);
        }

        // --- The traversal rule ---

        [Fact]
        public async Task A_path_into_an_entity_read_under_no_grant_reaches_its_projection_only()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest { Select = "Id, CreatedBy.Name", Filters = ["CreatedBy.Id > 0"], OrderBy = "CreatedBy.Name" });
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest { Select = "Id, CreatedBy.Email" }));

            Assert.Equal((ForbiddenReason.RestrictedPath, CoreResources.User, AccessActions.Read, "CreatedBy.Email"), (refused.Reason, refused.Resource, refused.Action, refused.Path));
            Assert.Equal("restricted-path", refused.Arguments["reason"]);
        }

        [Theory]
        [InlineData(CoreResources.User, null, true)]
        [InlineData("*", null, true)]
        [InlineData(CoreResources.User, "Id > 0", false)]
        public async Task Every_column_of_an_entity_is_reachable_under_an_unrestricted_read_only(string resource, string? filter, bool reachable)
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null), (resource, AccessActions.Read, filter));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            Task query = RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = ["CreatedBy.Email != ''"] });

            if (reachable)
            {
                await query;
            }
            else
            {
                ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => query);
                Assert.Equal("CreatedBy.Email", refused.Path);
            }
        }

        [Fact]
        public async Task A_named_filter_is_trusted_while_the_same_text_from_the_caller_is_checked()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest { NamedFilters = ["by-creator-email"] });
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = [QmAccountFilters.ByCreatorEmail] }));

            Assert.Equal(ForbiddenReason.RestrictedPath, refused.Reason);
        }

        [Fact]
        public async Task From_a_child_root_without_a_grant_of_its_own_the_owner_reads_as_the_root()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 0"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement statement = await RowsAsync(QueryModel.Lines, new QueryRequest { Select = "Id, Account.Price", Filters = ["Account.Code != ''"] });
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Lines, new QueryRequest { Filters = ["Account.CreatedBy.Email != ''"] }));

            // The child's decision composes the owner's: its filter reads the owner's row.
            Assert.Contains("via Account(or(Price > 0", QueryRig.Render(Spec(statement).Filter), StringComparison.Ordinal);
            Assert.Equal((CoreResources.User, "Account.CreatedBy.Email"), (refused.Resource, refused.Path));
        }

        [Fact]
        public async Task From_a_child_root_with_a_grant_of_its_own_the_owner_is_another_entity()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, "Price > 0"), (QueryModel.Lines, AccessActions.Read, "Amount > 0"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            _ = await RowsAsync(QueryModel.Lines, new QueryRequest { Filters = ["Account.Code != ''"] });
            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Lines, new QueryRequest { Filters = ["Account.Price > 0"] }));

            Assert.Equal((QueryModel.Accounts, "Account.Price"), (refused.Resource, refused.Path));
        }

        [Fact]
        public async Task A_path_reading_a_sensitive_entity_beyond_its_projection_asks_for_the_step_up_a_direct_read_asks_for()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            _rig.Assurance = null;

            // Below the bar: the direct read and the path past the projection ask alike; the projection asks nothing.
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => RowsAsync(QueryModel.Counters, new QueryRequest()));
            StepUpRequiredException traversed = await Assert.ThrowsAsync<StepUpRequiredException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Label" }));
            _ = await RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Number", Filters = ["Counter.Id > 0"] });

            // At the bar, the same path reads.
            _rig.Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, QueryRig.Now, QueryRig.Now);
            _ = await RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Label" });

            Assert.Equal(StepUpBar.DefaultAcr, traversed.Acr);
        }

        [Fact]
        public async Task A_path_into_a_sensitive_entity_under_a_filtered_grant_is_restricted_before_any_step_up()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, "Number > 0"));
            _rig.Assurance = null;

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Label" }));

            Assert.Equal((ForbiddenReason.RestrictedPath, QueryModel.Counters, "Counter.Label"), (refused.Reason, refused.Resource, refused.Path));
        }

        // --- The tree position a hierarchy predicate reads ---

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_hierarchy_predicate_over_a_path_reads_the_trees_position_which_an_absent_or_filtered_grant_refuses(bool filtered)
        {
            (string, string, string?)[] grants = filtered
                ? [(QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Nodes, AccessActions.Read, "Code != ''")]
                : [(QueryModel.Tickets, AccessActions.Read, null)];
            _rig.Grant(grants);
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            // The key path alone reads the projection, which any grant on the root reaches.
            _ = await RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["Folder.Id > 0"] });
            ForbiddenException below = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["descendantOf(Folder.Id, 1)"] }));
            ForbiddenException above = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["ancestorOf(Folder.Id, 2, 3)"] }));

            // Refused at the position the predicate tests, a column of the node the text never names.
            ForbiddenException[] refusals = [below, above];
            Assert.All(refusals, static refused => Assert.Equal((ForbiddenReason.RestrictedPath, QueryModel.Nodes, "Folder.Node"), (refused.Reason, refused.Resource, refused.Path)));
        }

        [Fact]
        public async Task A_hierarchy_predicate_over_a_path_into_a_sensitive_tree_asks_for_the_step_up_and_reads_at_the_bar()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Nodes, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());
            _rig.Assurance = null;

            // Below the bar the projection reads, while the position asks for what a direct read asks.
            _ = await RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["Folder.Id > 0"] });
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => RowsAsync(QueryModel.Nodes, new QueryRequest()));
            _ = await Assert.ThrowsAsync<StepUpRequiredException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["descendantOf(Folder.Id, 1)"] }));

            _rig.Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, QueryRig.Now, QueryRig.Now);
            ScriptedStatement statement = await RowsAsync(QueryModel.Tickets, new QueryRequest { Filters = ["descendantOf(Folder.Id, 1)"] });

            Assert.Contains("descendantOf(Folder.Id, 1)", QueryRig.Render(Spec(statement).Filter), StringComparison.Ordinal);
        }

        [Fact]
        public async Task From_a_child_root_without_a_grant_of_its_own_a_hierarchy_predicate_reads_the_owners_position()
        {
            _rig.Grant((QueryModel.Nodes, AccessActions.Read, "Code != ''"));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            ScriptedStatement statement = await RowsAsync(QueryModel.Notes, new QueryRequest { Filters = ["descendantOf(Node.Id, 1)"] });

            // Every note's node is one the caller may read, so its position is the root's own.
            Assert.Contains("descendantOf(Node.Id, 1)", QueryRig.Render(Spec(statement).Filter), StringComparison.Ordinal);
        }

        // --- The re-check of a restriction a stale set decided ---

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_path_refused_by_a_denial_or_a_filter_from_a_set_past_the_fast_deny_window_is_re_checked_once_and_reads_the_grant_found(bool filtered)
        {
            (string, string, string?)[] grants = filtered
                ? [(QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, "Number > 0")]
                : [(QueryModel.Tickets, AccessActions.Read, null)];
            _rig.Grant(grants);
            _rig.GrantOnRecheck((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(7));
            _rig.Clock.Advance(TimeSpan.FromMinutes(1));

            await using AsyncServiceScope scope = _rig.Scope();
            RowPage page = await Operation(scope).RunAsync(Surface(QueryModel.Tickets), new QueryRequest { Select = "Id, Counter.Label" }, null, Token);

            Assert.Equal(1, page.Rows.RowCount);
            Assert.Equal(1, _rig.Revalidations);
            Assert.Single(_rig.Script.Batches.SelectMany(static batch => batch.Of(ScriptedKind.Rows)));
        }

        [Theory]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        public async Task A_path_refused_by_a_fresh_restriction_or_by_one_the_re_check_confirms_stays_refused(bool stale, bool grantFound, bool filtered)
        {
            (string, string, string?)[] grants = filtered
                ? [(QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, "Number > 0")]
                : [(QueryModel.Tickets, AccessActions.Read, null)];
            _rig.Grant(grants);
            if (grantFound)
            {
                _rig.GrantOnRecheck((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            }

            if (stale)
            {
                _rig.Clock.Advance(TimeSpan.FromMinutes(1));
            }

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Label" }));

            // A fresh restriction is trusted with no round trip; a stale one is re-checked once.
            Assert.Equal((ForbiddenReason.RestrictedPath, QueryModel.Counters), (refused.Reason, refused.Resource));
            Assert.Equal(stale ? 1 : 0, _rig.Revalidations);
        }

        [Fact]
        public async Task A_root_the_caller_may_not_read_is_refused_before_any_batch()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null));

            ForbiddenException refused = await Assert.ThrowsAsync<ForbiddenException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest()));

            Assert.Equal((ForbiddenReason.NoGrant, QueryModel.Accounts, AccessActions.Read), (refused.Reason, refused.Resource, refused.Action));
            Assert.Empty(_rig.Script.Batches);
        }

        [Fact]
        public async Task The_traversal_rule_is_resolvable_by_its_public_contract()
        {
            await using AsyncServiceScope scope = _rig.Scope();

            _ = Assert.IsType<QueryTraversalRule>(scope.ServiceProvider.GetRequiredService<IQueryTraversalRule>());
        }

        [Fact]
        public async Task The_public_traversal_rule_asks_for_the_step_up_itself_once_no_path_is_refused()
        {
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null), (QueryModel.Counters, AccessActions.Read, null));
            _rig.Assurance = null;
            await using AsyncServiceScope scope = _rig.Scope();
            IQueryTraversalRule rule = scope.ServiceProvider.GetRequiredService<IQueryTraversalRule>();
            QueryexEngine engine = scope.ServiceProvider.GetRequiredService<QueryexEngine>();
            IAccessEvaluator evaluator = scope.ServiceProvider.GetRequiredService<IAccessEvaluator>();
            Dictionary<string, AccessDecision> decisions = new(StringComparer.OrdinalIgnoreCase)
            {
                [QueryModel.Tickets] = await evaluator.EvaluateAsync(QueryModel.Tickets, AccessActions.Read, Token),
                [QueryModel.Counters] = await evaluator.EvaluateAsync(QueryModel.Counters, AccessActions.Read, Token),
                [CoreResources.User] = await evaluator.EvaluateAsync(CoreResources.User, AccessActions.Read, Token),
            };
            DiscoveryResult Discover(string filter)
            {
                return engine.DiscoverQuery(
                    new QueryDiscoverySpec { Filter = FilterTree.Leaf(filter) },
                    new DiscoveryOptions { Schema = _rig.Schema, Root = _rig.Schema.FindEntity(QueryModel.Tickets) });
            }

            // The projection asks nothing; a column past it asks for the step-up, with no caller code to forget it.
            Assert.Empty(rule.Check(QueryModel.Tickets, Discover("Counter.Number > 0"), entity => decisions[entity]));
            _ = Assert.Throws<StepUpRequiredException>(() => rule.Check(QueryModel.Tickets, Discover("Counter.Label != ''"), entity => decisions[entity]));

            // A refused path is returned first, as a direct read refuses before it asks for the step-up.
            TraversalRefusal refusal = Assert.Single(rule.Check(QueryModel.Tickets, Discover("Counter.Label != '' and CreatedBy.Email != ''"), entity => decisions[entity]));
            Assert.Equal(CoreResources.User, refusal.Entity);
        }

        // --- The select-item types ---

        [Fact]
        public void A_geography_select_item_is_refused_at_its_ordinal_with_its_whole_span()
        {
            QueryexSchemaBuilder builder = new();
            _ = builder.Entity("geo.Site", "[geo].[Sites]")
                .Key("Id", QueryexType.QxNumeric, "Id")
                .Property("Name", QueryexType.QxString, "Name")
                .Property("Location", QueryexType.QxGeography, "Location");
            QueryexSchema schema = builder.Build();
            EntityDescriptor root = schema.FindEntity("geo.Site")!;

            InvalidQueryException refused = Assert.Throws<InvalidQueryException>(() => QueryPlanner.RequireSelectable(Plan(schema, root, "Name,  Location", "Name != ''", sent: true), "geo.Site"));
            QueryPlanner.RequireSelectable(Plan(schema, root, "Id, Name", null, sent: true), "geo.Site");
            _ = Assert.Throws<InvalidOperationException>(() => QueryPlanner.RequireSelectable(Plan(schema, root, "Location", null, sent: false), "geo.Site"));

            QueryexDiagnostic diagnostic = Assert.Single(refused.Diagnostics);
            Assert.Equal((ValidationCodes.QueryGeographyNotSelectable, "Select[1]", new QueryexSpan(7, 8)), (diagnostic.Code, diagnostic.Location, diagnostic.Span));
            Assert.Equal(new KeyValuePair<string, string>("item", "Location"), Assert.Single(diagnostic.Arguments));
        }

        // --- The arithmetic failure ---

        [Fact]
        public async Task An_arithmetic_failure_is_the_callers_validation_error_with_its_reason()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script
                .OnRows(static _ => QueryRig.Page())
                .OnExecute(static (_, _) => throw new ArithmeticFailureException(ArithmeticFailureException.DivideByZero, 1));

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest { Select = "Price / 0" }));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal((ValidationPath.Root, ValidationCodes.QueryArithmeticFailed), (error.Path, error.Code));
            Assert.Equal(ArithmeticFailureException.DivideByZero, error.Arguments["reason"]);
        }

        [Fact]
        public async Task A_read_whose_retries_ran_out_is_the_database_unavailable_for_a_second_and_counted_so()
        {
            using MetricCollector<long> operations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Operations);
            DataAccessRetryExhaustedException exhausted = new(new TimeoutException("The deadlock retries ran out."), 3);
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script
                .OnRows(static _ => QueryRig.Page())
                .OnExecute((_, _) => throw exhausted);

            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(() => RowsAsync(QueryModel.Accounts, new QueryRequest()));

            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
            Assert.Same(exhausted, unavailable.InnerException);
            Assert.Equal("unavailable", Assert.Single(operations.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task A_connect_whose_retries_ran_out_is_the_database_unavailable_and_counted_so()
        {
            using MetricCollector<long> operations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Operations);
            DataAccessRetryExhaustedException exhausted = new(new TimeoutException("The connect's retries ran out."), 3);
            _rig.Grant((QueryModel.Tickets, AccessActions.Read, null));
            _rig.ConnectFailure = exhausted;

            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(() => RowsAsync(QueryModel.Tickets, new QueryRequest()));

            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
            Assert.Same(exhausted, unavailable.InnerException);
            Assert.Equal("unavailable", Assert.Single(operations.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.OutcomeTag]);
            Assert.Empty(_rig.Script.Batches);
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_re_check_whose_retries_ran_out_is_the_database_unavailable_whether_the_root_or_a_path_asked_for_it(bool root)
        {
            using MetricCollector<long> operations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Operations);
            DataAccessRetryExhaustedException exhausted = new(new TimeoutException("The re-check's retries ran out."), 3);
            (string, string, string?)[] grants = root ? [] : [(QueryModel.Tickets, AccessActions.Read, null)];
            _rig.Grant(grants);
            _rig.RevalidateFailure = exhausted;
            _rig.Clock.Advance(TimeSpan.FromMinutes(1));

            // The root's stale denial is re-checked by the type-level check, the path's by the refusal.
            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => RowsAsync(QueryModel.Tickets, new QueryRequest { Select = "Id, Counter.Label" }));

            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
            Assert.Same(exhausted, unavailable.InnerException);
            Assert.Equal(1, _rig.Revalidations);
            Assert.Equal("unavailable", Assert.Single(operations.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.OutcomeTag]);
        }

        // --- The operation's name and instruments ---

        [Fact]
        public async Task The_operation_names_the_scopes_unit_of_work_and_is_counted_by_outcome_without_a_source()
        {
            using MetricCollector<long> operations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Operations);
            using MetricCollector<double> durations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.OperationDuration);
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1, 2));

            await using AsyncServiceScope scope = _rig.Scope();
            RowPage page = await Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest(), null, Token);
            _ = await Assert.ThrowsAsync<LimitExceededException>(() => Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest { Take = 101 }, null, Token));
            _ = await Assert.ThrowsAsync<ForbiddenException>(() => Operation(scope).RunAsync(Surface(QueryModel.Tickets), new QueryRequest(), null, Token));
            _ = await Assert.ThrowsAsync<InvalidQueryException>(() => Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest { Filters = ["Nope"] }, null, Token));

            Assert.Equal(2, page.Rows.RowCount);
            Assert.Equal("qm.QmAccount:query", scope.ServiceProvider.GetRequiredService<DataAccessScope>().Operation);
            Assert.Equal(["ok", "limit", "forbidden", "validation"], operations.GetMeasurementSnapshot().Select(static measurement => (string)measurement.Tags[CrudTelemetryNames.OutcomeTag]!));
            Assert.All(operations.GetMeasurementSnapshot(), static measurement =>
            {
                Assert.Equal("query", measurement.Tags[CrudTelemetryNames.OperationTag]);
                Assert.False(measurement.Tags.ContainsKey(CrudTelemetryNames.SourceTag));
            });
            Assert.All(durations.GetMeasurementSnapshot(), static measurement => Assert.Equal(CrudMetrics.SmallLane, measurement.Tags[CrudTelemetryNames.LaneTag]));
        }

        [Fact]
        public async Task A_read_the_caller_abandoned_is_counted_as_cancelled_and_rethrown()
        {
            using MetricCollector<long> operations = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Operations);
            using CancellationTokenSource abandoned = new();
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script
                .OnRows(static _ => QueryRig.Page())
                .OnExecute((_, token) =>
                {
                    abandoned.Cancel();
                    throw new OperationCanceledException(abandoned.Token);
                });

            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest(), null, abandoned.Token));

            Assert.Equal("cancelled", Assert.Single(operations.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.OutcomeTag]);
        }

        [Fact]
        public async Task A_query_recomposed_for_a_moved_tag_is_counted_as_one_stale_rerun_by_its_cause()
        {
            using MetricCollector<long> reruns = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.StaleContextReruns);
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _rig.GrantOnRecompose((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(1));

            // --- One recomposed query, then a plain one in a scope of its own ---
            for (int i = 0; i < 2; i++)
            {
                await using AsyncServiceScope scope = _rig.Scope();
                RowPage page = await Operation(scope).RunAsync(Surface(QueryModel.Accounts), new QueryRequest(), null, Token);
                Assert.Equal(1, page.Rows.RowCount);
            }

            Assert.Equal([true, false, false], _rig.Script.Batches.Select(static batch => batch.IsRefused));
            Assert.Equal("permissions", Assert.Single(reruns.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.TagTag]);
        }

        [Fact]
        public async Task A_warm_query_discovers_nothing_again()
        {
            using MetricCollector<double> discoveries = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.QueryDiscoverDuration);
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = ["Price > @min"] });
            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest { Filters = ["Price > @min"], Arguments = Arguments("""{ "min": 1 }""") });
            _ = await RowsAsync(QueryModel.Accounts, new QueryRequest());

            _ = Assert.Single(discoveries.GetMeasurementSnapshot());
        }

        // --- The child surfaces and the pipeline behind a service ---

        [Fact]
        public async Task The_child_query_service_runs_a_child_surface_as_a_stack_and_refuses_an_unknown_one()
        {
            _rig.Grant((QueryModel.Accounts, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page(5));

            await using AsyncServiceScope scope = _rig.Scope();
            IChildQueryService children = scope.ServiceProvider.GetRequiredService<IChildQueryService>();
            RowPage page = await children.QueryAsync(QueryModel.Lines, new QueryRequest { Search = "ab" }, Token);

            Assert.Equal(1, page.Rows.RowCount);
            Assert.Equal("and(or(contains(Name, @search)))", QueryRig.Render(Spec(_rig.Script.Batches[^1].Of(ScriptedKind.Rows).Single()).Filter));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => children.QueryAsync(QueryModel.Accounts, new QueryRequest(), Token));
            _ = Assert.Throws<NotSupportedException>(() => children.ExcelRowSource(QueryModel.Lines, new ExportSource.All()));
            _ = Assert.Throws<InvalidOperationException>(() => children.ExcelRowSource("qm.Nope", new ExportSource.All()));
        }

        [Fact]
        public async Task A_services_query_runs_through_its_pipeline_with_its_search_asked_once()
        {
            _rig.Grant((QueryModel.Counters, AccessActions.Read, null));
            _ = _rig.Script.OnRows(static _ => QueryRig.Page());

            await using AsyncServiceScope scope = _rig.Scope();
            QmCounterService counters = scope.ServiceProvider.GetRequiredService<QmCounterService>();
            _ = await counters.QueryAsync(new QueryRequest { Search = "‎ab" }, Token);

            Assert.Equal(["ab"], counters.Searched);
            Assert.Equal(QueryModel.Counters, counters.Descriptor.Resource);
            Assert.Equal(
                "and(or(" + QmCounterService.LabelSearch + "))",
                QueryRig.Render(Spec(_rig.Script.Batches[^1].Of(ScriptedKind.Rows).Single()).Filter));
        }

        [Fact]
        public async Task An_operation_the_stack_omits_is_an_authoring_bug_and_one_not_built_yet_is_not_supported()
        {
            await using AsyncServiceScope scope = _rig.Scope();
            IEntityPipelineFactory factory = scope.ServiceProvider.GetRequiredService<IEntityPipelineFactory>();
            IEntityPipeline<QmAccount, int> accounts = factory.Create(scope.ServiceProvider.GetRequiredService<EntityService<QmAccount, int>>());
            IEntityPipeline<QmNode, int> nodes = factory.Create(scope.ServiceProvider.GetRequiredService<EntityService<QmNode, int>>());

            Assert.Equal((QueryModel.Accounts, QueryModel.Nodes), (accounts.Descriptor.Resource, nodes.Descriptor.Resource));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.GetAllCachedAsync(new DetailsRequest(), Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.GetByParentIdsAsync(new ParentIdsRequest(null, null, null, null), Token));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.DeleteWithDescendantsAsync([1], Token));
            _ = await Assert.ThrowsAsync<NotSupportedException>(() => accounts.DeleteByIdsAsync([1], null, Token));
            _ = await Assert.ThrowsAsync<NotSupportedException>(() => nodes.DeleteWithDescendantsAsync([1], Token));
        }

        /// <summary>Runs one query in a scope of its own and returns its one row read.</summary>
        /// <param name="resource">The stack's or the child surface's resource.</param>
        /// <param name="request">The request.</param>
        /// <returns>The row read the batch held.</returns>
        private async Task<ScriptedStatement> RowsAsync(string resource, QueryRequest request)
        {
            int before = _rig.Script.Batches.Count;
            await using AsyncServiceScope scope = _rig.Scope();
            _ = await Operation(scope).RunAsync(Surface(resource), request, null, Token);
            ScriptedBatch batch = Assert.Single(_rig.Script.Batches.Skip(before));
            Assert.Equal(BatchPurpose.Read, batch.Purpose);
            return Assert.Single(batch.Of(ScriptedKind.Rows));
        }

        /// <summary>A plan over a schema the model cannot map, compiled as the planner compiles.</summary>
        /// <param name="schema">The schema.</param>
        /// <param name="root">The root.</param>
        /// <param name="select">The select.</param>
        /// <param name="filter">The filter, or null.</param>
        /// <param name="sent">Whether the caller sent the select.</param>
        /// <returns>The plan.</returns>
        private PlannedQuery Plan(QueryexSchema schema, EntityDescriptor root, string select, string? filter, bool sent)
        {
            QuerySpec spec = new() { Root = root, Select = select, Filter = filter is null ? null : FilterTree.Leaf(filter) };
            QueryexResult<CompiledQuery> compiled = _rig.Services.GetRequiredService<QueryexEngine>().CompileQuery(
                spec,
                new QueryCompilationOptions { Schema = schema, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });
            Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(static diagnostic => diagnostic.Code + "@" + diagnostic.Location)));
            return new PlannedQuery(spec, new QueryArguments(), new RowQueryOptions(), DiscoveryResultOf(), compiled.Value, sent);
        }

        /// <summary>An empty discovery.</summary>
        /// <returns>The discovery.</returns>
        private static DiscoveryResult DiscoveryResultOf()
        {
            return new DiscoveryResult([], [], [], false, []);
        }

        /// <summary>A scope's query operation.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The operation.</returns>
        private static QueryOperation Operation(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<QueryOperation>();
        }

        /// <summary>A stack's or a child surface's query surface.</summary>
        /// <param name="resource">The resource.</param>
        /// <returns>The surface.</returns>
        private QuerySurface Surface(string resource)
        {
            return _rig.Surfaces.Stack(resource) ?? _rig.Surfaces.Child(resource) ?? throw new ArgumentException($"No surface is named '{resource}'.", nameof(resource));
        }

        /// <summary>A row read's spec.</summary>
        /// <param name="statement">The statement.</param>
        /// <returns>The spec.</returns>
        private static QuerySpec Spec(ScriptedStatement statement)
        {
            return Assert.IsType<QuerySpec>(statement.Statement);
        }

        /// <summary>A row read's options.</summary>
        /// <param name="statement">The statement.</param>
        /// <returns>The options.</returns>
        private static RowQueryOptions Options(ScriptedStatement statement)
        {
            return Assert.IsType<RowQueryOptions>(statement.Options);
        }

        /// <summary>Arguments from a JSON object.</summary>
        /// <param name="json">The object.</param>
        /// <returns>The arguments, by name as written.</returns>
        private static Dictionary<string, JsonElement> Arguments([StringSyntax(StringSyntaxAttribute.Json)] string json)
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.EnumerateObject().ToDictionary(static property => property.Name, static property => property.Value.Clone(), StringComparer.Ordinal);
        }
    }
}
