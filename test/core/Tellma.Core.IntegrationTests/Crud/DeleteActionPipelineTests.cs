// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.IntegrationTests.Access;
using Tellma.Core.IntegrationTests.Data;
using Tellma.Testing.Entities;

namespace Tellma.Core.IntegrationTests.Crud
{
    /// <summary>
    ///     The delete and action pipelines against a real tenant database: a delete and a built-in
    ///     action of a stack that validates nothing take one round trip, a hooked delete and a custom
    ///     action two; on every id-addressed write an absent or hidden id is not found before a row
    ///     outside the grant is forbidden, and a hooked delete or an action is refused on its first
    ///     round trip; a write without its grant is refused before any; a delete by query refuses a
    ///     blank filter, and inside its transaction more rows than its cap or another count than the
    ///     caller confirmed; a subtree with a row outside the delete grant is refused whole; a row still
    ///     referenced is in use; a delete compares the stamps it is given; the built-in actions
    ///     stamp the rows they change, skip those already in the state, and answer alike on either path;
    ///     and a custom action whose permissions move before its persist commits once, recomposed.
    /// </summary>
    /// <param name="fixture">The seeded tenant databases.</param>
    [Trait("Category", "Integration")]
    public sealed class DeleteActionPipelineTests(WritePipelineDatabase fixture) : IClassFixture<WritePipelineDatabase>
    {
        /// <summary>The widgets' resource, whose service validates deletes and actions.</summary>
        private const string Widgets = "fixture.Widget";

        /// <summary>The nodes' resource, a tree whose service validates nothing.</summary>
        private const string Nodes = "fixture.Node";

        /// <summary>The lookups' resource, activatable, whose service validates nothing.</summary>
        private const string Lookups = "fixture.Lookup";

        /// <summary>The exports' resource, whose service validates nothing.</summary>
        private const string Exports = "fixture.Export";

        /// <summary>The widgets' custom action.</summary>
        private const string Rename = "rename";

        /// <summary>The securable action the widgets' custom action requires.</summary>
        private const string RenameGrant = "Rename";

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // --- Round trips ---

        [Fact]
        public async Task A_delete_or_a_built_in_action_takes_one_round_trip_and_a_hooked_delete_or_a_custom_action_two()
        {
            fixture.Probe.Reset();
            await SeedLookupsAsync("(201, 'L-201', N'One', 1)", "(202, 'L-202', N'Two', 1)");
            await SeedWidgetsAsync(501, 502);
            (_, string subject) = await fixture.SeedCallerAsync(
                (Lookups, AccessActions.Read, null),
                (Lookups, AccessActions.Delete, null),
                (Lookups, AccessActions.Activate, null),
                (Widgets, AccessActions.Read, null),
                (Widgets, AccessActions.Delete, null),
                (Widgets, RenameGrant, null));
            await WarmAsync<Lookup>(subject);

            // --- One round trip: the delete, and the update, each with its pre-check inside it ---
            (AffectedResult deleted, int deleteTrips) = await CountedAsync(subject, scope => LookupService(scope).DeleteByIdsAsync([201], null, Token));
            (EntitiesResult<Lookup> deactivated, int deactivateTrips) = await CountedAsync(subject, scope => LookupService(scope).DeactivateAsync([202], cancellationToken: Token));

            Assert.Equal((1, 1), (deleted.Count, deleteTrips));
            Assert.Equal([202L], deactivated.Ids);
            Assert.Equal(1, deactivateTrips);
            Assert.Equal(0, await CountAsync("[fixture].[Lookups] WHERE [Id] = 201"));
            Assert.Equal(0, await CountAsync("[fixture].[Lookups] WHERE [Id] = 202 AND [IsActive] = 1"));

            // --- Two: the targets and the validators' round, then the persist ---
            int validated = 0;
            fixture.Probe.ValidateDelete = context =>
            {
                validated += context.Ids.Count;
                return Task.CompletedTask;
            };
            (AffectedResult hooked, int hookedTrips) = await CountedAsync(subject, scope => WidgetService(scope).DeleteByIdsAsync([501], null, Token));
            (EntitiesResult<Widget> renamed, int renameTrips) = await CountedAsync(
                subject,
                scope => WidgetService(scope).ExecuteActionAsync(Rename, [502], Json(/*lang=json,strict*/ """{ "name": "Renamed" }"""), new ActionOptions(), Token));

            Assert.Equal((1, 2, 1), (hooked.Count, hookedTrips, validated));
            Assert.Equal([502L], renamed.Ids);
            Assert.Equal(2, renameTrips);
            Assert.Equal(["W-502"], fixture.Probe.Renamed);
            Assert.Equal(0, await CountAsync("[fixture].[Widgets] WHERE [Id] = 501"));
            Assert.Equal(1, await CountAsync("[fixture].[Widgets] WHERE [Id] = 502 AND [Name] = N'Renamed'"));
        }

        // --- Hidden equals missing, then the grant ---

        [Fact]
        public async Task On_every_id_addressed_write_a_hidden_id_is_not_found_before_a_row_outside_the_grant_is_forbidden()
        {
            fixture.Probe.Reset();
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Nodes", "[Id], [Code], [Name], [Node], [ParentId]", "(310, 'N-hidden', N'Hidden', '/10/', NULL)", "(311, 'N-locked', N'Locked', '/11/', NULL)"));
            await SeedWidgetsAsync(511, 512);
            (_, string subject) = await fixture.SeedCallerAsync(
                (Nodes, AccessActions.Read, "Code != 'N-hidden'"),
                (Nodes, AccessActions.Delete, "Code != 'N-hidden' and Code != 'N-locked'"),
                (Nodes, AccessActions.Activate, "Code != 'N-hidden' and Code != 'N-locked'"),
                (Widgets, AccessActions.Read, "Code != 'W-511'"),
                (Widgets, AccessActions.Delete, "Code != 'W-511' and Code != 'W-512'"),
                (Widgets, AccessActions.Activate, "Code != 'W-511' and Code != 'W-512'"),
                (Widgets, RenameGrant, "Code != 'W-511' and Code != 'W-512'"));
            await WarmAsync<Node>(subject);

            // --- Each write: the hidden id, the locked id, the securable action, and the round trips a
            // refusal takes; the hooked delete and the widget's actions run their validators, yet the
            // pre-check refuses on the first round trip all the same ---
            (string Name, Func<AsyncServiceScope, int[], Task> Write, int Hidden, int Locked, string Action, int Trips)[] writes =
            [
                ("delete", static (scope, ids) => NodeService(scope).DeleteByIdsAsync(ids, null, Token), 310, 311, AccessActions.Delete, 1),
                ("delete with descendants", static (scope, ids) => NodeService(scope).DeleteWithDescendantsAsync(ids, Token), 310, 311, AccessActions.Delete, 1),
                ("activate", static (scope, ids) => NodeService(scope).ActivateAsync(ids, cancellationToken: Token), 310, 311, AccessActions.Activate, 1),
                ("deactivate", static (scope, ids) => NodeService(scope).DeactivateAsync(ids, cancellationToken: Token), 310, 311, AccessActions.Activate, 1),
                ("hooked delete", static (scope, ids) => WidgetService(scope).DeleteByIdsAsync(ids, null, Token), 511, 512, AccessActions.Delete, 1),
                ("hooked deactivate", static (scope, ids) => WidgetService(scope).DeactivateAsync(ids, cancellationToken: Token), 511, 512, AccessActions.Activate, 1),
                ("custom action", static (scope, ids) => WidgetService(scope).ExecuteActionAsync(Rename, ids, new WidgetRename { Name = "x" }, new ActionOptions(), Token), 511, 512, RenameGrant, 1),
            ];

            foreach ((string name, Func<AsyncServiceScope, int[], Task> write, int hidden, int locked, string action, int trips) in writes)
            {
                // --- Hidden alone, and hidden beside a row outside the grant: not found, naming the hidden id alone ---
                (NotFoundException missing, int missingTrips) = await RefusedAsync<NotFoundException>(subject, scope => write(scope, [hidden]));
                (NotFoundException first, _) = await RefusedAsync<NotFoundException>(subject, scope => write(scope, [locked, hidden]));
                Assert.True(Text(hidden) == Assert.Single(missing.Ids), name);
                Assert.True(Text(hidden) == Assert.Single(first.Ids), name);
                Assert.True(trips == missingTrips, name);

                // --- Visible but outside the grant: forbidden ---
                (ForbiddenException outside, int outsideTrips) = await RefusedAsync<ForbiddenException>(subject, scope => write(scope, [locked]));
                Assert.True((action, ForbiddenReason.OutsideFilter, trips) == (outside.Action, outside.Reason, outsideTrips), name);
            }

            // --- Nothing was written ---
            Assert.Empty(fixture.Probe.Renamed);
            Assert.Equal(2, await CountAsync("[fixture].[Nodes] WHERE [Id] IN (310, 311) AND [IsActive] = 1"));
            Assert.Equal(2, await CountAsync("[fixture].[Widgets] WHERE [Id] IN (511, 512) AND [IsActive] = 1"));
        }

        [Fact]
        public async Task On_a_cacheable_stack_a_filtered_write_grant_reads_no_row_so_every_id_is_not_found()
        {
            // A cacheable stack's Read takes no filter, so a filtered write grant implies no read at all.
            await SeedLookupsAsync("(251, 'L-251', N'Granted', 1)");
            (_, string subject) = await fixture.SeedCallerAsync((Lookups, AccessActions.Delete, "Code = 'L-251'"), (Lookups, AccessActions.Activate, "Code = 'L-251'"));

            (NotFoundException deleted, _) = await RefusedAsync<NotFoundException>(subject, scope => LookupService(scope).DeleteByIdsAsync([251], null, Token));
            (NotFoundException deactivated, _) = await RefusedAsync<NotFoundException>(subject, scope => LookupService(scope).DeactivateAsync([251], cancellationToken: Token));

            Assert.Equal(["251"], deleted.Ids);
            Assert.Equal(["251"], deactivated.Ids);
            Assert.Equal(1, await CountAsync("[fixture].[Lookups] WHERE [Id] = 251 AND [IsActive] = 1"));
        }

        [Fact]
        public async Task A_write_without_its_grant_is_refused_before_any_round_trip()
        {
            fixture.Probe.Reset();
            (_, string subject) = await fixture.SeedCallerAsync((Lookups, AccessActions.Read, null), (Widgets, AccessActions.Read, null));
            await WarmAsync<Lookup>(subject);

            (ForbiddenException delete, int deleteTrips) = await RefusedAsync<ForbiddenException>(subject, scope => LookupService(scope).DeleteByIdsAsync([1], null, Token));
            (ForbiddenException activate, int activateTrips) = await RefusedAsync<ForbiddenException>(subject, scope => LookupService(scope).ActivateAsync([1], cancellationToken: Token));
            (ForbiddenException rename, int renameTrips) = await RefusedAsync<ForbiddenException>(
                subject, scope => WidgetService(scope).ExecuteActionAsync(Rename, [1], new WidgetRename { Name = "x" }, new ActionOptions(), Token));

            Assert.Equal((AccessActions.Delete, ForbiddenReason.NoGrant, 0), (delete.Action, delete.Reason, deleteTrips));
            Assert.Equal((AccessActions.Activate, ForbiddenReason.NoGrant, 0), (activate.Action, activate.Reason, activateTrips));
            Assert.Equal((RenameGrant, ForbiddenReason.NoGrant, 0), (rename.Action, rename.Reason, renameTrips));
        }

        // --- A delete by query ---

        [Fact]
        public async Task A_delete_by_query_refuses_a_blank_filter_and_inside_its_transaction_a_match_past_its_cap_or_another_count_than_confirmed()
        {
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Exports", "[Id], [Name]", "(900001, N'by-query')", "(900002, N'by-query')"));
            (_, string subject) = await fixture.SeedCallerAsync((Exports, AccessActions.Read, null), (Exports, AccessActions.Delete, null));
            await WarmAsync<Export>(subject);

            // --- A blank filter would delete every row: the caller's mistake ---
            _ = await RefusedAsync<BadRequestException>(subject, scope => ExportService(scope).DeleteByQueryAsync(new DeleteByQueryRequest(" ", null, 1), Token));

            // --- More rows than the cap: refused inside the transaction, nothing deleted ---
            (LimitExceededException capped, _) = await RefusedAsync<LimitExceededException>(
                subject, scope => ExportService(scope).DeleteByQueryAsync(new DeleteByQueryRequest($"Name = '{WritePipelineDatabase.BulkName}'", null, WritePipelineDatabase.BulkExports), Token));
            Assert.Equal((LimitNames.MaxDeleteByQueryRows, WritePipelineDatabase.BulkExports, WritePipelineDatabase.BulkExports - 1), (capped.Limit, capped.Actual, capped.Maximum));
            Assert.Equal(WritePipelineDatabase.BulkExports, await CountAsync($"[fixture].[Exports] WHERE [Name] = N'{WritePipelineDatabase.BulkName}'"));

            // --- Another count than the caller confirmed: refused with the count matched, nothing deleted ---
            (CountMismatchException mismatch, _) = await RefusedAsync<CountMismatchException>(
                subject, scope => ExportService(scope).DeleteByQueryAsync(new DeleteByQueryRequest("Name = @name", Arguments(("name", "by-query")), 3), Token));
            Assert.Equal((3, 2), (mismatch.Expected, mismatch.Actual));
            Assert.Equal(2, await CountAsync("[fixture].[Exports] WHERE [Name] = N'by-query'"));

            // --- The confirmed count: deleted on one round trip ---
            (AffectedResult deleted, int trips) = await CountedAsync(
                subject, scope => ExportService(scope).DeleteByQueryAsync(new DeleteByQueryRequest("Name = @name", Arguments(("name", "by-query")), 2), Token));
            Assert.Equal((2, 1), (deleted.Count, trips));
            Assert.Equal(0, await CountAsync("[fixture].[Exports] WHERE [Name] = N'by-query'"));
        }

        // --- A delete with descendants ---

        [Fact]
        public async Task A_subtree_holding_a_row_outside_the_delete_grant_is_refused_whole_and_one_wholly_granted_is_deleted_with_every_descendant()
        {
            await fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped(
                    "fixture.Nodes",
                    "[Id], [Code], [Name], [Node], [ParentId]",
                    "(320, 'N-tree', N'Tree', '/20/', NULL)",
                    "(321, 'N-tree-a', N'A', '/20/1/', 320)",
                    "(322, 'N-tree-b', N'B', '/20/1/1/', 321)",
                    "(323, 'N-tree-c', N'C', '/20/2/', 320)"));
            (_, string partial) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Delete, "Code != 'N-tree-b'"));
            (_, string whole) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Delete, null));
            await WarmAsync<Node>(partial);
            await WarmAsync<Node>(whole);

            // --- A descendant outside the grant: never a partly deleted subtree ---
            (ForbiddenException refused, _) = await RefusedAsync<ForbiddenException>(partial, scope => NodeService(scope).DeleteWithDescendantsAsync([320], Token));
            Assert.Equal((AccessActions.Delete, ForbiddenReason.OutsideFilter), (refused.Action, refused.Reason));
            Assert.Equal(4, await CountAsync("[fixture].[Nodes] WHERE [Id] BETWEEN 320 AND 323"));

            // --- Wholly granted: the root and every descendant, on one round trip ---
            (AffectedResult deleted, int trips) = await CountedAsync(whole, scope => NodeService(scope).DeleteWithDescendantsAsync([320], Token));
            Assert.Equal((4, 1), (deleted.Count, trips));
            Assert.Equal(0, await CountAsync("[fixture].[Nodes] WHERE [Id] BETWEEN 320 AND 323"));
        }

        // --- A row still referenced ---

        [Fact]
        public async Task A_row_still_referenced_is_in_use_naming_the_referencing_entity_and_property()
        {
            (_, string subject) = await fixture.SeedCallerAsync((Nodes, AccessActions.Read, null), (Nodes, AccessActions.Delete, null));

            // --- N12 is the node link 1 points at ---
            (ValidationException refused, _) = await RefusedAsync<ValidationException>(subject, scope => NodeService(scope).DeleteByIdsAsync([5], null, Token));

            ValidationError error = Assert.Single(refused.Errors);
            Assert.Equal(("Ids[0]", ValidationCodes.FkInUse), (error.Path.ToString(), error.Code));
            Assert.Equal(("fixture.Link", nameof(Link.OtherNodeId)), (error.Arguments["entity"], error.Arguments["property"]));
            Assert.Equal(1, await CountAsync("[fixture].[Nodes] WHERE [Id] = 5"));
            Assert.Equal(1, await CountAsync("[fixture].[Links] WHERE [Id] = 3"));
        }

        // --- Stamps ---

        [Fact]
        public async Task A_delete_comparing_stamps_deletes_the_rows_read_with_them_and_a_row_changed_since_is_a_conflict()
        {
            await SeedLookupsAsync("(241, 'L-241', N'Kept', 1)", "(242, 'L-242', N'Changed', 1)");
            (_, string subject) = await fixture.SeedCallerAsync((Lookups, AccessActions.Read, null), (Lookups, AccessActions.Delete, null));
            Dictionary<int, DateTimeOffset> stamps;
            await using (AsyncServiceScope scope = await fixture.ScopeAsync(subject))
            {
                stamps = (await LookupService(scope).GetByIdsAsync([241, 242], new DetailsRequest(), Token)).Entities.ToDictionary(static lookup => lookup.Id, static lookup => lookup.ModifiedAt);
            }

            // --- Another connection changes 242 after it was read ---
            await fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, "UPDATE [fixture].[Lookups] SET [Name] = N'Theirs', [ModifiedAt] = DATEADD(SECOND, 1, [ModifiedAt]) WHERE [Id] = 242;");

            (AffectedResult deleted, _) = await CountedAsync(subject, scope => LookupService(scope).DeleteByIdsAsync([241], [stamps[241]], Token));
            (ConcurrencyException conflict, _) = await RefusedAsync<ConcurrencyException>(subject, scope => LookupService(scope).DeleteByIdsAsync([242], [stamps[242]], Token));
            (ValidationException unstamped, _) = await RefusedAsync<ValidationException>(subject, scope => LookupService(scope).DeleteByIdsAsync([242], [null], Token));

            Assert.Equal(1, deleted.Count);
            Assert.Equal(242, Assert.Single(conflict.Conflicts).Id);
            Assert.Equal($"Ids[0] {ValidationCodes.ConcurrencyStampRequired}", Assert.Single(unstamped.Errors) is { } error ? $"{error.Path} {error.Code}" : null);
            Assert.Equal(0, await CountAsync("[fixture].[Lookups] WHERE [Id] = 241"));
            Assert.Equal(1, await CountAsync("[fixture].[Lookups] WHERE [Id] = 242"));
        }

        [Fact]
        public async Task A_built_in_action_stamps_the_rows_it_changes_and_leaves_rows_already_in_the_state_unstamped()
        {
            await SeedLookupsAsync("(221, 'L-221', N'Active', 1)", "(222, 'L-222', N'Inactive', 0)");
            (int userId, string subject) = await fixture.SeedCallerAsync((Lookups, AccessActions.Read, null), (Lookups, AccessActions.Activate, null));
            string? before = await StampAsync(221);
            string? changedBefore = await StampAsync(222);

            (EntitiesResult<Lookup> activated, int trips) = await CountedAsync(subject, scope => LookupService(scope).ActivateAsync([221, 222], cancellationToken: Token));

            Assert.Equal([221L, 222L], activated.Ids);
            Assert.Equal(1, trips);
            Assert.Equal(before, await StampAsync(221));
            Assert.NotEqual(changedBefore, await StampAsync(222));
            Assert.Equal(1, await CountAsync(Invariant($"[fixture].[Lookups] WHERE [Id] = 222 AND [IsActive] = 1 AND [ModifiedById] = {userId}")));
        }

        [Fact]
        public async Task Deactivate_answers_alike_on_the_one_round_trip_path_and_the_general_one()
        {
            fixture.Probe.Reset();
            await SeedLookupsAsync("(231, 'L-231', N'One', 1)", "(232, 'L-232', N'Two', 1)");
            await SeedWidgetsAsync(531, 532);
            (_, string subject) = await fixture.SeedCallerAsync(
                (Lookups, AccessActions.Read, null), (Lookups, AccessActions.Activate, null), (Widgets, AccessActions.Read, null), (Widgets, AccessActions.Activate, null));
            await WarmAsync<Lookup>(subject);
            ActionOptions options = new(ReturnEntities: true);

            (EntitiesResult<Lookup> lookups, int fastTrips) = await CountedAsync(subject, scope => LookupService(scope).DeactivateAsync([232, 231], options, Token));
            (EntitiesResult<Widget> widgets, int generalTrips) = await CountedAsync(subject, scope => WidgetService(scope).DeactivateAsync([532, 531], options, Token));

            // --- The same answer: the ids in request order, and the rows read back in the new state ---
            Assert.Equal([232L, 231L], lookups.Ids);
            Assert.Equal(1, fastTrips);
            Assert.Equal([532L, 531L], widgets.Ids);
            Assert.Equal(2, generalTrips);
            Assert.Equal(lookups.Ids, lookups.Entities.Select(static lookup => (long)lookup.Id));
            Assert.Equal(widgets.Ids, widgets.Entities.Select(static widget => (long)widget.Id));
            Assert.All(lookups.Entities, static lookup => Assert.False(lookup.IsActive));
            Assert.All(widgets.Entities, static widget => Assert.False(widget.IsActive));
        }

        // --- A re-entry: the persist recomposed for a moved permission ---

        [Fact]
        public async Task A_custom_action_whose_permissions_move_after_its_validation_round_re_runs_its_hook_in_memory_replays_its_method_and_commits_once()
        {
            fixture.Probe.Reset();
            await SeedWidgetsAsync(541);
            (int userId, string subject) = await fixture.SeedCallerAsync((Widgets, AccessActions.Read, null), (Widgets, RenameGrant, null));
            await WarmAsync<Widget>(subject);
            int validated = 0;
            fixture.Probe.ValidateAction = async (_, context) =>
            {
                // The first run moves the caller's permissions once its round answered, before the persist is sent.
                await context.Loader.LoadAsync();
                if (++validated == 1)
                {
                    await fixture.Connect.BumpPermissionsAsync(userId);
                }
            };

            (EntitiesResult<Widget> renamed, int trips) = await CountedAsync(
                subject,
                scope => WidgetService(scope).ExecuteActionAsync(Rename, [541], new WidgetRename { Name = "Recomposed" }, new ActionOptions(), Token));

            // --- The validation round, the persist its guard refused and the recomposed one: the hook
            // ran again in memory, the method once, and its update committed once ---
            Assert.Equal([541L], renamed.Ids);
            Assert.Equal((3, 2), (trips, validated));
            Assert.Equal(["W-541"], fixture.Probe.Renamed);
            Assert.Equal(1, await CountAsync("[fixture].[Widgets] WHERE [Id] = 541 AND [Name] = N'Recomposed'"));
        }

        // --- Helpers ---

        /// <summary>Runs a call in a scope of the caller and counts the round trips it took.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="call">The call.</param>
        /// <returns>What it answered, and its round trips.</returns>
        private async Task<(T Result, int Trips)> CountedAsync<T>(string subject, Func<AsyncServiceScope, Task<T>> call)
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            T result = await call(scope);
            return (result, access.RoundTrips - before);
        }

        /// <summary>Runs a call that must be refused in a scope of the caller, and counts the round trips it took.</summary>
        /// <typeparam name="TException">The refusal.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <param name="call">The call.</param>
        /// <returns>The refusal, and the round trips the call took.</returns>
        private async Task<(TException Refusal, int Trips)> RefusedAsync<TException>(string subject, Func<AsyncServiceScope, Task> call)
            where TException : Exception
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            DataAccessScope access = scope.ServiceProvider.GetRequiredService<DataAccessScope>();
            int before = access.RoundTrips;
            TException refusal = await Assert.ThrowsAsync<TException>(() => call(scope));
            return (refusal, access.RoundTrips - before);
        }

        /// <summary>Reads rows of a stack the caller may read once, which warms the caller's connection and permissions.</summary>
        /// <typeparam name="TEntity">The stack's entity.</typeparam>
        /// <param name="subject">The caller's subject.</param>
        /// <returns>A task that completes when the read ran.</returns>
        private async Task WarmAsync<TEntity>(string subject)
            where TEntity : Entity<int>
        {
            await using AsyncServiceScope scope = await fixture.ScopeAsync(subject);
            _ = await scope.ServiceProvider.GetRequiredService<EntityService<TEntity, int>>().QueryAsync(new QueryRequest { Select = "Id" }, Token);
        }

        /// <summary>Seeds lookups, each row its id, code, name and active flag.</summary>
        /// <param name="rows">The rows' values.</param>
        /// <returns>A task that completes when they are seeded.</returns>
        private Task SeedLookupsAsync(params string[] rows)
        {
            return fixture.Connect.ExecuteAsync(ConnectDatabase.TenantA, DataQueryDatabase.Stamped("fixture.Lookups", "[Id], [Code], [Name], [IsActive]", rows));
        }

        /// <summary>Seeds active widgets, each coded and named after its id.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>A task that completes when they are seeded.</returns>
        private Task SeedWidgetsAsync(params int[] ids)
        {
            return fixture.Connect.ExecuteAsync(
                ConnectDatabase.TenantA,
                DataQueryDatabase.Stamped("fixture.Widgets", "[Id], [Code], [Kind], [Name], [Subject]", [.. ids.Select(static id => Invariant($"({id}, 'W-{id}', 'Plain', N'W-{id}', N's-{id}')"))]));
        }

        /// <summary>How many rows of a table match a condition.</summary>
        /// <param name="where">The table and its condition: <c>[schema].[Table] WHERE …</c>.</param>
        /// <returns>The count.</returns>
        private Task<int> CountAsync(string where)
        {
            return fixture.Connect.ScalarAsync<int>(ConnectDatabase.TenantA, "SELECT COUNT(*) FROM " + where + ";");
        }

        /// <summary>A lookup's stored stamp, as text with every fractional digit.</summary>
        /// <param name="id">The lookup's id.</param>
        /// <returns>The stamp.</returns>
        private Task<string?> StampAsync(int id)
        {
            return fixture.Connect.ScalarAsync<string>(ConnectDatabase.TenantA, Invariant($"SELECT CONVERT(nvarchar(40), [ModifiedAt], 127) FROM [fixture].[Lookups] WHERE [Id] = {id};"));
        }

        /// <summary>A delete by query's arguments, each a name and a string value.</summary>
        /// <param name="arguments">The arguments.</param>
        /// <returns>The arguments as JSON values by name.</returns>
        private static Dictionary<string, JsonElement> Arguments(params (string Name, string Value)[] arguments)
        {
            return arguments.ToDictionary(static argument => argument.Name, static argument => JsonSerializer.SerializeToElement(argument.Value), StringComparer.Ordinal);
        }

        /// <summary>Parses JSON text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The element.</returns>
        private static JsonElement Json(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        /// <summary>An id as text, as a refusal names it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The text.</returns>
        private static string Text(int id)
        {
            return id.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Formats text in the invariant culture.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The formatted text.</returns>
        private static string Invariant(FormattableString text)
        {
            return FormattableString.Invariant(text);
        }

        /// <summary>A scope's lookup service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Lookup, int> LookupService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Lookup, int>>();
        }

        /// <summary>A scope's node service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Node, int> NodeService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Node, int>>();
        }

        /// <summary>A scope's export service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static EntityService<Export, int> ExportService(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<EntityService<Export, int>>();
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
