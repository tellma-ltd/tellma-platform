// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud;
using Tellma.Core.Crud.Save;
using Tellma.Core.Data;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Save
{
    // The persist step of the save pipeline over the scripted batch: the batch's assembly in its
    // fixed order, the post-check and the read-back riding it, the tenant-state verdict before it,
    // a re-entered compose that re-runs the validators in memory and replays the participants, the
    // translation of the executor's failures, and the post-commit phase with its instruments.
    public sealed partial class SaveOperationTests
    {
        // --- The persist's assembly ---

        [Fact]
        public async Task The_persist_appends_the_emitter_then_the_hook_and_each_effect_in_order_then_the_callers_statements_and_commits_on_one_round_trip()
        {
            Warm();
            List<string> calls = [];
            _persistence.Contribute = (participant, context) =>
            {
                calls.Add("contribute " + participant);
                _ = context.Batch.Sql($"SELECT {participant};", SqlOptions.None);
                return Task.CompletedTask;
            };
            _persistence.AfterCommit = (participant, outcome) =>
            {
                calls.Add("after " + participant);
                Assert.Equal(Committed, outcome.Stamp);
                return Task.CompletedTask;
            };
            SaveOptions options = IdsOnly();
            options.OnPersist = batch =>
            {
                calls.Add("on-persist");
                _ = batch.Sql($"SELECT {"caller"};", SqlOptions.None);
            };

            EntitiesResult<PsDocument> saved = await SaveAsync(options, New(-1, "A"));

            // --- One batch: the emitter's save of the final rows, then each participant's statement ---
            Assert.Equal([101L], saved.Ids);
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            Assert.Equal((BatchPurpose.Persist, 1), (persist.Purpose, _rig.Script.RoundTrips));
            Assert.Same(QueryexProfiles.PipelineLimits, persist.Limits);
            Assert.Equal([ScriptedKind.Save, ScriptedKind.Sql, ScriptedKind.Sql, ScriptedKind.Sql, ScriptedKind.Sql], persist.Statements.Select(static statement => statement.Kind));
            ScriptedStatement save = persist.Statements[0];
            Assert.Equal(101, Assert.Single((IReadOnlyList<PsDocument>)save.Statement!).Id);
            Assert.Equal(ConcurrencyMode.Check, save.Options);
            Assert.Equal(
                [PsPersistence.Service, PsPersistence.First, PsPersistence.Second, "caller"],
                persist.Of(ScriptedKind.Sql).Select(static statement => (string)statement.Text.GetArgument(0)!));

            // --- Each participant once, the caller's last; after the commit, the same order ---
            Assert.Equal(
                ["contribute service", "contribute first", "contribute second", "on-persist", "after service", "after first", "after second"],
                calls);
        }

        [Fact]
        public async Task An_update_validates_on_one_round_trip_and_persists_on_a_second_its_save_under_the_callers_concurrency_mode()
        {
            Store(5);
            Store(6);
            using MetricCollector<long> overrides = Collect(CrudTelemetryNames.ConcurrencyOverrides);

            _ = await SaveAsync(IdsOnly(), Update(5, "A"));

            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
            Assert.Equal(2, _rig.Script.RoundTrips);
            Assert.Equal(ConcurrencyMode.Check, Assert.Single(_rig.Script.Batches[1].Of(ScriptedKind.Save)).Options);
            Assert.Empty(overrides.GetMeasurementSnapshot());

            // --- Under override, a stale update persists, and the committed override of a change the
            // caller had not seen is counted; an update echoing the stored stamp and a create are not ---
            PsDocument stale = Update(6, "B");
            stale.ModifiedAt = Stamp.AddMinutes(-1);
            _ = await SaveAsync(new SaveOptions { ReturnEntities = false, Concurrency = ConcurrencyMode.Override }, stale);
            _ = await SaveAsync(new SaveOptions { ReturnEntities = false, Concurrency = ConcurrencyMode.Override }, Update(6, "C"));
            Warm();
            _ = await SaveAsync(new SaveOptions { ReturnEntities = false, Concurrency = ConcurrencyMode.Override }, New(-1, "D"));

            Assert.Equal(ConcurrencyMode.Override, Assert.Single(_rig.Script.Batches[3].Of(ScriptedKind.Save)).Options);
            Assert.Equal(1L, Assert.Single(overrides.GetMeasurementSnapshot()).Value);
        }

        [Fact]
        public async Task A_filtered_save_grant_post_checks_the_saved_ids_under_its_filter_and_a_row_outside_it_at_commit_is_forbidden()
        {
            Grant(categories: null, save: "Name != 'locked'");
            Store(5);
            Store(6);
            _saveCount = 2;

            _ = await SaveAsync(IdsOnly(), Update(5, "A"), Update(6, "B"));

            // --- After the emitter: every root of the payload must be in the filter's reach ---
            ScriptedBatch persist = _rig.Script.Batches[^1];
            ScriptedStatement save = Assert.Single(persist.Of(ScriptedKind.Save));
            ScriptedStatement check = Assert.Single(persist.Of(ScriptedKind.Assert));
            Assert.True(save.Ordinal < check.Ordinal);
            Assert.Equal((PipelineSaveModel.Documents, "RowSecurity", (2, TellmaSqlErrors.RowSecurity)), (check.Spec.Root.Name, check.Name, ((int, int))check.Options!));
            Assert.Contains("Name != 'locked'", QueryRig.Render(check.Spec.Filter), StringComparison.Ordinal);
            Assert.Equal($"@save{save.Ordinal}_saved", Assert.Single(check.Spec.Restrictions).TableSource);

            // --- The executor raises the check's failure as row security: a row left the filter ---
            _saveCount = 1;
            FailPersist(new RowSecurityException(check.Ordinal));
            ForbiddenException outside = await Assert.ThrowsAsync<ForbiddenException>(() => SaveAsync(IdsOnly(), Update(5, "A")));
            Assert.Equal((PipelineSaveModel.Documents, AccessActions.Save, ForbiddenReason.OutsideFilter), (outside.Resource, outside.Action, outside.Reason));

            // --- An unfiltered grant checks nothing ---
            Grant(categories: null);
            _ = _rig.Script.OnExecute(static (_, _) => Task.CompletedTask);
            _ = await SaveAsync(IdsOnly(), Update(5, "A"));
            Assert.Empty(_rig.Script.Batches[^1].Of(ScriptedKind.Assert));
        }

        [Fact]
        public async Task The_read_back_rides_the_persist_over_the_saved_ids_and_the_save_returns_the_rows_it_reads()
        {
            Warm();
            PsDocument stored = new() { Id = 101, Name = "A", Title = "T", ModifiedAt = Committed };
            _readBack.Add(stored);

            EntitiesResult<PsDocument> saved = await SaveAsync(New(-1, "A"));

            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            ScriptedStatement save = Assert.Single(persist.Of(ScriptedKind.Save));
            ScriptedStatement read = Assert.Single(persist.Of(ScriptedKind.Query));
            Assert.True(save.Ordinal < read.Ordinal);
            Assert.Equal($"@save{save.Ordinal}_saved", Assert.Single(read.Query<PsDocument>().Restrictions).TableSource);
            Assert.Equal([101L], saved.Ids);
            Assert.Same(stored, Assert.Single(saved.Entities));
        }

        [Fact]
        public async Task A_save_whose_validation_fails_calls_no_persist_participant_and_sends_no_persist()
        {
            Warm();
            List<string> called = [];
            _persistence.Contribute = (participant, _) =>
            {
                called.Add(participant);
                return Task.CompletedTask;
            };
            SaveOptions options = IdsOnly();
            options.OnPersist = _ => called.Add("caller");
            _validation.Validate = static context =>
            {
                context.Errors.Add(ValidationPath.Root.Index(0).Property(nameof(PsDocument.Name)), ValidationCodes.Required);
                return Task.CompletedTask;
            };

            _ = await RefusedAsync(options, New(-1, "A"));

            Assert.Empty(called);
            Assert.Empty(_rig.Script.Batches);
        }

        [Fact]
        public async Task A_participant_may_only_append_so_executing_the_batch_or_changing_its_mode_while_contributing_is_refused()
        {
            Warm();
            _persistence.Contribute = static (participant, context) => participant == PsPersistence.First ? context.Batch.ExecuteAsync(Token) : Task.CompletedTask;

            InvalidOperationException executed = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(IdsOnly(), New(-1, "A")));
            Assert.Contains("the pipeline executes it", executed.Message, StringComparison.Ordinal);

            _persistence.Contribute = static (_, context) =>
            {
                context.Batch.TransactionMode = TransactionMode.None;
                return Task.CompletedTask;
            };
            _ = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(IdsOnly(), New(-1, "B")));

            Assert.DoesNotContain(_rig.Script.Batches, static batch => batch.IsExecuted);
        }

        [Fact]
        public async Task A_participant_writes_the_stacks_tables_under_its_authority_and_OnPersist_under_the_callers_alone()
        {
            Warm();
            TableName documents = new("ps", "Documents");
            _rig.Script.Owners[documents] = PipelineSaveModel.Documents;
            SqlOptions writes = new(writes: new HashSet<TableName> { documents });

            // The build-time ownership rule sees these texts in the test class; at run time the first
            // is the service hook's own and the second the caller's, which the batch refuses.
#pragma warning disable TELLMA0006
            _persistence.Contribute = (participant, context) =>
            {
                if (participant == PsPersistence.Service)
                {
                    _ = context.Batch.Sql($"UPDATE [ps].[Documents] SET [Audit] = {"hook"} WHERE [Id] IN (SELECT [Id] FROM {context.AffectedIds});", writes);
                }

                return Task.CompletedTask;
            };

            _ = await SaveAsync(IdsOnly(), New(-1, "A"));
            _ = Assert.Single(_rig.Script.Batches[^1].Of(ScriptedKind.Sql));

            // --- The caller's statement may not write the stack's table: refused at append, nothing sent ---
            _persistence.Contribute = null;
            SaveOptions options = IdsOnly();
            options.OnPersist = batch => _ = batch.Sql($"UPDATE [ps].[Documents] SET [Audit] = {"caller"} WHERE 1 = 0;", writes);
#pragma warning restore TELLMA0006
            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(options, New(-1, "B")));

            Assert.Contains($"which the stack '{PipelineSaveModel.Documents}' owns", refused.Message, StringComparison.Ordinal);
            Assert.False(_rig.Script.Batches[^1].IsExecuted);
        }

        // --- The tenant-state verdict ---

        [Fact]
        public async Task A_read_only_or_frozen_tenant_refuses_a_validated_save_before_its_persist_is_composed_unless_a_frame_admits_it()
        {
            Warm();
            int validated = 0;
            _validation.Validate = _ =>
            {
                validated++;
                return Task.CompletedTask;
            };
            (TenantState State, string Code)[] refusals = [(TenantState.ReadOnly, TenantProblemCodes.ReadOnly), (TenantState.Frozen, TenantProblemCodes.Frozen)];

            foreach ((TenantState state, string code) in refusals)
            {
                _rig.TenantState = state;
                TenantUnavailableException refused = await Assert.ThrowsAsync<TenantUnavailableException>(() => SaveAsync(IdsOnly(), New(-1, "A")));
                Assert.Equal(code, refused.Code);
            }

            Assert.Equal(2, validated);
            Assert.Empty(_rig.Script.Batches);

            // --- A job whose handler runs on a read-only tenant admits its persist ---
            _rig.TenantState = TenantState.ReadOnly;
            await using (AsyncServiceScope scope = _rig.Scope())
            {
                using (scope.ServiceProvider.GetRequiredService<WriteFrames>().Enter(new WriteFrame(WriteHostKind.Job, "ps.reindex", AdmitsReadOnly: true)))
                {
                    _ = await Documents(scope).SaveAsync([New(-1, "B")], IdsOnly(), Token);
                }
            }

            Assert.Equal(BatchPurpose.Persist, Assert.Single(_rig.Script.Batches).Purpose);
        }

        // --- A re-entered compose ---

        [Fact]
        public async Task A_persist_recomposed_for_a_moved_tag_re_runs_the_validators_in_memory_and_replays_the_participants_without_calling_them_again()
        {
            Warm();
            _visible[PipelineSaveModel.Categories] = [3];
            int validated = 0;
            _validation.Validate = async context =>
            {
                validated++;
                _ = context.Loader.ByIds<PsCategory, int>([3]);
                await context.Loader.LoadAsync();
            };
            Dictionary<string, int> contributed = new(StringComparer.Ordinal);
            _persistence.Contribute = (participant, context) =>
            {
                contributed[participant] = contributed.GetValueOrDefault(participant) + 1;
                _ = context.Batch.Sql($"SELECT {participant};", SqlOptions.None);
                if (participant == PsPersistence.Service)
                {
                    // The persist batch's guard will find the permissions tag moved.
                    _rig.GrantOnRecompose(Grants(categories: null));
                }

                return Task.CompletedTask;
            };
            int onPersist = 0;
            SaveOptions options = IdsOnly();
            options.OnPersist = batch =>
            {
                onPersist++;
                _ = batch.Sql($"SELECT {"caller"};", SqlOptions.None);
            };
            using MetricCollector<long> reruns = Collect(CrudTelemetryNames.StaleContextReruns);

            EntitiesResult<PsDocument> saved = await SaveAsync(options, New(-1, "A"));

            // --- Every participant once; the validators twice, the second time answered from round 1 ---
            Assert.Equal([101L], saved.Ids);
            Assert.Equal(2, validated);
            Assert.Equal((1, 1, 1, 1), (contributed[PsPersistence.Service], contributed[PsPersistence.First], contributed[PsPersistence.Second], onPersist));

            // --- The refused batch ran nothing; the recomposed one holds its statements in its order ---
            Assert.Equal([BatchPurpose.Validate, BatchPurpose.Persist, BatchPurpose.Persist], _rig.Script.Batches.Select(static batch => batch.Purpose));
            (ScriptedBatch refused, ScriptedBatch committed) = (_rig.Script.Batches[1], _rig.Script.Batches[2]);
            Assert.True(refused.IsRefused);
            Assert.True(committed.IsExecuted && !committed.IsRefused);
            Assert.Equal(Describe(refused), Describe(committed));
            Assert.Equal("permissions", Assert.Single(reruns.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.TagTag]);
        }

        [Fact]
        public async Task A_re_entered_persist_whose_validator_asks_for_a_load_the_rounds_did_not_serve_is_stale_and_sends_nothing_more()
        {
            Warm();
            int validated = 0;
            _validation.Validate = async context =>
            {
                if (++validated == 2)
                {
                    _ = context.Loader.ByIds<PsCategory, int>([3]);
                    await context.Loader.LoadAsync();
                }
            };
            RecomposeOnFirstContribution(Grants(categories: null));

            _ = await Assert.ThrowsAsync<StaleContextException>(() => SaveAsync(IdsOnly(), New(-1, "A")));

            // The refused persist alone: the recomposed one was never sent.
            Assert.Equal(1, _rig.Script.RoundTrips);
            Assert.False(_rig.Script.Batches[^1].IsExecuted);
        }

        [Fact]
        public async Task A_re_entered_persist_is_refused_by_a_new_error_a_new_unconfirmed_warning_or_a_revoked_save_grant()
        {
            Warm();
            int validated = 0;
            Action<SaveContext<PsDocument>>? again = null;
            _validation.Validate = context =>
            {
                if (++validated % 2 == 0)
                {
                    again?.Invoke(context);
                }

                return Task.CompletedTask;
            };

            // --- A new error ---
            again = static context => context.Errors.Add(ValidationPath.Root.Index(0).Property(nameof(PsDocument.Name)), ValidationCodes.Required);
            RecomposeOnFirstContribution(Grants(categories: null));
            ValidationException invalid = await RefusedAsync(IdsOnly(), New(-1, "A"));
            Assert.Equal(["[0].Name Required"], Lines(invalid));

            // --- A new unconfirmed warning ---
            again = static context => context.Host.Confirmations.Require(PipelineSaveModel.Recost, [context.Entities[0].Id]);
            RecomposeOnFirstContribution(Grants(categories: null));
            ConfirmationRequiredException warned = await Assert.ThrowsAsync<ConfirmationRequiredException>(() => SaveAsync(IdsOnly(), New(-1, "B")));
            Assert.Equal(PipelineSaveModel.Recost, Assert.Single(warned.Requests).Key);

            // --- The save grant revoked since the scope cached its caller ---
            again = null;
            RecomposeOnFirstContribution([.. Grants(categories: null).Where(static grant => grant.Action != AccessActions.Save)]);
            ForbiddenException denied = await Assert.ThrowsAsync<ForbiddenException>(() => SaveAsync(IdsOnly(), New(-1, "C")));
            Assert.Equal((PipelineSaveModel.Documents, AccessActions.Save, ForbiddenReason.NoGrant), (denied.Resource, denied.Action, denied.Reason));

            // Each refusal followed one refused persist; none was sent again.
            Assert.Equal(3, _rig.Script.RoundTrips);
        }

        // --- The translation of the executor's failures ---

        [Fact]
        public async Task A_conflict_the_emitter_reports_names_each_modifier_and_conflicts_on_missing_rows_alone_are_not_found()
        {
            Store(5);
            Store(6);
            using MetricCollector<long> conflicts = Collect(CrudTelemetryNames.ConcurrencyConflicts);
            FailPersist(new ConcurrencyConflictException([new ConflictRow(5, false, Stamp, Modifier, "Bea", null, null), Missing(6)], statementOrdinal: 1));

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => SaveAsync(IdsOnly(), Update(5, "A"), Update(6, "B")));

            Assert.Equal("concurrency-conflict", conflict.Code);
            Assert.Equal(
                [new ConcurrencyConflict(5, "2026-11-16T08:30:00.1230000+03:00", Modifier, "Bea", IsMissing: false), new ConcurrencyConflict(6, null, null, null, IsMissing: true)],
                conflict.Conflicts);

            // --- Every row missing: deleted under the caller, so no concurrency conflict ---
            FailPersist(new ConcurrencyConflictException([Missing(5), Missing(6)], statementOrdinal: 1));
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => SaveAsync(IdsOnly(), Update(5, "A"), Update(6, "B")));
            Assert.Equal(PipelineSaveModel.Documents, missing.Resource);
            Assert.Equal(["5", "6"], missing.Ids);

            // --- Counted by kind ---
            Assert.Equal(
                [(CrudMetrics.MissingConflict, 1L), (CrudMetrics.StampConflict, 1L), (CrudMetrics.MissingConflict, 2L)],
                conflicts.GetMeasurementSnapshot().Select(static measurement => ((string)measurement.Tags[CrudTelemetryNames.KindTag]!, measurement.Value)));
        }

        [Fact]
        public void A_conflicts_modifier_is_named_in_the_content_language_falling_back_to_the_primary_name()
        {
            PersistFailureContext context = new(PipelineSaveModel.Documents, AccessActions.Save, Metadata<PsDocument>(), [], LanguageIndex: 2);
            ConcurrencyConflictException failure = new([new ConflictRow(5, false, Stamp, 7, "Bea", "Beatrix", null), new ConflictRow(6, false, Stamp, 8, "Ann", "", null)], statementOrdinal: 1);

            ConcurrencyException conflict = Assert.IsType<ConcurrencyException>(PersistFailures.Translate(failure, context, _rig.Services.GetRequiredService<CrudMetrics>()));

            Assert.Equal(["Beatrix", "Ann"], conflict.Conflicts.Select(static row => row.ModifiedByName));
        }

        [Fact]
        public async Task The_executors_assertions_are_translated_by_their_number_and_an_unknown_one_passes_through()
        {
            Warm();
            (BatchAssertionFailedException Failure, string Expected, string? Reason)[] cases =
            [
                (new(TellmaSqlErrors.Invariant, "Ps.Balanced", statementOrdinal: 3), " Ps.Balanced", null),
                (new(TellmaSqlErrors.Invariant, ValidationCodes.BlobNotAttachable, statementOrdinal: 3), $" {ValidationCodes.BlobNotAttachable}", "persist"),
                (new(TellmaSqlErrors.DistroMin + 50, "Acme.PeriodClosed", statementOrdinal: 3), " Acme.PeriodClosed", null),
            ];

            foreach ((BatchAssertionFailedException failure, string expected, string? reason) in cases)
            {
                FailPersist(failure);
                ValidationException refused = await RefusedAsync(IdsOnly(), New(-1, "A"));
                Assert.Equal([expected], Lines(refused));
                Assert.Equal(reason, refused.Errors[0].Arguments.GetValueOrDefault("reason"));
            }

            // --- A row the batch's statements found missing ---
            FailPersist(new BatchAssertionFailedException(TellmaSqlErrors.NotFound, "NotFound", statementOrdinal: 3, missingIds: [9, 11]));
            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => SaveAsync(IdsOnly(), New(-1, "A")));
            Assert.Equal(PipelineSaveModel.Documents, missing.Resource);
            Assert.Equal(["9", "11"], missing.Ids);

            // --- A number neither the platform's bands nor a distribution's ---
            BatchAssertionFailedException unknown = new(TellmaSqlErrors.Transient, "Transient", statementOrdinal: 3);
            FailPersist(unknown);
            Assert.Same(unknown, await Assert.ThrowsAsync<BatchAssertionFailedException>(() => SaveAsync(IdsOnly(), New(-1, "A"))));
        }

        [Fact]
        public async Task A_cycle_or_a_depth_the_tree_fence_finds_at_persist_is_refused_at_the_root_and_the_cycle_counted_as_a_conflict()
        {
            Warm();
            using MetricCollector<long> conflicts = Collect(CrudTelemetryNames.ConcurrencyConflicts);

            FailPersist(new TreeCycleException(typeof(PsNode), statementOrdinal: 1));
            ValidationException cycle = await SaveNodesRefusedAsync(new PsNode { Id = -1, Code = "a" });
            FailPersist(new TreeDepthExceededException(typeof(PsNode), statementOrdinal: 1));
            ValidationException deep = await SaveNodesRefusedAsync(new PsNode { Id = -1, Code = "b" });

            Assert.Equal([$" {ValidationCodes.TreeCycle}"], Lines(cycle));
            Assert.Equal([$" {ValidationCodes.TreeTooDeep}"], Lines(deep));
            CollectedMeasurement<long> counted = Assert.Single(conflicts.GetMeasurementSnapshot());
            Assert.Equal((CrudMetrics.CycleConflict, 1L), ((string)counted.Tags[CrudTelemetryNames.KindTag]!, counted.Value));
        }

        [Fact]
        public async Task A_unique_index_raced_is_refused_at_the_one_row_carrying_the_value_else_at_the_root_naming_the_property()
        {
            Warm();
            (string Index, Func<PsDocument[]> Payload, string[] Expected, string? Property)[] cases =
            [
                ("UX_Documents_Code", static () => [WithLine()], ["[0].Code Unique"], null),
                ("UX_Documents_Code", static () => [New(-1, "A", code: "X"), New(-2, "B", code: "Y")], [" Unique"], "Code"),
                ("UX_Lines_Barcode", static () => [WithLine()], ["[0].Lines[0].Barcode Unique"], null),
                ("UX_Assignments_OpenTarget", static () => [New(-1, "A")], [" Assignments.Concurrent"], null),
            ];

            foreach ((string index, Func<PsDocument[]> payload, string[] expected, string? property) in cases)
            {
                FailPersist(new UniqueConstraintViolationException(index, statementOrdinal: 1));
                ValidationException refused = await RefusedAsync(IdsOnly(), payload());
                Assert.Equal(expected, Lines(refused));
                Assert.Equal(property, refused.Errors[0].Arguments.GetValueOrDefault("property"));
            }

            // --- An index no table of the stack holds passes through ---
            UniqueConstraintViolationException foreign = new("UX_Elsewhere_Code", statementOrdinal: 1);
            FailPersist(foreign);
            Assert.Same(foreign, await Assert.ThrowsAsync<UniqueConstraintViolationException>(() => SaveAsync(IdsOnly(), New(-1, "A"))));

            // A new document with a code and one line with a barcode.
            static PsDocument WithLine()
            {
                PsDocument document = New(-1, "A", code: "X");
                document.Lines = [new PsLine { Barcode = "B1" }];
                return document;
            }
        }

        [Fact]
        public async Task A_reference_deleted_before_the_commit_is_not_found_at_the_one_row_carrying_it_else_at_the_root()
        {
            Warm();
            _visible[PipelineSaveModel.Categories] = [3, 4];

            FailPersist(new ForeignKeyViolationException("FK_Documents_CategoryId", statementOrdinal: 1));
            ValidationException one = await RefusedAsync(IdsOnly(), New(-1, "A", categoryId: 3));
            ValidationException several = await RefusedAsync(IdsOnly(), New(-1, "A", categoryId: 3), New(-2, "B", categoryId: 4));

            Assert.Equal(["[0].CategoryId Fk.NotFound"], Lines(one));
            Assert.Equal((PipelineSaveModel.Categories, 3L), (one.Errors[0].Arguments["entity"], one.Errors[0].Arguments["id"]));
            Assert.Equal([$" {ValidationCodes.FkNotFound}"], Lines(several));
            Assert.Equal((PipelineSaveModel.Categories, "CategoryId"), (several.Errors[0].Arguments["entity"], several.Errors[0].Arguments["property"]));
        }

        [Fact]
        public async Task An_arithmetic_failure_of_a_save_carrying_no_query_text_passes_through()
        {
            Warm();
            ArithmeticFailureException failure = new("divide-by-zero", statementOrdinal: 1);
            FailPersist(failure);

            Assert.Same(failure, await Assert.ThrowsAsync<ArithmeticFailureException>(() => SaveAsync(IdsOnly(), New(-1, "A"))));
        }

        [Fact]
        public async Task A_commit_the_probe_proved_is_read_back_plainly_then_runs_its_post_commit_phase_and_any_other_ambiguous_end_is_the_database_unavailable()
        {
            Warm();
            Grant(categories: null, read: "Name != 'hidden'");
            _readBack.Add(new PsDocument { Id = 101, Name = "A", Title = "T", ModifiedAt = Committed });
            DateTimeOffset? stamp = null;
            _persistence.AfterCommit = (participant, outcome) =>
            {
                stamp = participant == PsPersistence.Service ? outcome.Stamp : stamp;
                return Task.CompletedTask;
            };
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Committed));

            EntitiesResult<PsDocument> saved = await SaveAsync(New(-1, "A"));

            // --- A plain read over the final ids, whatever the caller's read filter admits, the stamp theirs ---
            Assert.Equal([BatchPurpose.Persist, BatchPurpose.Read], _rig.Script.Batches.Select(static batch => batch.Purpose));
            ScriptedBatch read = _rig.Script.Batches[1];
            ScriptedStatement ids = Assert.Single(read.Of(ScriptedKind.Tvp));
            Assert.All(read.Of(ScriptedKind.Query), query => Assert.Equal(ids.Name, Assert.Single(query.Query<PsDocument>().Restrictions).TableSource));
            Assert.DoesNotContain(read.Of(ScriptedKind.Rows), static rows => rows.Options is RowQueryOptions { CaptureKeys: true });
            Assert.Equal(101, Assert.Single(saved.Entities).Id);
            Assert.Equal(Committed, stamp);

            // --- An update the probe cannot prove: the caller reloads to learn the outcome ---
            Store(5);
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Unknown));
            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(() => SaveAsync(IdsOnly(), Update(5, "B")));
            Assert.Equal(("database", TimeSpan.FromSeconds(1)), (unavailable.Dependency, unavailable.RetryAfter));
            _ = Assert.IsType<DataAccessAmbiguousException>(unavailable.InnerException);
        }

        // --- The post-commit phase ---

        [Fact]
        public async Task A_post_commit_failure_is_logged_and_counted_by_its_component_and_never_changes_the_response()
        {
            Warm();
            List<string> after = [];
            InvalidOperationException thrown = new("The effect failed.");
            _persistence.AfterCommit = (participant, _) =>
            {
                after.Add(participant);
                return participant == PsPersistence.First ? Task.FromException(thrown) : Task.CompletedTask;
            };
            using MetricCollector<long> failures = Collect(CrudTelemetryNames.EffectFailures);

            EntitiesResult<PsDocument> saved = await SaveAsync(IdsOnly(), New(-1, "A"));

            Assert.Equal([101L], saved.Ids);
            Assert.Equal([PsPersistence.Service, PsPersistence.First, PsPersistence.Second], after);
            Assert.Equal(nameof(PsFirstEffect), Assert.Single(failures.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.EffectTag]);
            FakeLogRecord logged = Assert.Single(_rig.Services.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Name == "PostCommitFailed");
            Assert.Equal(LogLevel.Error, logged.Level);
            Assert.Same(thrown, logged.Exception);
        }

        [Fact]
        public async Task A_commit_callback_a_participant_registered_that_fails_is_contained_and_counted_by_the_component_that_wrote_it()
        {
            Warm();
            bool ran = false;
            _persistence.Contribute = (participant, context) =>
            {
                if (participant == PsPersistence.Second)
                {
                    context.Batch.OnCommitted(static _ => throw new InvalidOperationException("The callback failed."));
                    context.Batch.OnCommitted(_ => ran = true);
                }

                return Task.CompletedTask;
            };
            using MetricCollector<long> failures = Collect(CrudTelemetryNames.EffectFailures);

            EntitiesResult<PsDocument> saved = await SaveAsync(IdsOnly(), New(-1, "A"));

            Assert.Equal([101L], saved.Ids);
            Assert.True(ran);
            Assert.Equal(nameof(SaveOperationTests), Assert.Single(failures.GetMeasurementSnapshot()).Tags[CrudTelemetryNames.EffectTag]);
        }

        [Fact]
        public async Task A_committed_save_records_its_rows_at_every_depth_by_its_source_and_lane()
        {
            Warm();
            using MetricCollector<long> entities = Collect(CrudTelemetryNames.SaveEntities);
            PsDocument document = New(-1, "A");
            document.Lines = [new PsLine { SubLines = [new PsSubLine()] }, new PsLine()];

            _ = await SaveAsync(IdsOnly(), document);

            CollectedMeasurement<long> recorded = Assert.Single(entities.GetMeasurementSnapshot());
            Assert.Equal(4L, recorded.Value);
            Assert.Equal(
                (CrudMetrics.Source(SaveSource.Web), CrudMetrics.SmallLane),
                ((string)recorded.Tags[CrudTelemetryNames.SourceTag]!, (string)recorded.Tags[CrudTelemetryNames.LaneTag]!));
        }

        [Fact]
        public async Task A_proven_commit_returning_ids_alone_reads_only_the_roots_stamp_over_the_final_ids_and_runs_its_post_commit_phase()
        {
            Warm();
            _readBack.Add(new PsDocument { Id = 101, Name = "A", Title = "T", ModifiedAt = Committed });
            List<string> after = [];
            _persistence.AfterCommit = (participant, outcome) =>
            {
                after.Add($"{participant} {outcome.Stamp:O}");
                return Task.CompletedTask;
            };
            FailPersist(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Committed));

            EntitiesResult<PsDocument> saved = await SaveAsync(IdsOnly(), New(-1, "A"));

            // --- The ids, no details read, and the stamp read over the final ids ---
            Assert.Equal([101L], saved.Ids);
            Assert.Empty(saved.Entities);
            ScriptedBatch read = _rig.Script.Batches[^1];
            Assert.Equal((BatchPurpose.Read, ScriptedKind.Query), (read.Purpose, Assert.Single(read.Statements, static statement => statement.Kind != ScriptedKind.Tvp).Kind));
            Assert.Equal([.. new[] { PsPersistence.Service, PsPersistence.First, PsPersistence.Second }.Select(static participant => $"{participant} {Committed:O}")], after);
        }

        [Fact]
        public async Task A_proven_commit_whose_read_fails_still_runs_its_post_commit_phase_before_the_failure_surfaces()
        {
            Warm();
            List<string> after = [];
            _persistence.AfterCommit = (participant, outcome) =>
            {
                after.Add(participant);
                Assert.Equal(default, outcome.Stamp);
                return Task.CompletedTask;
            };
            TimeoutException dropped = new("The read's connection dropped too.");
            _ = _rig.Script.OnExecute((batch, _) => batch.Purpose == BatchPurpose.Persist
                ? Task.FromException(new DataAccessAmbiguousException(new TimeoutException("The connection dropped."), CommitVerdict.Committed))
                : batch.Purpose == BatchPurpose.Read ? Task.FromException(dropped) : Task.CompletedTask);

            Assert.Same(dropped, await Assert.ThrowsAsync<TimeoutException>(() => SaveAsync(New(-1, "A"))));

            Assert.Equal([PsPersistence.Service, PsPersistence.First, PsPersistence.Second], after);
        }

        [Fact]
        public async Task A_caller_granted_only_a_filtered_save_reads_back_the_rows_it_saved_as_the_save_implies_reading_them()
        {
            Warm();
            _rig.Grant([.. Grants(categories: null, save: "Name != 'locked'").Where(static grant => !(grant.Resource == PipelineSaveModel.Documents && grant.Action == AccessActions.Read))]);
            _readBack.Add(new PsDocument { Id = 101, Name = "A", Title = "T", ModifiedAt = Committed });

            EntitiesResult<PsDocument> saved = await SaveAsync(New(-1, "A"));

            // --- The post-check and the read-back ride the one persist ---
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches);
            _ = Assert.Single(persist.Of(ScriptedKind.Assert));
            _ = Assert.Single(persist.Of(ScriptedKind.Query));
            Assert.Equal(101, Assert.Single(saved.Entities).Id);
        }

        [Fact]
        public async Task The_post_commit_phase_runs_before_the_response_is_assembled_so_an_extra_failing_after_the_commit_skips_none_of_it()
        {
            Warm();
            List<string> after = [];
            _persistence.AfterCommit = (participant, _) =>
            {
                after.Add(participant);
                return Task.CompletedTask;
            };
            SaveOptions options = new() { Details = new DetailsRequest { Include = [PsDetails.Failing] } };

            InvalidOperationException failed = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(options, New(-1, "A")));

            Assert.Equal("The extra's assembly failed.", failed.Message);
            Assert.True(Assert.Single(_rig.Script.Batches).IsExecuted);
            Assert.Equal([PsPersistence.Service, PsPersistence.First, PsPersistence.Second], after);
        }

        [Fact]
        public async Task A_recomposed_save_asking_for_its_rows_composes_its_read_back_again_and_calls_no_persist_participant_again()
        {
            Warm();
            _readBack.Add(new PsDocument { Id = 101, Name = "A", Title = "T", ModifiedAt = Committed });
            Dictionary<string, int> contributed = new(StringComparer.Ordinal);
            _persistence.Contribute = (participant, _) =>
            {
                contributed[participant] = contributed.GetValueOrDefault(participant) + 1;
                if (contributed.Count == 1)
                {
                    _rig.GrantOnRecompose(Grants(categories: null));
                }

                return Task.CompletedTask;
            };

            EntitiesResult<PsDocument> saved = await SaveAsync(New(-1, "A"));

            // --- The read-back is the pipeline's own statement, rebuilt under the fresh grants ---
            Assert.Equal(101, Assert.Single(saved.Entities).Id);
            (ScriptedBatch refused, ScriptedBatch committed) = (_rig.Script.Batches[0], _rig.Script.Batches[1]);
            Assert.True(refused.IsRefused);
            _ = Assert.Single(refused.Of(ScriptedKind.Query));
            _ = Assert.Single(committed.Of(ScriptedKind.Query));
            Assert.Equal([1, 1, 1], [contributed[PsPersistence.Service], contributed[PsPersistence.First], contributed[PsPersistence.Second]]);
        }

        // --- The access guards ---

        [Fact]
        public async Task A_stack_writing_a_security_table_takes_the_guards_lock_before_its_emitter_and_their_invariants_after_every_contribution()
        {
            WarmRoles();
            _rig.Grant([.. Grants(categories: null), (CoreResources.Role, AccessActions.Read, null), (CoreResources.Role, AccessActions.Save, "IsPublic = false")]);
            SaveOptions options = new() { OnPersist = static batch => _ = batch.Sql($"SELECT {"caller"};", SqlOptions.None) };

            _ = await SaveRolesAsync(options, new Role { Name = "Clerks" });

            // --- The lock, the emitter, the caller's statement, the invariants, the post-check, the read-back ---
            ScriptedBatch persist = Assert.Single(_rig.Script.Batches, static batch => batch.Purpose == BatchPurpose.Persist);
            int Text(string text)
            {
                return Assert.Single(persist.Of(ScriptedKind.Sql), statement => (string)statement.Text.GetArgument(0)! == text).Ordinal;
            }

            int[] order =
            [
                Text(PsGuards.Lock),
                Assert.Single(persist.Of(ScriptedKind.Save)).Ordinal,
                Text("caller"),
                Text(PsGuards.Invariants),
                Assert.Single(persist.Of(ScriptedKind.Assert)).Ordinal,
                Assert.Single(persist.Of(ScriptedKind.Query)).Ordinal,
            ];
            Assert.Equal([.. order.Order()], order);
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants], _guards.Calls);
        }

        [Fact]
        public async Task The_guards_join_every_composition_of_a_recomposed_persist_and_none_of_a_system_scope_or_of_a_stack_writing_no_security_table()
        {
            WarmRoles();
            (string, string, string?)[] grants = [.. Grants(categories: null), (CoreResources.Role, AccessActions.Read, null), (CoreResources.Role, AccessActions.Save, null)];
            _rig.Grant(grants);
            int onPersist = 0;
            SaveOptions options = IdsOnly();
            options.OnPersist = _ =>
            {
                if (++onPersist == 1)
                {
                    _rig.GrantOnRecompose(grants);
                }
            };

            // --- A recomposed persist: the guards are the pipeline's own statements, composed again ---
            _ = await SaveRolesAsync(options, new Role { Name = "Clerks" });
            Assert.Equal((1, 2), (onPersist, _rig.Script.Batches.Count(static batch => batch.Purpose == BatchPurpose.Persist)));
            Assert.Equal([PsGuards.Lock, PsGuards.Invariants, PsGuards.Lock, PsGuards.Invariants], _guards.Calls);

            // --- A system scope's persist, and a stack whose tables hold no security table, call neither ---
            _guards.Calls.Clear();
            _rig.Kind = PrincipalKind.System;
            _ = await SaveRolesAsync(IdsOnly(), new Role { Name = "Auditors" });
            _rig.Kind = PrincipalKind.User;
            Warm();
            _ = await SaveAsync(IdsOnly(), New(-1, "A"));
            Assert.Empty(_guards.Calls);
        }

        [Fact]
        public void A_stack_writes_a_security_table_when_its_root_or_a_collection_at_any_depth_maps_to_one()
        {
            HashSet<TableName> documents = StackTableSet.Of(Metadata<PsDocument>());
            HashSet<TableName> users = StackTableSet.Of(Metadata<User>());

            Assert.Equal(
                [new TableName("ps", "Documents"), new TableName("ps", "Lines"), new TableName("ps", "SubLines")],
                documents.OrderBy(static table => table.Name, StringComparer.Ordinal));
            Assert.False(StackTableSet.WritesSecurityTable(documents));
            Assert.Contains(new TableName("core", "RoleMemberships"), users);
            Assert.True(StackTableSet.WritesSecurityTable([new TableName("ps", "Documents"), new TableName("CORE", "rolememberships")]));
        }

        // --- The entity tags the validation rounds were served under ---

        [Fact]
        public async Task The_entity_tags_a_validation_round_was_served_under_ride_the_persists_guard_and_a_re_entry_finding_one_moved_is_stale()
        {
            Store(5);
            VersionTagDependency categories = new(
                $"{VersionTagNames.EntityPrefix}{PipelineSaveModel.Categories}", new VersionTag(Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff")), VersionTagMismatchPolicy.Refresh);
            _rig.Script.Declares = purpose => purpose == BatchPurpose.Validate ? [categories] : [];

            _ = await SaveAsync(IdsOnly(), Update(5, "A"));

            // --- The persist re-declares the round's tag, to be re-run on a mismatch ---
            ScriptedBatch persist = _rig.Script.Batches[^1];
            Assert.Equal(BatchPurpose.Persist, persist.Purpose);
            Assert.Equal(categories with { OnMismatch = VersionTagMismatchPolicy.Rerun }, Assert.Single(persist.Dependencies));

            // --- A re-entry whose fresh snapshot moved past it: stale, no participant called again ---
            int contributed = 0;
            _persistence.Contribute = (_, _) =>
            {
                if (++contributed == 1)
                {
                    _rig.GrantOnRecompose(Grants(categories: null));
                    _rig.TagOnRecompose(categories.Name, Guid.NewGuid());
                }

                return Task.CompletedTask;
            };

            StaleContextException stale = await Assert.ThrowsAsync<StaleContextException>(() => SaveAsync(IdsOnly(), Update(5, "B")));

            Assert.Equal(categories.Name, Assert.Single(stale.Dependencies).Name);
            Assert.Equal(3, contributed);
            Assert.False(_rig.Script.Batches[^1].IsExecuted);
        }

        // --- A delete of the child synchronisation still referenced ---

        [Fact]
        public async Task A_dropped_child_another_row_still_references_is_in_use_at_the_collection_that_dropped_it()
        {
            Store(5, lines: [Line(11, documentId: 5), Line(12, documentId: 5)]);
            Store(6, lines: [Line(13, documentId: 6)]);
            FailPersist(new ForeignKeyViolationException("FK_Documents_LineRefId", statementOrdinal: 1));
            PsDocument dropping = Update(5, "A");
            dropping.Lines = [];

            // --- One collection dropped rows of the referenced entity: the error sits on it ---
            ValidationException one = await RefusedAsync(IdsOnly(), dropping);
            Assert.Equal([$"[0].Lines {ValidationCodes.FkInUse}"], Lines(one));
            Assert.Equal((PipelineSaveModel.Documents, nameof(PsDocument.LineRefId)), (one.Errors[0].Arguments["entity"], one.Errors[0].Arguments["property"]));

            // --- Two did: at the root; a constraint no model reference names passes through ---
            PsDocument other = Update(6, "B");
            other.Lines = [];
            dropping.Lines = [];
            ValidationException several = await RefusedAsync(IdsOnly(), dropping, other);
            Assert.Equal([$" {ValidationCodes.FkInUse}"], Lines(several));
            ForeignKeyViolationException unknown = new("FK_Elsewhere_ThingId", statementOrdinal: 1);
            FailPersist(unknown);
            dropping.Lines = [];
            Assert.Same(unknown, await Assert.ThrowsAsync<ForeignKeyViolationException>(() => SaveAsync(IdsOnly(), dropping)));
        }

        // --- Persist helpers ---

        /// <summary>Fills the roles' id buffer and answers every role query with none, so a new role validates and persists.</summary>
        private void WarmRoles()
        {
            _ids.Allocator.Replenish(Tenant, typeof(Role), 501, 599);
            _ = _rig.Script.OnQuery<Role>(static _ => []).OnSave<Role>(Attempted);
        }

        /// <summary>Saves roles through the core roles' stack.</summary>
        /// <param name="options">The options.</param>
        /// <param name="roles">The roles.</param>
        /// <returns>The save's result.</returns>
        private async Task<EntitiesResult<Role>> SaveRolesAsync(SaveOptions options, params Role[] roles)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            return await scope.ServiceProvider.GetRequiredService<EntityService<Role, int>>().SaveAsync(roles, options, Token);
        }

        /// <summary>Makes every persist batch's execution fail, as the executor raises a failure of its statements.</summary>
        /// <param name="failure">The failure.</param>
        private void FailPersist(Exception failure)
        {
            _ = _rig.Script.OnExecute((batch, _) => batch.Purpose == BatchPurpose.Persist ? Task.FromException(failure) : Task.CompletedTask);
        }

        /// <summary>Makes the next persist batch's guard find the permissions tag moved, once its first participant contributed.</summary>
        /// <param name="grants">The grants the fresh set holds.</param>
        private void RecomposeOnFirstContribution((string Resource, string Action, string? Filter)[] grants)
        {
            bool armed = false;
            _persistence.Contribute = (_, _) =>
            {
                if (!armed)
                {
                    armed = true;
                    _rig.GrantOnRecompose(grants);
                }

                return Task.CompletedTask;
            };
        }

        /// <summary>Saves nodes whose persist fails validation.</summary>
        /// <param name="nodes">The nodes.</param>
        /// <returns>The refusal.</returns>
        private async Task<ValidationException> SaveNodesRefusedAsync(params PsNode[] nodes)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            EntityService<PsNode, int> service = scope.ServiceProvider.GetRequiredService<EntityService<PsNode, int>>();
            return await Assert.ThrowsAsync<ValidationException>(() => service.SaveAsync(nodes, IdsOnly(), Token));
        }

        /// <summary>Collects the measurements of one of the pipeline's instruments.</summary>
        /// <param name="instrument">The instrument's name.</param>
        /// <returns>The collector.</returns>
        private MetricCollector<long> Collect(string instrument)
        {
            return new MetricCollector<long>(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, instrument);
        }

        /// <summary>An entity's metadata in the rig's model.</summary>
        /// <typeparam name="T">The entity class.</typeparam>
        /// <returns>The metadata.</returns>
        private EntityMetadata Metadata<T>()
        {
            return _rig.Services.GetRequiredService<IEntityMetadataProvider>().Get(typeof(T));
        }

        /// <summary>A conflict row the emitter reports for a row it found missing.</summary>
        /// <param name="id">The row's id.</param>
        /// <returns>The row.</returns>
        private static ConflictRow Missing(long id)
        {
            return new ConflictRow(id, IsMissing: true, ModifiedAt: null, ModifiedById: null, Name: null, Name2: null, Name3: null);
        }

        /// <summary>A batch's statements, each as its kind and its text or name, in order.</summary>
        /// <param name="batch">The batch.</param>
        /// <returns>The statements.</returns>
        private static string[] Describe(ScriptedBatch batch)
        {
            return [.. batch.Statements.Select(static statement => $"{statement.Kind} {(statement.Kind == ScriptedKind.Sql ? FormattableString.Invariant(statement.Text) : statement.Name)}")];
        }
    }
}
