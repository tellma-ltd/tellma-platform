// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Data;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Writes
{
    // The actions over the scripted batch: the built-in activate and deactivate on one round trip
    // when nothing validates actions, the general path's two round trips, the overload an action's
    // result selects, the single target, the arguments read from JSON and walked under Arguments, a
    // method parked on its persist, and a partial failure that persists before it is rethrown.
    public sealed partial class WriteOperationTests
    {
        // --- The built-in actions on one round trip ---

        [Fact]
        public async Task A_built_in_action_of_a_stack_that_validates_nothing_updates_on_one_round_trip_all_or_nothing_under_both_filters()
        {
            Grant(read: "Name != 'hidden'", write: "Name != 'locked'");

            EntitiesResult<PwLabel> deactivated = await LabelsAsync(labels => labels.DeactivateAsync([2, 1, 2], cancellationToken: Token));

            // --- One persist: one update of each id once, unchanged rows skipped by the statement ---
            Assert.Equal([2L, 1L], deactivated.Ids);
            Assert.Empty(deactivated.Entities);
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            Assert.Equal((BatchPurpose.Persist, 1), (persist.Purpose, _rig.Script.RoundTrips));
            UpdateSpec<PwLabel>.ByIds spec = Assert.IsType<UpdateSpec<PwLabel>.ByIds>(Assert.Single(persist.Of(ScriptedKind.Update)).Statement);
            Assert.Equal([2, 1], spec.Ids.Cast<int>());
            Assert.Equal((false, true, true), ((bool)spec.Assignments[nameof(PwLabel.IsActive)]!, spec.Stamp, spec.AllOrNothing));
            Assert.Contains("hidden", QueryRig.Render(spec.ReadFilter), StringComparison.Ordinal);
            Assert.Contains("locked", QueryRig.Render(spec.Filter), StringComparison.Ordinal);

            // --- Activate assigns the flag the other way; no id sends nothing ---
            _ = await LabelsAsync(labels => labels.ActivateAsync([3], cancellationToken: Token));
            Assert.True((bool)Assert.IsType<UpdateSpec<PwLabel>.ByIds>(Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Update)).Statement).Assignments[nameof(PwLabel.IsActive)]!);
            Assert.Empty((await LabelsAsync(labels => labels.ActivateAsync([], cancellationToken: Token))).Ids);
            Assert.Equal(2, _rig.Script.RoundTrips);
        }

        // --- The general path ---

        [Fact]
        public async Task A_built_in_action_the_service_validates_loads_its_targets_on_round_trip_1_and_updates_on_a_second()
        {
            (string Action, int[] Loaded)? seen = null;
            _hooks.ValidateAction = async (action, context) =>
            {
                await context.Loader.LoadAsync();
                seen = (action, [.. context.Entities.Select(static ticket => ticket.Id)]);
            };

            EntitiesResult<PwTicket> activated = await TicketsAsync(tickets => tickets.ActivateAsync([3, 1], cancellationToken: Token));

            Assert.Equal([3L, 1L], activated.Ids);
            Assert.Equal("activate", seen!.Value.Action);
            Assert.Equal([3, 1], seen.Value.Loaded);
            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
            Assert.Equal(2, _rig.Script.RoundTrips);
            Assert.True((bool)Assert.IsType<UpdateSpec<PwTicket>.ByIds>(Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Update)).Statement).Assignments[nameof(PwTicket.IsActive)]!);
        }

        [Fact]
        public async Task A_declared_action_persists_what_it_composed_on_round_trip_2_the_rows_it_saved_under_override_and_its_updates_all_or_nothing()
        {
            _hooks.Close = async context =>
            {
                await context.Loader.LoadAsync();
                PwTicket first = context.Entities[0];
                first.Name = "Closed";
                context.Save([first, first]);
                context.Update(new Dictionary<string, object?> { [nameof(PwTicket.IsActive)] = false });
                _ = context.Batch.Sql($"SELECT {"closed"};", SqlOptions.None);
            };

            EntitiesResult<PwTicket> closed = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [2, 1], null, new ActionOptions(), Token));

            // --- Round trip 2: the emitter, the update, then the method's own statement ---
            Assert.Equal([2L, 1L], closed.Ids);
            Assert.Equal(2, _rig.Script.RoundTrips);
            ScriptedBatch persist = _rig.Script.Batches[1];
            Assert.Equal(
                [ScriptedKind.Save, ScriptedKind.Update, ScriptedKind.Sql],
                persist.Statements.Where(static statement => statement.Kind is ScriptedKind.Save or ScriptedKind.Update or ScriptedKind.Sql).Select(static statement => statement.Kind));
            ScriptedStatement save = Assert.Single(persist.Of(ScriptedKind.Save));
            Assert.Equal((ConcurrencyMode.Override, "Closed"), (save.Options, Assert.Single((IReadOnlyList<PwTicket>)save.Statement!).Name));
            Assert.True(Assert.IsType<UpdateSpec<PwTicket>.ByIds>(Assert.Single(persist.Of(ScriptedKind.Update)).Statement).AllOrNothing);

            // --- The post-commit phase sees the action, its targets and its ids ---
            ActionPersistOutcome<PwTicket, int> outcome = Assert.IsType<ActionPersistOutcome<PwTicket, int>>(Assert.Single(_hooks.Outcomes));
            Assert.Equal(PipelineWriteModel.Close, outcome.Action);
            Assert.Equal([2, 1], outcome.Ids);
            Assert.Equal([2, 1], outcome.Entities.Select(static ticket => ticket.Id));
        }

        [Fact]
        public async Task An_actions_absent_or_hidden_id_is_not_found_before_a_row_outside_its_grant_is_forbidden()
        {
            Grant(write: "Name != 'locked'");
            _stored.Remove(9);
            _granted = 0;

            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1, 9], null, new ActionOptions(), Token)));
            ForbiddenException outside = await Assert.ThrowsAsync<ForbiddenException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1], null, new ActionOptions(), Token)));

            Assert.Equal(["9"], missing.Ids);
            Assert.Equal(("Close", ForbiddenReason.OutsideFilter), (outside.Action, outside.Reason));
            Assert.All(_rig.Script.Batches, static batch => Assert.Equal(BatchPurpose.Validate, batch.Purpose));
        }

        // --- The overloads and the single target ---

        [Fact]
        public async Task An_action_is_reached_through_the_overload_its_result_selects_and_the_other_is_refused()
        {
            _hooks.Count = static context => Task.FromResult(40 + context.Ids[0]);

            // --- The typed overload answers the method's own value, without a read-back ---
            int counted = await TicketsAsync(tickets => tickets.ExecuteActionAsync<int>(PipelineWriteModel.Count, [2], null, new ActionOptions(ReturnEntities: true), Token));
            Assert.Equal(42, counted);
            Assert.Equal(42, await TicketsAsync(tickets => tickets.ExecuteActionAsync<object>(PipelineWriteModel.Count, [2], null, new ActionOptions(), Token)));
            int trips = _rig.Script.RoundTrips;

            // --- Every mismatch is an authoring bug, refused before any round trip ---
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Count, [2], null, new ActionOptions(), Token)));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => TicketsAsync(tickets => tickets.ExecuteActionAsync<int>(PipelineWriteModel.Close, [2], null, new ActionOptions(), Token)));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => TicketsAsync(tickets => tickets.ExecuteActionAsync<string>(PipelineWriteModel.Count, [2], null, new ActionOptions(), Token)));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close + "-not", [2], null, new ActionOptions(), Token)));
            Assert.Equal(trips, _rig.Script.RoundTrips);
        }

        [Fact]
        public async Task A_single_target_action_called_with_other_than_one_id_is_refused_before_any_round_trip()
        {
            InvalidOperationException two = await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync<int>(PipelineWriteModel.Count, [1, 2], null, new ActionOptions(), Token)));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => TicketsAsync(tickets => tickets.ExecuteActionAsync<int>(PipelineWriteModel.Count, [], null, new ActionOptions(), Token)));

            Assert.Contains("one id", two.Message, StringComparison.Ordinal);
            Assert.Equal(0, _rig.Script.RoundTrips);
        }

        // --- The arguments ---

        [Fact]
        public async Task Arguments_read_from_json_are_walked_by_their_attributes_and_each_failure_is_reported_under_Arguments()
        {
            PwRename? received = null;
            _hooks.Rename = (_, arguments) =>
            {
                received = arguments;
                return Task.CompletedTask;
            };

            // --- JSON reads as the record under the platform's options ---
            _ = await RenameAsync(Json(/*lang=json,strict*/ """{ "name": "Short", "priority": 3 }"""));
            Assert.Equal(new PwRename { Name = "Short", Priority = 3 }, received);

            // --- A record passed as itself is walked alike ---
            ValidationException invalid = await Assert.ThrowsAsync<ValidationException>(() => RenameAsync(Json(/*lang=json,strict*/ """{ "name": "Too long", "priority": 12 }""")));
            Assert.Equal([$"Arguments.Name {ValidationCodes.MaxLength}", $"Arguments.Priority {ValidationCodes.Range}"], Lines(invalid));
            ValidationException missing = await Assert.ThrowsAsync<ValidationException>(() => RenameAsync(new PwRename { Priority = 2 }));
            Assert.Equal([$"Arguments.Name {ValidationCodes.Required}"], Lines(missing));

            // --- A shape that does not read, none where some are taken, some where none are: the caller's mistake ---
            _ = await Assert.ThrowsAsync<BadRequestException>(() => RenameAsync(Json(/*lang=json,strict*/ """{ "name": 5 }""")));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => RenameAsync(null));
            _ = await Assert.ThrowsAsync<BadRequestException>(() => RenameAsync(Json("null")));
            _ = await Assert.ThrowsAsync<BadRequestException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1], Json(/*lang=json,strict*/ """{ "name": "x" }"""), new ActionOptions(), Token)));
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1], new PwRename(), new ActionOptions(), Token)));

            // The refusals came before any round trip; only the first rename ran.
            Assert.Equal(2, _rig.Script.RoundTrips);
        }

        // --- The method and its persist ---

        [Fact]
        public async Task A_method_awaiting_Persisted_parks_while_round_trip_2_is_sent_and_then_reads_the_results_of_its_appends()
        {
            RawResult answered = new([]);
            _ = _rig.Script.OnSql(_ => answered);
            bool resumed = false;
            bool? resumedWhenSent = null;
            _ = _rig.Script.OnExecute((batch, _) =>
            {
                if (batch.Purpose == BatchPurpose.Persist)
                {
                    resumedWhenSent = resumed;
                }

                return Task.CompletedTask;
            });
            RawResult? read = null;
            _hooks.Close = async context =>
            {
                await context.Loader.LoadAsync();
                BatchResult<RawResult> appended = context.Batch.Sql($"SELECT {"closed"};", SqlOptions.None);
                context.Update(new Dictionary<string, object?> { [nameof(PwTicket.Name)] = "closed" });
                await context.Persisted;
                resumed = true;
                read = appended.Value;
            };

            _ = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1], null, new ActionOptions(), Token));

            // --- The persist was sent while the method waited; it resumed after the commit ---
            Assert.Equal(false, resumedWhenSent);
            Assert.True(resumed);
            Assert.Same(answered, read);
            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
        }

        [Fact]
        public async Task A_method_awaiting_Persisted_sees_the_failure_that_kept_the_persist_from_running()
        {
            Exception? seen = null;
            _hooks.ValidateAction = static (_, context) =>
            {
                context.Errors.Add(ValidationPath.Root.Property("Ids").Index(0), "Pw.Closed");
                return Task.CompletedTask;
            };
            _hooks.Close = async context =>
            {
                await context.Loader.LoadAsync();
                try
                {
                    await context.Persisted;
                }
                catch (ValidationException failure)
                {
                    seen = failure;
                }
            };

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1], null, new ActionOptions(), Token)));

            Assert.Equal(["Ids[0] Pw.Closed"], Lines(refused));
            await WaitForAsync(() => seen is not null);
            Assert.Same(refused, seen);
            Assert.All(_rig.Script.Batches, static batch => Assert.Equal(BatchPurpose.Validate, batch.Purpose));
        }

        [Fact]
        public async Task A_partial_failure_persists_what_the_method_composed_then_is_rethrown_once_committed()
        {
            PartialFailureException partial = new("Pw.Partial", new { Closed = 1 }, ["2"]);
            _hooks.Close = async context =>
            {
                await context.Loader.LoadAsync();
                context.Update(new Dictionary<string, object?> { [nameof(PwTicket.Name)] = "partly" });
                throw partial;
            };

            PartialFailureException thrown = await Assert.ThrowsAsync<PartialFailureException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [1, 2], null, new ActionOptions(), Token)));

            Assert.Same(partial, thrown);
            ScriptedBatch persist = _rig.Script.Batches[^1];
            Assert.True(persist.Purpose == BatchPurpose.Persist && persist.IsExecuted);
            Assert.Single(persist.Of(ScriptedKind.Update));
            Assert.Single(_hooks.Outcomes);
        }

        // --- A commit whose response was lost ---

        [Fact]
        public async Task A_proven_commit_answers_a_delete_with_its_ids_and_an_action_with_its_ids_alone_once_its_method_cannot_read_what_it_appended()
        {
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Committed));

            // --- A delete: the ids it was asked to delete answer it ---
            AffectedResult deleted = await LabelsAsync(labels => labels.DeleteByIdsAsync([1, 2], null, Token));
            Assert.Equal(2, deleted.Count);

            // --- An action whose method reads a result the lost batch never answered: logged, and the ids answer it ---
            _hooks.Close = async context =>
            {
                await context.Loader.LoadAsync();
                BatchResult<RawResult> appended = context.Batch.Sql($"SELECT {"closed"};", SqlOptions.None);
                context.Update(new Dictionary<string, object?> { [nameof(PwTicket.Name)] = "closed" });
                await context.Persisted;
                _ = appended.Value;
            };
            EntitiesResult<PwTicket> closed = await TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Close, [2, 1], null, new ActionOptions(), Token));

            Assert.Equal([2L, 1L], closed.Ids);
            Assert.Empty(closed.Entities);
            _ = Assert.Single(_hooks.Outcomes);
            FakeLogRecord logged = Assert.Single(_rig.Services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Name == "ProvenCommitReadFailed");
            Assert.Equal(LogLevel.Warning, logged.Level);
        }

        [Fact]
        public async Task A_proven_commit_raises_the_failure_of_a_method_answering_a_value_of_its_own_and_an_unproven_one_asks_the_caller_to_reload()
        {
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Committed));
            _hooks.Count = async context =>
            {
                BatchResult<RawResult> appended = context.Batch.Sql($"SELECT {"counted"};", SqlOptions.None);
                await context.Persisted;
                return appended.Value.ResultSets.Count;
            };

            // --- The method has no value to answer with once it failed ---
            _ = await Assert.ThrowsAsync<InvalidOperationException>(
                () => TicketsAsync(tickets => tickets.ExecuteActionAsync<int>(PipelineWriteModel.Count, [1], null, new ActionOptions(), Token)));
            _ = Assert.Single(_hooks.Outcomes);

            // --- A commit the probe cannot prove: the caller reloads to learn the outcome ---
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Unknown));
            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(
                () => LabelsAsync(labels => labels.DeleteByIdsAsync([1], null, Token)));
            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
        }

        // --- Which stacks need the validation round ---

        [Fact]
        public async Task Only_an_overridden_hook_of_the_service_or_of_a_component_sends_a_delete_or_a_built_in_action_through_the_validation_round()
        {
            await using AsyncServiceScope scope = _rig.Scope();
            EntityService<PwLabel, int> labels = scope.ServiceProvider.GetRequiredService<EntityService<PwLabel, int>>();
            PwTicketService tickets = scope.ServiceProvider.GetRequiredService<PwTicketService>();

            // --- The service: the platform's default overrides nothing; the tickets' overrides both ---
            Assert.False(HookOverrides.ValidatesDeletes(labels, []));
            Assert.False(HookOverrides.ValidatesActions(labels, []));
            Assert.True(HookOverrides.ValidatesDeletes(tickets, []));
            Assert.True(HookOverrides.ValidatesActions(tickets, []));

            // --- A component: only the hook it implements counts ---
            Assert.False(HookOverrides.ValidatesDeletes(labels, [new PwSaveOnlyValidator(), new PwActionValidator()]));
            Assert.True(HookOverrides.ValidatesDeletes(labels, [new PwSaveOnlyValidator(), new PwDeleteValidator()]));
            Assert.False(HookOverrides.ValidatesActions(labels, [new PwDeleteValidator()]));
            Assert.True(HookOverrides.ValidatesActions(labels, [new PwActionValidator()]));
        }

        // --- Action helpers ---

        /// <summary>Renames ticket 1 with the arguments given.</summary>
        /// <param name="arguments">The arguments: the record, its JSON, or null.</param>
        /// <returns>The action's answer.</returns>
        private Task<EntitiesResult<PwTicket>> RenameAsync(object? arguments)
        {
            return TicketsAsync(tickets => tickets.ExecuteActionAsync(PipelineWriteModel.Rename, [1], arguments, new ActionOptions(), Token));
        }

        /// <summary>Parses JSON text.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The element.</returns>
        private static JsonElement Json(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        /// <summary>Waits, briefly, for a flow the operation left running to reach a state.</summary>
        /// <param name="reached">Whether the state is reached.</param>
        /// <returns>A task that completes once it is.</returns>
        private static async Task WaitForAsync(Func<bool> reached)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (!reached())
            {
                await Task.Delay(10, timeout.Token);
            }
        }
    }
}
