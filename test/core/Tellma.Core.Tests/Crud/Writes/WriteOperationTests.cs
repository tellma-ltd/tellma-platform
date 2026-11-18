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
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Writes
{
    /// <summary>
    ///     The delete and action pipelines over the scripted batch: the ceilings and the type-level
    ///     grant before any round trip; a delete or a built-in action of a stack that validates nothing
    ///     persisting on its one round trip under the caller's Read filter and the grant's filter; a
    ///     hooked delete and an action loading their targets on round trip 1, an absent or hidden id
    ///     not found before a row outside the grant is forbidden, and persisting on a second; a delete
    ///     by query counting the rows it matches beside its capped delete; the translation of the
    ///     executor's failures; the action overloads, the single target and the arguments; and a
    ///     method parked on its persist.
    /// </summary>
    public sealed partial class WriteOperationTests : IAsyncDisposable
    {
        /// <summary>The stamp every stored ticket carries.</summary>
        private static readonly DateTimeOffset Stamp = new(2026, 11, 16, 8, 30, 0, 123, TimeSpan.Zero);

        /// <summary>The stamp a persist's save statement reports.</summary>
        private static readonly DateTimeOffset Committed = new(2026, 11, 17, 9, 0, 0, 456, TimeSpan.Zero);

        /// <summary>The tickets' hooks and methods, shared with the rig's scopes.</summary>
        private readonly PwHooks _hooks = new();

        /// <summary>The composed model.</summary>
        private readonly QueryRig _rig;

        /// <summary>The ticket ids the target load finds.</summary>
        private readonly HashSet<int> _stored = [1, 2, 3, 4, 5];

        /// <summary>The ids a delete by query's statement deletes.</summary>
        private readonly HashSet<object> _matched = [7, 8];

        /// <summary>The count the operation grant's filter admits on round trip 1; none scripted when null.</summary>
        private int? _granted;

        /// <summary>Composes the model, grants every operation, and scripts every statement the operations append.</summary>
        public WriteOperationTests()
        {
            _rig = new QueryRig(PipelineWriteModel.Compose, services =>
            {
                _ = services.AddSingleton(_hooks);
                _ = services.AddFakeLogging();
            });
            Grant();
            _ = _rig.Script
                .OnQuery(statement => (IReadOnlyList<PwTicket>)[.. statement.KeySet<IdList>().Where(row => _stored.Contains(row.Id)).Select(static row => Ticket(row.Id))])
                .OnCount(statement => statement.Batch.Purpose == BatchPurpose.Persist ? _matched.Count : _granted ?? throw new InvalidOperationException("The test scripts no grant count."))
                .OnDelete<PwTicket>(Deleted<PwTicket>)
                .OnDelete<PwLabel>(Deleted<PwLabel>)
                .OnDelete<PwNode>(Deleted<PwNode>)
                .OnUpdate<PwTicket>(Updated<PwTicket>)
                .OnUpdate<PwLabel>(Updated<PwLabel>)
                .OnSave<PwTicket>(static _ => new SaveReceipt(Committed, 1, 0, 0))
                .OnSql(static _ => RawResult.Empty);
        }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- Deletes on one round trip ---

        [Fact]
        public async Task A_delete_of_a_stack_that_validates_nothing_persists_on_one_round_trip_under_the_read_filter_and_the_grants_filter()
        {
            Grant(read: "Name != 'hidden'", write: "Name != 'locked'");

            AffectedResult deleted = await LabelsAsync(labels => labels.DeleteByIdsAsync([2, 1, 2], null, Token));

            // --- One persist: each id once, in request order, under both filters ---
            Assert.Equal(2, deleted.Count);
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            Assert.Equal((BatchPurpose.Persist, 1), (persist.Purpose, _rig.Script.RoundTrips));
            DeleteSpec<PwLabel>.ByIds spec = Assert.IsType<DeleteSpec<PwLabel>.ByIds>(Assert.Single(persist.Of(ScriptedKind.Delete)).Statement);
            Assert.Equal([2, 1], spec.Ids.Cast<int>());
            Assert.Contains("hidden", QueryRig.Render(spec.ReadFilter), StringComparison.Ordinal);
            Assert.Contains("locked", QueryRig.Render(spec.Filter), StringComparison.Ordinal);
            Assert.Null(spec.ExpectedStamps);

            // --- No id: nothing to delete, and no round trip ---
            Assert.Equal(0, (await LabelsAsync(labels => labels.DeleteByIdsAsync([], null, Token))).Count);
            Assert.Equal(1, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_delete_with_descendants_persists_on_one_round_trip_and_answers_every_row_deleted()
        {
            Grant(read: "Id != 7", write: "Id != 8");
            _matched.Clear();
            _matched.UnionWith([4, 9, 10]);

            AffectedResult deleted = await NodesAsync(nodes => nodes.DeleteWithDescendantsAsync([4], Token));

            Assert.Equal(3, deleted.Count);
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            DeleteSpec<PwNode>.WithDescendants spec = Assert.IsType<DeleteSpec<PwNode>.WithDescendants>(Assert.Single(persist.Of(ScriptedKind.Delete)).Statement);
            Assert.Equal([4], spec.Ids.Cast<int>());
            Assert.Contains("Id != 7", QueryRig.Render(spec.ReadFilter), StringComparison.Ordinal);
            Assert.Equal("or(Id != 8)", QueryRig.Render(spec.Filter));
        }

        [Fact]
        public async Task Stamps_ride_the_delete_beside_their_ids_a_missing_one_is_refused_at_its_id_and_a_count_other_than_the_ids_is_a_bad_request()
        {
            // --- A stamp per id, the first one an id is named with kept ---
            _ = await LabelsAsync(labels => labels.DeleteByIdsAsync([1, 2, 1], [Stamp, Stamp.AddSeconds(1), Stamp.AddSeconds(2)], Token));
            DeleteSpec<PwLabel>.ByIds spec = Assert.IsType<DeleteSpec<PwLabel>.ByIds>(Assert.Single(_rig.Script.Batches[0].Of(ScriptedKind.Delete)).Statement);
            Assert.Equal([1, 2], spec.Ids.Cast<int>());
            Assert.Equal([Stamp, Stamp.AddSeconds(1)], spec.ExpectedStamps!);

            // --- A null stamp is refused at its id; a count other than the ids' is the caller's mistake ---
            ValidationException missing = await Assert.ThrowsAsync<ValidationException>(() => LabelsAsync(labels => labels.DeleteByIdsAsync([1, 2], [Stamp, null], Token)));
            Assert.Equal([$"Ids[1] {ValidationCodes.ConcurrencyStampRequired}"], Lines(missing));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => LabelsAsync(labels => labels.DeleteByIdsAsync([1, 2], [Stamp], Token)));
            Assert.Equal(1, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_delete_comparing_stamps_on_a_long_keyed_stack_is_a_bad_request_before_any_round_trip()
        {
            await using AsyncServiceScope scope = _rig.Scope();
            EntityService<PwBig, long> bigs = scope.ServiceProvider.GetRequiredService<EntityService<PwBig, long>>();

            BadRequestException refused = await Assert.ThrowsAsync<BadRequestException>(() => bigs.DeleteByIdsAsync([1L], [DateTimeOffset.UnixEpoch], Token));

            Assert.Equal(PipelineWriteModel.Bigs, refused.Arguments!["resource"]);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_delete_beyond_the_id_ceiling_or_without_the_grant_is_refused_before_any_round_trip()
        {
            LimitExceededException ceiling = await Assert.ThrowsAsync<LimitExceededException>(() => TicketsAsync(tickets => tickets.DeleteByIdsAsync([1, 2, 3, 4, 5, 6], null, Token)));
            Assert.Equal((LimitNames.MaxIds, 6, 5), (ceiling.Limit, ceiling.Actual, ceiling.Maximum));

            _rig.Grant((PipelineWriteModel.Labels, AccessActions.Read, null));
            ForbiddenException denied = await Assert.ThrowsAsync<ForbiddenException>(() => LabelsAsync(labels => labels.DeleteByIdsAsync([1], null, Token)));
            Assert.Equal((PipelineWriteModel.Labels, AccessActions.Delete, ForbiddenReason.NoGrant), (denied.Resource, denied.Action, denied.Reason));
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        // --- A hooked delete ---

        [Fact]
        public async Task A_hooked_delete_loads_its_targets_on_round_trip_1_beside_the_validators_loads_and_persists_on_a_second()
        {
            Grant(write: "Name != 'locked'");
            _granted = 2;
            (int[] Ids, int[] Loaded)? seen = null;
            _hooks.ValidateDelete = async context =>
            {
                await context.Loader.LoadAsync();
                seen = ([.. context.Ids], [.. context.Entities.Select(static ticket => ticket.Id)]);
            };

            AffectedResult deleted = await TicketsAsync(tickets => tickets.DeleteByIdsAsync([2, 1], null, Token));

            // --- Round trip 1: the targets under the read filter and their count under the grant's ---
            Assert.Equal(2, deleted.Count);
            Assert.Equal([2, 1], seen!.Value.Ids);
            Assert.Equal([2, 1], seen.Value.Loaded);
            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
            Assert.Equal(2, _rig.Script.RoundTrips);
            ScriptedBatch round = _rig.Script.Batches[0];
            Assert.Single(round.Of(ScriptedKind.Query));
            ScriptedStatement count = Assert.Single(round.Of(ScriptedKind.Count));
            Assert.Contains("locked", QueryRig.Render(count.Spec.Filter), StringComparison.Ordinal);

            // --- Round trip 2: the delete checks the same ids again inside the transaction ---
            Assert.IsType<DeleteSpec<PwTicket>.ByIds>(Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Delete)).Statement);
            DeletePersistOutcome<PwTicket, int> outcome = Assert.IsType<DeletePersistOutcome<PwTicket, int>>(Assert.Single(_hooks.Outcomes));
            Assert.Equal([2, 1], outcome.DeletedIds);
        }

        [Fact]
        public async Task A_hooked_deletes_absent_or_hidden_id_is_not_found_before_a_row_outside_the_grant_is_forbidden_and_nothing_is_persisted()
        {
            Grant(write: "Name != 'locked'");
            _stored.Remove(9);
            _granted = 0;

            // --- An absent id, and a row outside the grant beside it: not found wins ---
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => TicketsAsync(tickets => tickets.DeleteByIdsAsync([1, 9], null, Token)));
            Assert.Equal(PipelineWriteModel.Tickets, missing.Resource);
            Assert.Equal(["9"], missing.Ids);

            // --- Every id visible, one outside the grant ---
            ForbiddenException outside = await Assert.ThrowsAsync<ForbiddenException>(() => TicketsAsync(tickets => tickets.DeleteByIdsAsync([1], null, Token)));
            Assert.Equal((PipelineWriteModel.Tickets, AccessActions.Delete, ForbiddenReason.OutsideFilter), (outside.Resource, outside.Action, outside.Reason));

            // --- A validator's error refuses the delete as well ---
            Grant();
            _hooks.ValidateDelete = static context =>
            {
                context.Errors.Add(ValidationPath.Root.Property("Ids").Index(0), "Pw.Referenced");
                return Task.CompletedTask;
            };
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => TicketsAsync(tickets => tickets.DeleteByIdsAsync([1], null, Token)));
            Assert.Equal(["Ids[0] Pw.Referenced"], Lines(refused));

            Assert.All(_rig.Script.Batches, static batch => Assert.Equal(BatchPurpose.Validate, batch.Purpose));
        }

        // --- A delete by query ---

        [Fact]
        public async Task A_delete_by_query_counts_the_rows_it_matches_beside_its_capped_delete_on_one_round_trip_and_runs_no_validator()
        {
            Grant(write: "Name != 'locked'");
            bool validated = false;
            _hooks.ValidateDelete = _ =>
            {
                validated = true;
                return Task.CompletedTask;
            };

            AffectedResult deleted = await TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Name = 'x'", null, 2), Token));

            // --- The count, one past the cap, then the delete: both under the caller's filter and the grant's ---
            Assert.Equal(2, deleted.Count);
            Assert.False(validated);
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            Assert.Equal([ScriptedKind.Count, ScriptedKind.Delete], persist.Statements.Where(static statement => statement.Kind is ScriptedKind.Count or ScriptedKind.Delete).Select(static statement => statement.Kind));
            ScriptedStatement count = Assert.Single(persist.Of(ScriptedKind.Count));
            DeleteSpec<PwTicket>.ByQuery spec = Assert.IsType<DeleteSpec<PwTicket>.ByQuery>(Assert.Single(persist.Of(ScriptedKind.Delete)).Statement);
            Assert.Equal((10, 10, 2), (count.Cap, spec.Cap, spec.ExpectedCount));
            Assert.Equal("and(Name = 'x' | or(Name != 'locked'))", QueryRig.Render(spec.Filter));
            Assert.Equal(QueryRig.Render(spec.Filter), QueryRig.Render(count.Spec.Filter));
        }

        [Fact]
        public async Task A_delete_by_query_refuses_a_blank_filter_a_negative_count_and_a_filter_that_does_not_compile_before_any_round_trip()
        {
            _ = await Assert.ThrowsAsync<BadRequestException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest(" ", null, 1), Token)));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Name = 'x'", null, -1), Token)));
            InvalidQueryException invalid = await Assert.ThrowsAsync<InvalidQueryException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Nope = 1", null, 1), Token)));

            // --- The diagnostic sits in the request's filter, whether the grant's filter joins it or not ---
            Grant(write: "Name != 'locked'");
            InvalidQueryException filtered = await Assert.ThrowsAsync<InvalidQueryException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Nope = 1", null, 1), Token)));
            Assert.Equal(["filter"], invalid.Diagnostics.Select(static diagnostic => diagnostic.Location));
            Assert.Equal(["filter"], filtered.Diagnostics.Select(static diagnostic => diagnostic.Location));
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_delete_by_querys_cap_and_count_failing_inside_the_transaction_are_translated_with_the_rows_it_matched()
        {
            // --- Past the cap: the count read beside the delete, else one more than the cap ---
            FailPersist(new BatchAssertionFailedException(TellmaSqlErrors.LimitExceeded, TellmaSqlErrors.DeleteCapExceededCode, statementOrdinal: 2));
            LimitExceededException capped = await Assert.ThrowsAsync<LimitExceededException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Name = 'x'", null, 2), Token)));
            Assert.Equal((LimitNames.MaxDeleteByQueryRows, 11, 10), (capped.Limit, capped.Actual, capped.Maximum));

            // --- Another count than confirmed: the rows matched, as counted ---
            BatchAssertionFailedException mismatch = new(TellmaSqlErrors.CountMismatch, TellmaSqlErrors.DeleteCountMismatchCode, statementOrdinal: 2);
            BatchResult<int> counted = new();
            counted.Complete(4);
            CountMismatchException translated = Assert.IsType<CountMismatchException>(PersistFailures.Translate(mismatch, ByQuery(counted), _rig.Services.GetRequiredService<CrudMetrics>()));
            Assert.Equal((2, 4), (translated.Expected, translated.Actual));
            BatchResult<int> overCap = new();
            overCap.Complete(11);
            BatchAssertionFailedException over = new(TellmaSqlErrors.LimitExceeded, TellmaSqlErrors.DeleteCapExceededCode, statementOrdinal: 2);
            Assert.Equal(11, Assert.IsType<LimitExceededException>(PersistFailures.Translate(over, ByQuery(overCap), _rig.Services.GetRequiredService<CrudMetrics>())).Actual);

            // --- A mismatch whose count was never read reports no count nobody read: it passes through ---
            FailPersist(mismatch);
            Assert.Same(mismatch, await Assert.ThrowsAsync<BatchAssertionFailedException>(() => TicketsAsync(tickets => tickets.DeleteByQueryAsync(new DeleteByQueryRequest("Name = 'x'", null, 2), Token))));
        }

        // --- A row still referenced ---

        [Fact]
        public async Task A_row_still_referenced_is_in_use_at_its_id_naming_the_referencing_entity_and_property()
        {
            FailPersist(new ForeignKeyViolationException("FK_Tickets_LabelId", statementOrdinal: 1));

            ValidationException one = await Assert.ThrowsAsync<ValidationException>(() => LabelsAsync(labels => labels.DeleteByIdsAsync([3], null, Token)));
            ValidationException several = await Assert.ThrowsAsync<ValidationException>(() => LabelsAsync(labels => labels.DeleteByIdsAsync([3, 4], null, Token)));

            Assert.Equal([$"Ids[0] {ValidationCodes.FkInUse}"], Lines(one));
            Assert.Equal((PipelineWriteModel.Tickets, nameof(PwTicket.LabelId)), (one.Errors[0].Arguments["entity"], one.Errors[0].Arguments["property"]));
            Assert.Equal([$"Ids {ValidationCodes.FkInUse}"], Lines(several));
        }

        // --- A re-entered persist ---

        [Fact]
        public async Task A_hooked_delete_recomposed_for_a_moved_tag_re_runs_its_validator_in_memory_and_replays_its_participant_without_calling_it_again()
        {
            int validated = 0;
            _hooks.ValidateDelete = async context =>
            {
                validated++;
                await context.Loader.LoadAsync();
            };
            int contributed = 0;
            _hooks.Contribute = context =>
            {
                // The persist batch's guard will find the permissions tag moved.
                if (++contributed == 1)
                {
                    _rig.GrantOnRecompose(Grants());
                }

                _ = context.Batch.Sql($"SELECT {"contributed"};", SqlOptions.None);
                return Task.CompletedTask;
            };

            AffectedResult deleted = await TicketsAsync(tickets => tickets.DeleteByIdsAsync([2, 1], null, Token));

            // --- The validator twice, the second time in memory; the participant once, replayed once ---
            Assert.Equal(2, deleted.Count);
            Assert.Equal((2, 1), (validated, contributed));
            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
            (ScriptedBatch refused, ScriptedBatch committed) = (_rig.Script.Batches[1], _rig.Script.Batches[2]);
            Assert.True(refused.IsRefused && committed.IsExecuted && !committed.IsRefused);
            Assert.Equal(refused.Statements.Select(static statement => statement.Kind), committed.Statements.Select(static statement => statement.Kind));
            Assert.Single(committed.Of(ScriptedKind.Sql));
            Assert.Single(committed.Of(ScriptedKind.Delete));
            _ = Assert.Single(_hooks.Outcomes);
        }

        // --- Helpers ---

        /// <summary>Grants the caller every operation of every stack, under the filters given.</summary>
        /// <param name="read">The filter of every Read grant; every row when null.</param>
        /// <param name="write">The filter of every Delete grant and action grant; every row when null.</param>
        private void Grant(string? read = null, string? write = null)
        {
            _rig.Grant(Grants(read, write));
        }

        /// <summary>Every operation of every stack, under the filters given.</summary>
        /// <param name="read">The filter of every Read grant; every row when null.</param>
        /// <param name="write">The filter of every Delete grant and action grant; every row when null.</param>
        /// <returns>The grants.</returns>
        private static (string Resource, string Action, string? Filter)[] Grants(string? read = null, string? write = null)
        {
            return
            [
                (PipelineWriteModel.Tickets, AccessActions.Read, read),
                (PipelineWriteModel.Tickets, AccessActions.Delete, write),
                (PipelineWriteModel.Tickets, AccessActions.Activate, write),
                (PipelineWriteModel.Tickets, "Close", write),
                (PipelineWriteModel.Tickets, "Count", write),
                (PipelineWriteModel.Tickets, "Rename", write),
                (PipelineWriteModel.Labels, AccessActions.Read, read),
                (PipelineWriteModel.Labels, AccessActions.Delete, write),
                (PipelineWriteModel.Labels, AccessActions.Activate, write),
                (PipelineWriteModel.Nodes, AccessActions.Read, read),
                (PipelineWriteModel.Nodes, AccessActions.Delete, write),
                (PipelineWriteModel.Bigs, AccessActions.Read, read),
                (PipelineWriteModel.Bigs, AccessActions.Delete, write),
            ];
        }

        /// <summary>Makes every persist batch's execution fail, as the executor raises a failure of its statements.</summary>
        /// <param name="failure">The failure.</param>
        private void FailPersist(Exception failure)
        {
            _ = _rig.Script.OnExecute((batch, _) => batch.Purpose == BatchPurpose.Persist ? Task.FromException(failure) : Task.CompletedTask);
        }

        /// <summary>What a delete by query of the tickets knows about its persist, its count as given.</summary>
        /// <param name="counted">The count of the rows matched.</param>
        /// <returns>The context.</returns>
        private PersistFailureContext ByQuery(BatchResult<int> counted)
        {
            EntityMetadata tickets = _rig.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(PwTicket));
            return new PersistFailureContext(PipelineWriteModel.Tickets, AccessActions.Delete, tickets, [], LanguageIndex: 1, CarriesQueryText: true)
            {
                DeleteCounts = new QueryDeleteCounts(Cap: 10, ExpectedCount: 2, counted),
            };
        }

        /// <summary>Runs a call on the tickets' service in a scope of the caller.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="call">The call.</param>
        /// <returns>What it answered.</returns>
        private async Task<T> TicketsAsync<T>(Func<PwTicketService, Task<T>> call)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            return await call(scope.ServiceProvider.GetRequiredService<PwTicketService>());
        }

        /// <summary>Runs a call on the labels' service in a scope of the caller.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="call">The call.</param>
        /// <returns>What it answered.</returns>
        private async Task<T> LabelsAsync<T>(Func<EntityService<PwLabel, int>, Task<T>> call)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            return await call(scope.ServiceProvider.GetRequiredService<EntityService<PwLabel, int>>());
        }

        /// <summary>Runs a call on the nodes' service in a scope of the caller.</summary>
        /// <typeparam name="T">What the call answers.</typeparam>
        /// <param name="call">The call.</param>
        /// <returns>What it answered.</returns>
        private async Task<T> NodesAsync<T>(Func<EntityService<PwNode, int>, Task<T>> call)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            return await call(scope.ServiceProvider.GetRequiredService<EntityService<PwNode, int>>());
        }

        /// <summary>A stored ticket, as the target load finds it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The ticket.</returns>
        private static PwTicket Ticket(int id)
        {
            return new PwTicket { Id = id, Name = "T", CreatedAt = Stamp, ModifiedAt = Stamp };
        }

        /// <summary>Answers a delete: by ids, the ids it names; otherwise the rows the test matched.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="statement">The delete.</param>
        /// <returns>The receipt.</returns>
        private DeleteReceipt Deleted<T>(ScriptedStatement statement)
            where T : class
        {
            HashSet<object> deleted = statement.Statement is DeleteSpec<T>.ByIds byIds ? [.. byIds.Ids] : [.. _matched];
            return new DeleteReceipt(deleted);
        }

        /// <summary>Answers an update by the ids it names.</summary>
        /// <typeparam name="T">The entity type.</typeparam>
        /// <param name="statement">The update.</param>
        /// <returns>The receipt.</returns>
        private static UpdateReceipt Updated<T>(ScriptedStatement statement)
            where T : class
        {
            HashSet<object> updated = statement.Statement is UpdateSpec<T>.ByIds byIds ? [.. byIds.Ids] : [];
            return new UpdateReceipt(updated);
        }

        /// <summary>A refusal's errors, each as its path and code, ordered.</summary>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The lines.</returns>
        private static string[] Lines(ValidationException refusal)
        {
            return [.. refusal.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal)];
        }
    }
}
