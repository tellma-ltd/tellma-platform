// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Collections;
using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Ids;
using Tellma.Core.Queryex;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Scripted;
using Tellma.Core.Tests.Crud.Validation;

namespace Tellma.Core.Tests.Crud.Save
{
    /// <summary>
    ///     The save pipeline over the scripted batch: the shape and the grant before any round trip,
    ///     preprocessing, the ids at once or riding round trip 1, the before images and the save
    ///     grant's count, existence and early concurrency, ownership and the active flag, child
    ///     synchronisation, references and visible loads with their step-up and their owners'
    ///     filters, a round recomposed for a moved permissions tag, uniqueness in the persist's
    ///     statement order, cycles, value sets, the validators' rounds and their warnings; then the
    ///     persist, its re-entry, the translation of its failures and the post-commit phase.
    /// </summary>
    public sealed partial class SaveOperationTests : IAsyncDisposable
    {
        /// <summary>The fixture tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The user who last modified every stored document.</summary>
        private const int Modifier = 7;

        /// <summary>The stamp every stored document carries.</summary>
        private static readonly DateTimeOffset Stamp = new(2026, 11, 16, 8, 30, 0, 123, TimeSpan.FromHours(3));

        /// <summary>The stamp the persist's save statement reports.</summary>
        private static readonly DateTimeOffset Committed = new(2026, 11, 17, 9, 0, 0, 456, TimeSpan.Zero);

        /// <summary>The documents' validator's body, shared with the rig's scopes.</summary>
        private readonly PsValidation _validation = new();

        /// <summary>The documents' persist participants' body, shared with the rig's scopes.</summary>
        private readonly PsPersistence _persistence = new();

        /// <summary>The access guards, which record where each persist called them.</summary>
        private readonly PsGuards _guards = new();

        /// <summary>The documents' bespoke criteria, read at every decision; none by default.</summary>
        private readonly DocumentCriteria _criteria = new();

        /// <summary>The rows a read-back finds, whatever ids it reads.</summary>
        private readonly List<PsDocument> _readBack = [];

        /// <summary>The stored documents the before-image query finds, each built afresh per query, by id.</summary>
        private readonly Dictionary<int, Func<PsDocument>> _stored = [];

        /// <summary>The stored documents holding a code, which the uniqueness load finds.</summary>
        private readonly List<PsDocument> _codes = [];

        /// <summary>The stored documents holding a serial, which the uniqueness load finds.</summary>
        private readonly List<PsDocument> _serials = [];

        /// <summary>The stored lines holding a line code, which the uniqueness load finds.</summary>
        private readonly List<PsLine> _lineCodes = [];

        /// <summary>The stored lines holding a barcode, which the uniqueness load finds.</summary>
        private readonly List<PsLine> _barcodes = [];

        /// <summary>The ids each referenced entity's read finds, by entity name.</summary>
        private readonly Dictionary<string, int[]> _visible = new(StringComparer.Ordinal);

        /// <summary>The composed pipeline-save model.</summary>
        private readonly QueryRig _rig;

        /// <summary>The ids the save reserves.</summary>
        private readonly TestIds _ids;

        /// <summary>The count the save grant's filter admits; none scripted when null.</summary>
        private int? _saveCount;

        /// <summary>The documents' preprocessing hook for the next save; nothing when null.</summary>
        private Func<SaveContext<PsDocument>, Task>? _preprocess;

        /// <summary>The warnings the next save's caller confirmed.</summary>
        private string[] _confirmed = [];

        /// <summary>Composes the model with the test's ids, grants the documents, nodes and categories, and scripts every read.</summary>
        public SaveOperationTests()
        {
            _rig = new QueryRig(PipelineSaveModel.Compose, services =>
            {
                _ = services.AddSingleton(_validation);
                _ = services.AddSingleton(_persistence);
                _ = services.Replace(ServiceDescriptor.Singleton<IAccessGuards>(_guards));
                _ = services.AddSingleton<IAccessCriteriaProvider>(_criteria);
                _ = services.AddFakeLogging();
                _ = services.AddSingleton(static provider => new TestIds(provider.GetRequiredService<IdAllocator>()));
                _ = services.Replace(ServiceDescriptor.Singleton<IIdAllocator>(static provider => provider.GetRequiredService<TestIds>()));
                _ = services.Replace(ServiceDescriptor.Singleton<IIdBuffer>(static provider => provider.GetRequiredService<TestIds>()));
            });
            _ids = _rig.Services.GetRequiredService<TestIds>();
            Grant(categories: null);

            // --- Every read the save may make: the before images, the codes and the serials, the line
            // codes and barcodes, the referenced ids, the tree, the modifiers' names and the save
            // grant's count; the validators' visible loads of categories and sub-lines; the read-back
            // beside the persist or after it; and every persist's save and raw statements ---
            _ = _rig.Script
                .OnQuery(statement => statement.Batch.Purpose != BatchPurpose.Validate ? _readBack : Restriction(statement.Query<PsDocument>().Restrictions) switch
                {
                    "Id" => [.. statement.KeySet<IdList>().Where(row => _stored.ContainsKey(row.Id)).Select(row => _stored[row.Id]())],
                    "Serial" => _serials,
                    _ => _codes,
                })
                .OnQuery(statement => Restriction(statement.Query<PsLine>().Restrictions) == nameof(PsLine.Barcode) ? _barcodes : _lineCodes)
                .OnQuery<PsCategory>(static statement => statement.Query<PsCategory>().Restrictions is []
                    ? [new PsCategory { Id = 3, Name = "C" }]
                    : [.. statement.KeySet<IdList>().Select(static row => new PsCategory { Id = row.Id, Name = "C" })])
                .OnQuery<PsSubLine>(static _ => [new PsSubLine { Id = 31, LineId = 12, Note = "N" }])
                .OnRows(Rows)
                .OnCount(_ => _saveCount ?? throw new InvalidOperationException("The test scripts no save count."))
                .OnSave<PsDocument>(Attempted)
                .OnSave<PsNode>(Attempted)
                .OnSql(static _ => RawResult.Empty);
        }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The batches of the validation rounds, in order: every batch but the persists and their read-backs.</summary>
        private ScriptedBatch[] ValidationBatches => [.. _rig.Script.Batches.Where(static batch => batch.Purpose == BatchPurpose.Validate)];

        /// <summary>The round trips the validation rounds made.</summary>
        private int ValidationTrips => ValidationBatches.Count(static batch => batch.IsExecuted);

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- Ids and rounds ---

        [Fact]
        public async Task A_create_on_a_warm_buffer_validates_with_its_final_ids_before_any_round_trip_and_persists_on_its_one()
        {
            Warm();
            (long Id, int Round)? seen = null;
            _validation.Validate = context =>
            {
                seen = (context.Entities[0].Id, context.Round);
                return Task.CompletedTask;
            };

            EntitiesResult<PsDocument> saved = await SaveAsync(IdsOnly(), New(-1, "A"));

            Assert.Equal<(long, int)?>((101L, 1), seen);
            Assert.Equal([101L], saved.Ids);
            Assert.Empty(saved.Entities);
            Assert.Empty(_ids.Reserved);

            // --- One round trip, the persist's: the id it committed is spent ---
            Assert.Equal((0, 1), (ValidationTrips, _rig.Script.RoundTrips));
            Assert.Equal(BatchPurpose.Persist, Assert.Single(_rig.Script.Batches).Purpose);
            Assert.Equal(98, _ids.Allocator.Buffered(Tenant, typeof(PsDocument)));
        }

        [Fact]
        public async Task A_cold_buffer_reserves_on_round_trip_1_alone_and_the_validators_start_after_it_with_the_final_ids()
        {
            Warm();
            _ids.Cold = true;
            _visible[PipelineSaveModel.Categories] = [3];
            (long Id, int Round)? started = null;
            int? resumed = null;
            _validation.Validate = async context =>
            {
                started = (context.Entities[0].Id, context.Round);
                _ = context.Loader.ByIds<PsCategory, int>([3]);
                await context.Loader.LoadAsync();
                resumed = context.Round;
            };

            await ValidatedAsync(New(-1, "A", categoryId: 3));

            // --- Round trip 1: the reservation and the reference, no validator's load; round trip 2: the validator's ---
            Assert.Equal(2, ValidationTrips);
            ScriptedBatch first = ValidationBatches[0];
            Assert.Same(first, Assert.Single(_ids.Reserved));
            Assert.Empty(first.Of(ScriptedKind.Query));
            Assert.Equal(PipelineSaveModel.Categories, Assert.Single(first.Of(ScriptedKind.Rows)).Spec.Root.Name);
            _ = Assert.Single(ValidationBatches[1].Of(ScriptedKind.Query));
            Assert.Equal<(long, int)?>((101L, 1), started);
            Assert.Equal(2, resumed);
        }

        [Fact]
        public async Task On_a_warm_buffer_the_validators_loads_ride_round_trip_1_beside_the_pipelines()
        {
            Warm();
            _visible[PipelineSaveModel.Categories] = [3];
            (long Id, int Round)? started = null;
            int? resumed = null;
            _validation.Validate = async context =>
            {
                started = (context.Entities[0].Id, context.Round);
                _ = context.Loader.ByIds<PsCategory, int>([3]);
                await context.Loader.LoadAsync();
                resumed = context.Round;
            };

            await ValidatedAsync(New(-1, "A", categoryId: 3));

            ScriptedBatch batch = Assert.Single(ValidationBatches);
            Assert.Equal((BatchPurpose.Validate, 1), (batch.Purpose, ValidationTrips));
            Assert.Same(QueryexProfiles.PipelineLimits, batch.Limits);
            _ = Assert.Single(batch.Of(ScriptedKind.Rows));
            _ = Assert.Single(batch.Of(ScriptedKind.Query));
            Assert.Empty(_ids.Reserved);
            Assert.Equal<(long, int)?>((101L, 1), started);
            Assert.Equal(2, resumed);
        }

        // --- A round recomposed for a moved permissions tag ---

        [Fact]
        public async Task A_read_granted_since_the_scope_cached_its_caller_admits_the_reference_once_the_round_is_recomposed()
        {
            Warm();
            _visible[PipelineSaveModel.Categories] = [3];
            _rig.Grant(WithoutCategories());
            _rig.GrantOnRecompose(Grants(categories: null));

            await ValidatedAsync(New(-1, "A", categoryId: 3));

            // The stale composition read nothing of the denied target; the fresh one reads it.
            Assert.Equal(2, ValidationTrips);
            Assert.Empty(ValidationBatches[0].Of(ScriptedKind.Rows));
            Assert.Equal(PipelineSaveModel.Categories, Assert.Single(ValidationBatches[1].Of(ScriptedKind.Rows)).Spec.Root.Name);
        }

        [Fact]
        public async Task A_read_revoked_since_the_scope_cached_its_caller_refuses_the_reference_once_the_round_is_recomposed()
        {
            Warm();
            _visible[PipelineSaveModel.Categories] = [3];
            _rig.GrantOnRecompose(WithoutCategories());

            ValidationException refused = await RefusedAsync(New(-1, "A", categoryId: 3));

            Assert.Equal(["[0].CategoryId Fk.NotFound"], Lines(refused));
            Assert.Equal(2, ValidationTrips);
            _ = Assert.Single(ValidationBatches[0].Of(ScriptedKind.Rows));
            Assert.Empty(ValidationBatches[1].Of(ScriptedKind.Rows));
        }

        [Fact]
        public async Task A_recomposed_cold_round_gives_back_the_first_reservation_and_validates_with_the_ids_of_the_second()
        {
            Warm();
            _ids.Cold = true;
            _rig.GrantOnRecompose(Grants(categories: null));
            long? seen = null;
            _validation.Validate = context =>
            {
                seen = context.Entities[0].Id;
                return Task.CompletedTask;
            };

            await ValidatedAsync(New(-1, "A"));

            // --- Each composition reserved on its own batch; the first gave its id back before the
            // second reserved, so the second received it, the persist spent it, and nothing leaked ---
            Assert.Equal(2, ValidationTrips);
            Assert.Equal(2, _ids.Reserved.Count);
            Assert.Same(ValidationBatches[0], _ids.Reserved[0]);
            Assert.Same(ValidationBatches[1], _ids.Reserved[1]);
            Assert.Equal(101L, seen);
            Assert.Equal(98, _ids.Allocator.Buffered(Tenant, typeof(PsDocument)));
        }

        // --- Shape, grant and preprocessing ---

        [Fact]
        public async Task A_payload_of_the_wrong_shape_or_without_the_save_grant_is_refused_before_any_round_trip()
        {
            bool validated = false;
            _validation.Validate = _ =>
            {
                validated = true;
                return Task.CompletedTask;
            };

            ValidationException shape = await RefusedAsync(Update(5, "A"), Update(5, "B"));
            Assert.Equal(["[1].Id Entity.DuplicateId"], Lines(shape));

            // --- A collection beyond its ceiling two levels down ---
            PsDocument crowded = New(-1, "A");
            crowded.Lines = [new PsLine(), new PsLine { SubLines = [new PsSubLine(), new PsSubLine(), new PsSubLine(), new PsSubLine(), new PsSubLine()] }];
            LimitExceededException children = await Assert.ThrowsAsync<LimitExceededException>(() => SaveAsync(crowded));
            Assert.Equal((LimitNames.MaxChildren, 5, 4), (children.Limit, children.Actual, children.Maximum));

            _rig.Grant((PipelineSaveModel.Documents, AccessActions.Read, null));
            await using (AsyncServiceScope scope = _rig.Scope())
            {
                ForbiddenException denied = await Assert.ThrowsAsync<ForbiddenException>(() => Documents(scope).SaveAsync([New(-1, "A")], new SaveOptions(), Token));
                Assert.Equal((PipelineSaveModel.Documents, AccessActions.Save), (denied.Resource, denied.Action));
            }

            Assert.False(validated);
            Assert.Equal(0, ValidationTrips);
        }

        [Fact]
        public async Task Preprocessing_trims_resets_what_the_server_owns_drops_unconfigured_twins_and_spells_values_as_their_set_does()
        {
            Warm();
            PsDocument document = New(-1, "  Ann  ");
            (document.Memo, document.Raw, document.Color, document.Name2, document.Name3) = ("  m  ", "  r  ", "blue", "Anne", "Anna");
            (document.Total, document.Audit, document.Title, document.Prefix, document.Handler) = (5m, "client", "  ", "  ", " h ");
            object?[]? seen = null;
            _preprocess = context =>
            {
                PsDocument row = context.Entities[0];
                seen = [row.Name, row.Memo, row.Raw, row.Color, row.Name2, row.Name3, row.Total, row.Audit, row.Title, row.Prefix, row.Handler];
                row.Title = "Filled";
                return Task.CompletedTask;
            };

            await ValidatedAsync(document);

            // A required string stays empty for the hook to fill; a nullable one becomes null.
            object?[] expected = ["Ann", "m", "  r  ", "Blue", null, null, null, null, "", null, "h"];
            Assert.Equal(expected, seen);
        }

        [Fact]
        public async Task An_import_refuses_a_language_the_tenant_lacks_and_a_defaulted_value_the_hook_left_empty_is_required()
        {
            Warm();
            PsDocument document = New(-1, "A");
            (document.Name2, document.Name3, document.Title) = ("B", "", "");

            ValidationException refused = await RefusedAsync(new SaveOptions { Source = SaveSource.Import }, document);

            Assert.Equal(["[0].Name2 Import.LanguageNotConfigured", "[0].Title Required"], Lines(refused));
            Assert.Equal("Title", Assert.Single(refused.Errors, static error => error.Code == ValidationCodes.Required).Arguments["property"]);
            Assert.Null(document.Name3);
        }

        [Fact]
        public async Task Nesting_sets_the_parent_keys_and_temporary_ids_become_the_final_ones_wherever_referenced()
        {
            Warm();
            PsSubLine sub = new() { Id = -1, LineId = 88, Note = "n" };
            PsLine line = new() { Id = -1, DocumentId = 77, SubLines = [sub] };
            PsLine other = new() { Id = 0 };
            PsDocument first = New(-1, "A");
            first.Lines = [line];
            PsDocument second = New(-2, "B");
            (second.RelatedId, second.Lines) = (-1, [other]);
            (int DocumentId, int LineId)? preprocessed = null;
            _preprocess = _ =>
            {
                preprocessed = (line.DocumentId, sub.LineId);
                return Task.CompletedTask;
            };

            await ValidatedAsync(first, second);

            Assert.Equal<(int, int)?>((-1, -1), preprocessed);
            Assert.Equal((101, 102, 102), (first.Id, second.Id, second.Lines[0].DocumentId));
            Assert.Equal((201, 101, 202), (line.Id, line.DocumentId, other.Id));
            Assert.Equal((301, 201), (sub.Id, sub.LineId));
            Assert.Equal(101, second.RelatedId);
            Assert.Equal(0, ValidationTrips);
        }

        [Fact]
        public async Task A_temporary_id_no_row_of_the_payload_defines_is_not_found_at_the_reference()
        {
            Warm();
            PsDocument document = New(-1, "A");
            document.RelatedId = -9;

            ValidationException refused = await RefusedAsync(document);

            Assert.Equal(["[0].RelatedId Entity.NotFound"], Lines(refused));
            Assert.Equal(99, _ids.Allocator.Buffered(Tenant, typeof(PsDocument)));
        }

        // --- The before images, existence and the save grant ---

        [Fact]
        public async Task The_before_images_load_under_the_read_filter_with_the_collections_the_payload_carries_and_the_save_grant_counts_them()
        {
            Grant(categories: null, read: "Code != 'hidden'", save: "Name != 'locked'");
            Store(5, lines: [Line(11, subLines: [21]), Line(12)]);
            Store(6, lines: [Line(13)]);
            _saveCount = 2;
            PsDocument withLines = Update(5, "A");
            withLines.Lines = [new PsLine { Id = 11, SubLines = [] }];
            PsDocument without = Update(6, "B");

            await ValidatedAsync(withLines, without);

            // --- One round trip: the images with the present collections, ordered by key, and the count ---
            ScriptedBatch batch = Assert.Single(ValidationBatches);
            ScriptedStatement images = Assert.Single(batch.Of(ScriptedKind.Query));
            EntityQuery<PsDocument> query = images.Query<PsDocument>();
            Assert.True(query.AllProperties);
            Assert.Equal(["Lines", "Lines.SubLines"], query.Children);
            Assert.Equal("Id", query.OrderBy);
            Assert.Equal("Id", Restriction(query.Restrictions));
            Assert.Equal([5, 6], images.KeySet<IdList>().Select(static row => row.Id));
            Assert.Contains("Code != 'hidden'", QueryRig.Render(query.Filter), StringComparison.Ordinal);

            ScriptedStatement count = Assert.Single(batch.Of(ScriptedKind.Count));
            Assert.Equal((PipelineSaveModel.Documents, 2), (count.Spec.Root.Name, count.Cap));
            Assert.Contains("Name != 'locked'", QueryRig.Render(count.Spec.Filter), StringComparison.Ordinal);
        }

        [Fact]
        public async Task An_update_root_missing_or_hidden_is_not_found_by_its_ids_and_one_outside_the_save_filter_is_forbidden()
        {
            // The images hold the rows the read filter admits: a missing row and a hidden one alike are absent.
            Store(5);

            NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => SaveAsync(Update(5, "A"), Update(6, "B"), Update(8, "C")));
            Assert.Equal(PipelineSaveModel.Documents, missing.Resource);
            Assert.Equal(["6", "8"], missing.Ids);

            Grant(categories: null, save: "Name != 'locked'");
            _saveCount = 0;
            ForbiddenException outside = await Assert.ThrowsAsync<ForbiddenException>(() => SaveAsync(Update(5, "A")));
            Assert.Equal((PipelineSaveModel.Documents, AccessActions.Save, ForbiddenReason.OutsideFilter), (outside.Resource, outside.Action, outside.Reason));
        }

        // --- Early concurrency ---

        [Fact]
        public async Task Under_check_an_update_without_a_stamp_is_refused_at_the_stamp()
        {
            Store(5);
            PsDocument document = Update(5, "A");
            document.ModifiedAt = default;

            ValidationException refused = await RefusedAsync(document);

            Assert.Equal(["[0].ModifiedAt Concurrency.StampRequired"], Lines(refused));
        }

        [Fact]
        public async Task A_stamp_the_stored_row_has_moved_past_fails_the_save_before_any_validator_resumes_naming_who_changed_it()
        {
            Store(5);
            Store(6);
            PsDocument stale = Update(5, "A");
            stale.ModifiedAt = Stamp.AddMinutes(-1);
            bool resumed = false;
            _validation.Validate = async context =>
            {
                _ = context.Loader.ByIds<PsCategory, int>([3]);
                await context.Loader.LoadAsync();
                resumed = true;
            };

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => SaveAsync(stale, Update(6, "B")));

            Assert.Equal("concurrency-conflict", conflict.Code);
            Assert.Equal(new ConcurrencyConflict(5, "2026-11-16T08:30:00.1230000+03:00", Modifier, "Bea", IsMissing: false), Assert.Single(conflict.Conflicts));
            Assert.False(resumed);

            // The names cost a round trip of their own, unfiltered, keyed by the modifiers.
            Assert.Equal(2, ValidationTrips);
            ScriptedStatement names = Assert.Single(ValidationBatches[1].Of(ScriptedKind.Rows));
            Assert.Equal(CoreResources.User, names.Spec.Root.Name);
            Assert.Null(names.Spec.Filter);
            Assert.Equal([Modifier], names.KeySet<IdList>().Select(static row => row.Id));
        }

        [Fact]
        public async Task Under_override_a_stale_stamp_is_no_conflict()
        {
            Store(5);
            PsDocument stale = Update(5, "A");
            stale.ModifiedAt = Stamp.AddMinutes(-1);

            await ValidatedAsync(new SaveOptions { Concurrency = ConcurrencyMode.Override }, stale);

            Assert.Equal(1, ValidationTrips);
        }

        // --- Ownership, child synchronisation and references ---

        [Fact]
        public async Task An_update_keeps_what_the_server_owns_a_write_once_value_may_not_change_and_a_prefix_may_not_hold_the_separator()
        {
            Store(5, configure: static stored => (stored.Name, stored.Handler) = ("Old", "h1"));
            Store(6, configure: static stored => stored.Handler = "h2");
            PsDocument kept = Update(5, "New");
            (kept.Audit, kept.CreatedById, kept.Prefix) = ("client", 99, "A - B");
            PsDocument changed = Update(6, "B");
            changed.Handler = "other";
            object?[]? seen = null;
            _validation.Validate = async context =>
            {
                await AfterRoundTripAsync(context);
                PsDocument row = context.Entities[0];
                seen = [row.Audit, row.Handler, row.CreatedById, row.ModifiedById, context.Before(0)?.Name, context.Changed(0, static document => document.Name)];
            };

            ValidationException refused = await RefusedAsync(kept, changed);

            Assert.Equal(["[0].Prefix DisplayName.PrefixSeparator", "[1].Handler WriteOnce"], Lines(refused));
            ValidationError prefix = refused.Errors[0];
            Assert.Equal(("Prefix", " - "), (prefix.Arguments["property"], prefix.Arguments["separator"]));
            object?[] expected = ["kept", "h1", 3, Modifier, "Old", true];
            Assert.Equal(expected, seen);
        }

        [Fact]
        public async Task A_new_row_is_created_active_whatever_it_sends_and_an_update_keeps_its_stored_flag()
        {
            Warm();
            Store(5, configure: static stored => stored.IsActive = false);
            PsDocument created = New(-1, "A");
            PsDocument updated = Update(5, "B");
            (created.IsActive, updated.IsActive) = (false, true);
            bool[]? seen = null;
            _validation.Validate = async context =>
            {
                await AfterRoundTripAsync(context);
                seen = [context.Entities[0].IsActive, context.Entities[1].IsActive];
            };

            await ValidatedAsync(created, updated);

            // The class leaves the flag false: only the pipeline makes the new row active.
            Assert.Equal([true, false], seen!);
        }

        [Fact]
        public async Task A_present_collection_matches_its_children_by_id_and_a_child_its_parent_does_not_hold_is_not_found()
        {
            Warm();
            Store(5, lines: [Line(11, subLines: [21]), Line(12)]);
            Store(6, lines: [Line(13)]);
            PsDocument synced = Update(5, "A");
            PsLine kept = new() { Id = 11, SubLines = [new PsSubLine { Id = 21 }, new PsSubLine { Id = 22 }] };
            synced.Lines = [kept, new PsLine { Id = 13, SubLines = [new PsSubLine { Id = 31 }] }, new PsLine()];
            PsDocument untouched = Update(6, "B");
            bool? untouchedIsNull = null;
            int[]? storedLines = null;
            _validation.Validate = async context =>
            {
                await AfterRoundTripAsync(context);
                untouchedIsNull = context.Before(1)!.Lines is null;
                storedLines = [.. context.Before(0)!.Lines!.Select(static stored => stored.Id)];
            };

            ValidationException refused = await RefusedAsync(synced, untouched);

            // Line 13 is document 6's, which the payload leaves untouched: never re-parented, and
            // its own children are not judged again. Sub-line 22 is no child of line 11.
            Assert.Equal(["[0].Lines[0].SubLines[1].Id Entity.NotFound", "[0].Lines[1].Id Entity.NotFound"], Lines(refused));
            Assert.True(untouchedIsNull);
            Assert.Equal([11, 12], storedLines!);
        }

        [Fact]
        public async Task A_new_or_changed_reference_must_name_a_row_the_caller_sees_and_an_unguarded_target_need_only_hold_it()
        {
            Grant(categories: "Name != 'hidden'");
            Store(5, configure: static stored => stored.CategoryId = 4);
            _visible[PipelineSaveModel.Categories] = [3];
            _visible[PipelineSaveModel.Tags] = [6];
            PsDocument seen = New(-1, "A", categoryId: 3);
            (seen.TagId, seen.Lines) = (6, [new PsLine { CategoryId = 4 }]);
            PsDocument hidden = New(-2, "B", categoryId: 4);
            hidden.TagId = 7;
            PsDocument unchanged = Update(5, "C");
            unchanged.CategoryId = 4;
            Warm();

            ValidationException refused = await RefusedAsync(seen, hidden, unchanged);

            Assert.Equal(["[0].Lines[0].CategoryId Fk.NotFound", "[1].CategoryId Fk.NotFound", "[1].TagId Fk.NotFound"], Lines(refused));
            ValidationError category = Assert.Single(refused.Errors, static error => error.Path.ToString() == "[1].CategoryId");
            Assert.Equal((PipelineSaveModel.Categories, 4L), (category.Arguments["entity"], category.Arguments["id"]));

            // --- One read per target: the category under its filter, the tag for existence alone ---
            ScriptedStatement[] reads = [.. Assert.Single(ValidationBatches).Of(ScriptedKind.Rows)];
            ScriptedStatement categories = Assert.Single(reads, static read => read.Spec.Root.Name == PipelineSaveModel.Categories);
            Assert.Equal([3, 4], categories.KeySet<IdList>().Select(static row => row.Id).Order());
            Assert.Contains("Name != 'hidden'", QueryRig.Render(categories.Spec.Filter), StringComparison.Ordinal);
            Assert.Null(Assert.Single(reads, static read => read.Spec.Root.Name == PipelineSaveModel.Tags).Spec.Filter);
        }

        [Fact]
        public async Task A_target_the_caller_may_not_read_loads_nothing_and_every_reference_to_it_is_not_found()
        {
            Warm();
            _rig.Grant((PipelineSaveModel.Documents, AccessActions.Read, null), (PipelineSaveModel.Documents, AccessActions.Save, null));
            _visible[PipelineSaveModel.Categories] = [3];

            ValidationException refused = await RefusedAsync(New(-1, "A", categoryId: 3));

            Assert.Equal(["[0].CategoryId Fk.NotFound"], Lines(refused));
            Assert.Empty(Assert.Single(ValidationBatches).Of(ScriptedKind.Rows));
        }

        [Fact]
        public async Task A_new_or_changed_reference_to_a_sensitive_target_asks_for_the_step_up_a_direct_read_of_it_asks_for()
        {
            Warm();
            Store(5, configure: static stored => stored.CategoryId = 4);
            _visible[PipelineSaveModel.Categories] = [3, 4];
            _rig.Assurance = null;

            // --- Below the bar: an unchanged reference is not judged and asks nothing; a new one asks ---
            PsDocument unchanged = Update(5, "A");
            unchanged.CategoryId = 4;
            await ValidatedAsync(unchanged);
            StepUpRequiredException asked = await Assert.ThrowsAsync<StepUpRequiredException>(() => SaveAsync(New(-1, "B", categoryId: 3)));
            Assert.Equal(StepUpBar.DefaultAcr, asked.Acr);

            // --- At the bar, the same reference is judged ---
            _rig.Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, QueryRig.Now, QueryRig.Now);
            await ValidatedAsync(New(-1, "B", categoryId: 3));

            // --- A target the caller may not read tells nothing, so it asks nothing: the reference is not found ---
            _rig.Assurance = null;
            _rig.Grant((PipelineSaveModel.Documents, AccessActions.Read, null), (PipelineSaveModel.Documents, AccessActions.Save, null));
            ValidationException hidden = await RefusedAsync(New(-1, "C", categoryId: 3));
            Assert.Equal(["[0].CategoryId Fk.NotFound"], Lines(hidden));
        }

        [Fact]
        public async Task A_reference_to_a_child_row_is_read_under_its_owners_filter_seen_through_the_navigation_to_the_owners_row()
        {
            Warm();
            Grant(categories: null, read: "Name != 'secret'", save: "Name != 'secret'");
            _visible[PipelineSaveModel.Lines] = [12];
            _visible[PipelineSaveModel.SubLines] = [31];
            PsDocument document = New(-1, "A");
            (document.LineRefId, document.SubLineRefId) = (12, 32);

            ValidationException refused = await RefusedAsync(document);

            Assert.Equal(["[0].SubLineRefId Fk.NotFound"], Lines(refused));

            // --- The line's query surface composes its owner's filter; the sub-line, which has none,
            // is read under the line's, through its own navigation ---
            ScriptedStatement[] reads = [.. Assert.Single(ValidationBatches).Of(ScriptedKind.Rows)];
            QuerySpec lines = Assert.Single(reads, static read => read.Spec.Root.Name == PipelineSaveModel.Lines).Spec;
            QuerySpec subLines = Assert.Single(reads, static read => read.Spec.Root.Name == PipelineSaveModel.SubLines).Spec;
            Assert.Contains("via Document(", QueryRig.Render(lines.Filter), StringComparison.Ordinal);
            Assert.Contains("Name != 'secret'", QueryRig.Render(lines.Filter), StringComparison.Ordinal);
            Assert.StartsWith("via Line(", QueryRig.Render(subLines.Filter), StringComparison.Ordinal);
            Assert.Contains("via Document(", QueryRig.Render(subLines.Filter), StringComparison.Ordinal);

            // --- Both reads compile over the tenant's schema ---
            QueryexEngine engine = _rig.Services.GetRequiredService<QueryexEngine>();
            foreach (QuerySpec spec in (QuerySpec[])[lines, subLines])
            {
                QueryexResult<CompiledQuery> compiled = engine.CompileQuery(
                    spec with { Restrictions = [] },
                    new QueryCompilationOptions { Schema = _rig.Schema, QueryexVersion = QueryexLanguage.Version, Limits = QueryexProfiles.PipelineLimits });
                Assert.True(compiled.Succeeded, string.Join(", ", compiled.Diagnostics.Select(static diagnostic => diagnostic.Code + "@" + diagnostic.Location)));
            }
        }

        [Fact]
        public async Task A_visible_load_of_a_child_without_a_query_surface_is_read_under_its_owners_filter_through_its_navigation()
        {
            Warm();
            Grant(categories: null, read: "Name != 'secret'", save: "Name != 'secret'");
            IReadOnlyList<PsSubLine>? loaded = null;
            _validation.Validate = async context =>
            {
                ContextRef<IReadOnlyList<PsSubLine>> subLines = context.Loader.Visible<PsSubLine>(FilterTree.Leaf("Note != 'x'"));
                await context.Loader.LoadAsync();
                loaded = subLines.Value;
            };

            await ValidatedAsync(New(-1, "A"));

            // --- The rule's filter, conjoined with the documents' read seen through the line ---
            Assert.Equal([31], loaded!.Select(static subLine => subLine.Id));
            ScriptedStatement read = Assert.Single(Assert.Single(ValidationBatches).Of(ScriptedKind.Query));
            string filter = QueryRig.Render(read.Query<PsSubLine>().Filter);
            Assert.Contains("Note != 'x'", filter, StringComparison.Ordinal);
            Assert.Contains("via Line(", filter, StringComparison.Ordinal);
            Assert.Contains("via Document(", filter, StringComparison.Ordinal);
            Assert.Contains("Name != 'secret'", filter, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_visible_load_of_a_sensitive_target_asks_for_the_step_up_a_direct_read_asks_for_unless_it_is_denied()
        {
            Warm();
            _rig.Assurance = null;
            ContextRef<IReadOnlyList<PsCategory>>? categories = null;
            _validation.Validate = async context =>
            {
                categories = context.Loader.Visible<PsCategory>(FilterTree.Leaf("Name != 'x'"));
                await context.Loader.LoadAsync();
            };

            // --- Below the bar, the round fails the save before any row reaches the validator ---
            StepUpRequiredException asked = await Assert.ThrowsAsync<StepUpRequiredException>(() => SaveAsync(New(-1, "A")));
            Assert.Equal(StepUpBar.DefaultAcr, asked.Acr);
            Assert.Equal(0, ValidationTrips);
            Assert.False(categories!.IsLoaded);

            // --- At the bar, the rows load ---
            _rig.Assurance = new AuthenticationAssurance(StepUpBar.DefaultAcr, QueryRig.Now, QueryRig.Now);
            await ValidatedAsync(New(-1, "A"));
            Assert.Equal([3], categories!.Value.Select(static category => category.Id));

            // --- A target the caller may not read loads nothing and asks nothing ---
            _rig.Assurance = null;
            _rig.Grant(WithoutCategories());
            await ValidatedAsync(New(-1, "A"));
            Assert.Empty(categories!.Value);
        }

        // --- Uniqueness, cycles and value sets ---

        [Fact]
        public async Task A_unique_value_is_refused_twice_in_the_payload_or_held_by_another_stored_row_scoped_by_parent_for_a_child()
        {
            Warm();
            Store(5, configure: static stored => stored.Code = "W", lines: [Line(12, code: "L3")]);
            Store(6, configure: static stored => stored.Code = "Z", lines: []);
            Store(7, configure: static stored => stored.Code = "V");
            _codes.AddRange([Code(8, "Y"), Code(6, "Z"), Code(5, "W"), Code(7, "V")]);
            _lineCodes.AddRange([Line(12, code: "L3", documentId: 5)(), Line(13, code: "L2", documentId: 9)()]);

            // Two new codes alike but for case; one a stored row holds; two updates that swap codes; one row's own.
            PsDocument first = New(-1, "A", code: "X");
            first.Lines = [new PsLine { LineCode = "L2" }];
            PsDocument swapped = Update(5, "D", code: "Z");
            swapped.Lines = [new PsLine { LineCode = "L1" }, new PsLine { LineCode = "l1" }, new PsLine { LineCode = "L3" }];
            PsDocument other = Update(6, "E", code: "W");
            other.Lines = [new PsLine { LineCode = "L2" }];

            ValidationException refused = await RefusedAsync(first, New(-2, "B", code: "x"), New(-3, "C", code: "Y"), swapped, other, Update(7, "F", code: "V"));

            Assert.Equal(
                ["[0].Code Unique", "[1].Code Unique", "[2].Code Unique", "[3].Lines[0].LineCode Unique", "[3].Lines[1].LineCode Unique"],
                Lines(refused));

            // --- The stored holders load by value, one grouped read per entity and property ---
            ScriptedBatch batch = Assert.Single(ValidationBatches);
            Assert.Equal(
                ["Code", "Id"],
                batch.Of(ScriptedKind.Query).Where(static statement => statement.Entity == typeof(PsDocument)).Select(static statement => Restriction(statement.Query<PsDocument>().Restrictions)).Order());
            Assert.Equal("LineCode", Restriction(Assert.Single(batch.Of(ScriptedKind.Query), static statement => statement.Entity == typeof(PsLine)).Query<PsLine>().Restrictions));
        }

        [Fact]
        public async Task A_new_root_may_not_take_a_value_an_updated_root_gives_up_or_keeps_though_a_new_child_may()
        {
            Warm();
            Store(5, configure: static stored => (stored.Code, stored.Serial) = ("A", "S1"), lines: [Line(12, code: "L1")]);
            _codes.Add(Code(5, "A"));
            _serials.Add(new PsDocument { Id = 5, Serial = "S1", Name = "Stored", Title = "T" });
            _lineCodes.Add(Line(12, code: "L1", documentId: 5)());

            // Document 5 gives its code up and keeps its write-once serial, which it does not send;
            // its line 12 gives its code up to a new line of the same document.
            PsDocument renamed = Update(5, "R", code: "B");
            renamed.Lines = [new PsLine { Id = 12, LineCode = "L9" }, new PsLine { LineCode = "L1" }];
            PsDocument created = New(-1, "N", code: "a");
            created.Serial = "S1";

            ValidationException refused = await RefusedAsync(renamed, created);

            // The new root's insert runs before the existing root's update, while row 5 still holds
            // both values; a child's synchronisation updates before it inserts.
            Assert.Equal(["[1].Code Unique", "[1].Serial Unique"], Lines(refused));
        }

        [Fact]
        public async Task A_value_held_by_a_child_the_synchronisation_deletes_may_be_taken_and_an_empty_collection_deletes_every_child()
        {
            Warm();
            Store(5, lines: [Line(12, code: "L1", documentId: 5, barcode: "B1")]);
            Store(6, lines: []);
            _lineCodes.Add(Line(12, code: "L1", documentId: 5)());
            _barcodes.Add(Line(12, documentId: 5, barcode: "B1")());

            // --- Line 12 left out of its document's lines is deleted, so a new line takes its code ---
            PsDocument replaced = Update(5, "A");
            replaced.Lines = [new PsLine { LineCode = "L1" }];
            await ValidatedAsync(replaced);

            // --- An empty collection deletes every line, so another document's new line takes the
            // barcode; an absent one deletes none, so the barcode is still held ---
            PsDocument emptied = Update(5, "A");
            emptied.Lines = [];
            PsDocument taker = Update(6, "B");
            taker.Lines = [new PsLine { Barcode = "B1" }];
            await ValidatedAsync(emptied, taker);

            PsDocument untouched = Update(5, "A");
            PsDocument refusedTaker = Update(6, "B");
            refusedTaker.Lines = [new PsLine { Barcode = "B1" }];
            ValidationException refused = await RefusedAsync(untouched, refusedTaker);
            Assert.Equal(["[1].Lines[0].Barcode Unique"], Lines(refused));
        }

        [Fact]
        public async Task A_tree_whose_payload_closes_a_cycle_is_refused_at_the_parents()
        {
            Warm();
            _rig.Grant((PipelineSaveModel.Nodes, AccessActions.Read, null), (PipelineSaveModel.Nodes, AccessActions.Save, null));
            PsNode first = new() { Id = -1, ParentId = -2, Code = "a" };
            PsNode second = new() { Id = -2, ParentId = -1, Code = "b" };

            await using AsyncServiceScope scope = _rig.Scope();
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(
                () => scope.ServiceProvider.GetRequiredService<EntityService<PsNode, int>>().SaveAsync([first, second], new SaveOptions(), Token));

            Assert.Equal(["[0].ParentId Tree.Cycle", "[1].ParentId Tree.Cycle"], Lines(refused));
            Assert.Equal((401, 402, 402, 401), (first.Id, second.Id, first.ParentId, second.ParentId));
            Assert.Equal(PipelineSaveModel.Nodes, Assert.Single(Assert.Single(ValidationBatches).Of(ScriptedKind.Rows)).Spec.Root.Name);
        }

        [Fact]
        public async Task A_new_or_changed_value_outside_its_set_is_refused_unless_a_validator_judged_it_and_a_written_value_takes_the_sets_spelling()
        {
            Warm();
            Store(5, configure: static stored => stored.Color = "Purple");
            PsDocument outside = New(-1, "A");
            outside.Color = "green";
            PsDocument unchanged = Update(5, "B");
            unchanged.Color = "Purple";
            PsDocument judged = New(-2, "C");
            judged.Color = "pink";
            PsDocument written = New(-3, "D");
            _validation.Validate = context =>
            {
                context.Errors.Add(ValidationPath.Root.Index(2).Property(nameof(PsDocument.Color)), "Acme.Pink");
                context.Entities[3].Color = "blue";
                return Task.CompletedTask;
            };

            ValidationException refused = await RefusedAsync(outside, unchanged, judged, written);

            Assert.Equal(["[0].Color Validation.ValueNotInSet", "[2].Color Acme.Pink"], Lines(refused));
            ValidationError error = Assert.Single(refused.Errors, static error => error.Code == ValidationCodes.ValueNotInSet);
            Assert.Equal((PipelineSaveModel.Colors, "green"), (error.Arguments["set"], error.Arguments["value"]));
            Assert.Equal("Blue", written.Color);
        }

        // --- Warnings ---

        [Fact]
        public async Task A_validators_warning_fails_the_save_until_confirmed_and_an_error_wins_over_it()
        {
            Warm();
            string? operation = null;
            bool invalid = false;
            _validation.Validate = context =>
            {
                operation = context.Host.Operation;
                context.Host.Confirmations.Require(PipelineSaveModel.Recost, [context.Entities[0].Id]);
                if (invalid)
                {
                    context.Errors.Add(ValidationPath.Root.Index(0).Property(nameof(PsDocument.Name)), ValidationCodes.Required);
                }

                return Task.CompletedTask;
            };

            // --- Unconfirmed, the warning the validator raised fails the save, naming its rows ---
            ConfirmationRequiredException warned = await Assert.ThrowsAsync<ConfirmationRequiredException>(() => SaveAsync(New(-1, "A")));
            ConfirmationRequest request = Assert.Single(warned.Requests);
            Assert.Equal((PipelineSaveModel.Recost, 101L), (request.Key, Assert.Single(request.Ids)));
            Assert.Equal($"{PipelineSaveModel.Documents}:save", operation);

            // --- An error wins over the warning ---
            invalid = true;
            ValidationException refused = await RefusedAsync(New(-1, "A"));
            Assert.Equal(["[0].Name Required"], Lines(refused));

            // --- Confirmed, the save validates ---
            invalid = false;
            _confirmed = [PipelineSaveModel.Recost];
            await ValidatedAsync(New(-1, "A"));
        }

        // --- Failures of the data access ---

        [Fact]
        public async Task Retries_exhausted_on_a_round_are_the_database_unavailable()
        {
            Store(5);
            _ = _rig.Script.OnExecute(static (_, _) => throw new DataAccessRetryExhaustedException(new TimeoutException("timeout"), attempts: 3));

            DependencyUnavailableException unavailable = await Assert.ThrowsAsync<DependencyUnavailableException>(() => SaveAsync(Update(5, "A")));

            _ = Assert.IsType<DataAccessRetryExhaustedException>(unavailable.InnerException);
        }

        // --- Helpers ---

        /// <summary>Grants the caller the documents and the nodes, and reading the categories.</summary>
        /// <param name="categories">The filter of the categories' read; every category when null.</param>
        /// <param name="read">The filter of the documents' read; every document when null.</param>
        /// <param name="save">The filter of the documents' save; every document when null.</param>
        private void Grant(string? categories, string? read = null, string? save = null)
        {
            _rig.Grant(Grants(categories, read, save));
        }

        /// <summary>The documents and the nodes, and reading the categories.</summary>
        /// <param name="categories">The filter of the categories' read; every category when null.</param>
        /// <param name="read">The filter of the documents' read; every document when null.</param>
        /// <param name="save">The filter of the documents' save; every document when null.</param>
        /// <returns>The grants.</returns>
        private static (string Resource, string Action, string? Filter)[] Grants(string? categories, string? read = null, string? save = null)
        {
            return
            [
                (PipelineSaveModel.Documents, AccessActions.Read, read),
                (PipelineSaveModel.Documents, AccessActions.Save, save),
                (PipelineSaveModel.Categories, AccessActions.Read, categories),
                (PipelineSaveModel.Nodes, AccessActions.Read, null),
                (PipelineSaveModel.Nodes, AccessActions.Save, null),
            ];
        }

        /// <summary>The documents and the nodes, without reading the categories.</summary>
        /// <returns>The grants.</returns>
        private static (string Resource, string Action, string? Filter)[] WithoutCategories()
        {
            return [.. Grants(categories: null).Where(static grant => grant.Resource != PipelineSaveModel.Categories)];
        }

        /// <summary>Fills the buffers of every table the model saves: documents from 101, lines from 201, sub-lines from 301, nodes from 401.</summary>
        private void Warm()
        {
            _ids.Allocator.Replenish(Tenant, typeof(PsDocument), 101, 199);
            _ids.Allocator.Replenish(Tenant, typeof(PsLine), 201, 299);
            _ids.Allocator.Replenish(Tenant, typeof(PsSubLine), 301, 399);
            _ids.Allocator.Replenish(Tenant, typeof(PsNode), 401, 499);
        }

        /// <summary>Stores a document the before-image query finds, stamped and owned as the server wrote it.</summary>
        /// <param name="id">The id.</param>
        /// <param name="configure">Sets its columns; nothing when null.</param>
        /// <param name="lines">Its stored lines, built afresh per query; none when null.</param>
        private void Store(int id, Action<PsDocument>? configure = null, Func<PsLine>[]? lines = null)
        {
            _stored[id] = () =>
            {
                PsDocument stored = new()
                {
                    Id = id,
                    Name = "Stored",
                    Title = "T",
                    Audit = "kept",
                    CreatedAt = Stamp,
                    CreatedById = 3,
                    ModifiedAt = Stamp,
                    ModifiedById = Modifier,
                    Lines = lines is null ? null : [.. lines.Select(static line => line())],
                };
                configure?.Invoke(stored);
                return stored;
            };
        }

        /// <summary>A stored line, built afresh per query.</summary>
        /// <param name="id">The id.</param>
        /// <param name="subLines">The ids of its stored sub-lines; none when null.</param>
        /// <param name="code">Its line code.</param>
        /// <param name="documentId">Its document.</param>
        /// <param name="barcode">Its barcode.</param>
        /// <returns>The line's factory.</returns>
        private static Func<PsLine> Line(int id, int[]? subLines = null, string? code = null, int documentId = 0, string? barcode = null)
        {
            return () => new PsLine
            {
                Id = id,
                DocumentId = documentId,
                LineCode = code,
                Barcode = barcode,
                SubLines = subLines is null ? null : [.. subLines.Select(subLine => new PsSubLine { Id = subLine, LineId = id })],
            };
        }

        /// <summary>A stored document holding a code, as the uniqueness load finds it.</summary>
        /// <param name="id">The id.</param>
        /// <param name="code">The code.</param>
        /// <returns>The document.</returns>
        private static PsDocument Code(int id, string code)
        {
            return new PsDocument { Id = id, Code = code, Name = "Stored", Title = "T" };
        }

        /// <summary>A new document.</summary>
        /// <param name="id">Its temporary id, or zero.</param>
        /// <param name="name">Its name.</param>
        /// <param name="categoryId">Its category; none when null.</param>
        /// <param name="code">Its code; none when null.</param>
        /// <returns>The document.</returns>
        private static PsDocument New(int id, string name, int? categoryId = null, string? code = null)
        {
            return new PsDocument { Id = id, Name = name, Title = "T", CategoryId = categoryId, Code = code };
        }

        /// <summary>An update of a stored document, echoing its stamp.</summary>
        /// <param name="id">The id.</param>
        /// <param name="name">Its name.</param>
        /// <param name="code">Its code; none when null.</param>
        /// <returns>The document.</returns>
        private static PsDocument Update(int id, string name, string? code = null)
        {
            return new PsDocument { Id = id, Name = name, Title = "T", Code = code, ModifiedAt = Stamp };
        }

        /// <summary>Saves documents with the default options, as the test's hooks set.</summary>
        /// <param name="documents">The documents.</param>
        /// <returns>The save's result.</returns>
        private Task<EntitiesResult<PsDocument>> SaveAsync(params PsDocument[] documents)
        {
            return SaveAsync(new SaveOptions(), documents);
        }

        /// <summary>Saves documents in a scope of the caller, as the test's hooks set.</summary>
        /// <param name="options">The save's options.</param>
        /// <param name="documents">The documents.</param>
        /// <returns>The save's result.</returns>
        private async Task<EntitiesResult<PsDocument>> SaveAsync(SaveOptions options, params PsDocument[] documents)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            if (_confirmed.Length > 0)
            {
                // The entry point seeds the warnings the caller confirmed, once per scope.
                scope.ServiceProvider.GetRequiredService<Confirmations>().Seed(_confirmed);
            }

            PsDocumentService service = Documents(scope);
            service.Preprocess = _preprocess;
            return await service.SaveAsync(documents, options, Token);
        }

        /// <summary>Saves documents whose validation passes, which then persist on one round trip of their own.</summary>
        /// <param name="documents">The documents.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private Task ValidatedAsync(params PsDocument[] documents)
        {
            return ValidatedAsync(new SaveOptions(), documents);
        }

        /// <summary>
        ///     Saves documents whose validation passes under options, returning their ids alone, and
        ///     checks that the save sent one persist, after every validation round, returning the
        ///     payload's final ids.
        /// </summary>
        /// <param name="options">The save's options, which this sets to return ids alone.</param>
        /// <param name="documents">The documents.</param>
        /// <returns>A task that completes when the save committed.</returns>
        private async Task ValidatedAsync(SaveOptions options, params PsDocument[] documents)
        {
            // The read-back is the persist tests' concern: a validated save returns its ids alone.
            options.ReturnEntities = false;
            int before = _rig.Script.Batches.Count;

            EntitiesResult<PsDocument> saved = await SaveAsync(options, documents);

            ScriptedBatch[] sent = [.. _rig.Script.Batches.Skip(before)];
            ScriptedBatch persist = Assert.Single(sent, static batch => batch.Purpose == BatchPurpose.Persist);
            Assert.Same(persist, sent[^1]);
            Assert.True(persist.IsExecuted);
            Assert.Equal(documents.Select(static document => (long)document.Id), saved.Ids);
        }

        /// <summary>Options that save and return the ids alone.</summary>
        /// <returns>The options.</returns>
        private static SaveOptions IdsOnly()
        {
            return new SaveOptions { ReturnEntities = false };
        }

        /// <summary>Saves documents whose validation fails.</summary>
        /// <param name="documents">The documents.</param>
        /// <returns>The refusal.</returns>
        private Task<ValidationException> RefusedAsync(params PsDocument[] documents)
        {
            return RefusedAsync(new SaveOptions(), documents);
        }

        /// <summary>Saves documents whose validation fails under options.</summary>
        /// <param name="options">The save's options.</param>
        /// <param name="documents">The documents.</param>
        /// <returns>The refusal.</returns>
        private Task<ValidationException> RefusedAsync(SaveOptions options, params PsDocument[] documents)
        {
            return Assert.ThrowsAsync<ValidationException>(() => SaveAsync(options, documents));
        }

        /// <summary>
        ///     Parks a validator at a load of round trip 1, so it resumes once the before images, the
        ///     ownership and the references were settled.
        /// </summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the round ran.</returns>
        private static async Task AfterRoundTripAsync(SaveContext<PsDocument> context)
        {
            _ = context.Loader.ByIds<PsCategory, int>([3]);
            await context.Loader.LoadAsync();
        }

        /// <summary>A refusal's errors, each as its path and code, ordered.</summary>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The lines.</returns>
        private static string[] Lines(ValidationException refusal)
        {
            return [.. refusal.Errors.Select(static error => $"{error.Path} {error.Code}").Order(StringComparer.Ordinal)];
        }

        /// <summary>A scope's document service.</summary>
        /// <param name="scope">The scope.</param>
        /// <returns>The service.</returns>
        private static PsDocumentService Documents(AsyncServiceScope scope)
        {
            return scope.ServiceProvider.GetRequiredService<PsDocumentService>();
        }

        /// <summary>The path of a read's one key-set restriction.</summary>
        /// <param name="restrictions">The read's restrictions.</param>
        /// <returns>The path.</returns>
        private static string Restriction(IReadOnlyList<KeySetRestriction> restrictions)
        {
            return Assert.Single(restrictions).Path;
        }

        /// <summary>
        ///     Answers a row read: the modifiers' names, a read-back's visible ids (the rows it finds)
        ///     and its related rows (none), the tree's parents (none stored), or the referenced ids
        ///     its target holds of those it asks for.
        /// </summary>
        /// <param name="statement">The read.</param>
        /// <returns>The page.</returns>
        private RowPage Rows(ScriptedStatement statement)
        {
            string root = statement.Spec.Root.Name;
            if (root == CoreResources.User)
            {
                return new RowPage(Names(statement.Spec.Select.Split(',')), null, null, null);
            }

            if (statement.Batch.Purpose != BatchPurpose.Validate)
            {
                return statement.Options is RowQueryOptions { CaptureKeys: true } ? QueryRig.Page([.. _readBack.Select(static document => document.Id)]) : QueryRig.Page();
            }

            if (root == PipelineSaveModel.Nodes)
            {
                return new RowPage(ValidationRig.IntRows(["Id", "ParentId"]), null, null, null);
            }

            int[] held = _visible.GetValueOrDefault(root, []);
            return QueryRig.Page([.. statement.KeySet<IdList>().Select(static row => row.Id).Where(held.Contains)]);
        }

        /// <summary>
        ///     Answers a persist's save statement, marking its rows' reservation as attempted, as a
        ///     real batch marks it once it sends the persist: the ids are then kept whatever the outcome.
        /// </summary>
        /// <param name="statement">The save statement.</param>
        /// <returns>The receipt.</returns>
        private SaveReceipt Attempted(ScriptedStatement statement)
        {
            foreach (object row in (IEnumerable)statement.Statement!)
            {
                _ = _ids.Allocator.HoldOf(row)?.TryMarkPersistAttempted();
            }

            return new SaveReceipt(Committed, 1, 0, 0);
        }

        /// <summary>The modifier's row: its id, then its name in each name column the read selects, the primary one only filled.</summary>
        /// <param name="columns">The columns the read selects, the key first.</param>
        /// <returns>The row set.</returns>
        private static QueryRowSet Names(string[] columns)
        {
            using DataTable table = new();
            _ = table.Columns.Add(columns[0], typeof(int));
            foreach (string column in columns[1..])
            {
                _ = table.Columns.Add(column, typeof(string));
            }

            _ = table.Rows.Add([Modifier, "Bea", .. columns[2..].Select(static _ => (object)DBNull.Value)]);
            using DbDataReader reader = table.CreateDataReader();
            QueryRowSet.Builder builder = new(
                [.. columns.Select(static (name, index) => new QueryColumn(name, index == 0 ? QueryexType.QxNumeric : QueryexType.QxString, null, null, Nullable: true, [name], GroupingKey: false))],
                [.. columns.Select(static (_, index) => index == 0 ? typeof(int) : typeof(string))],
                1);
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }

        /// <summary>The documents' bespoke criteria, which a test sets.</summary>
        private sealed class DocumentCriteria : IAccessCriteriaProvider
        {
            /// <inheritdoc />
            public string Resource => PipelineSaveModel.Documents;

            /// <summary>The criteria every decision reads.</summary>
            public IReadOnlyList<AccessCriterion> Criteria { get; set; } = [];
        }

        /// <summary>
        ///     The ids the save reserves: the allocator's own buffers, warmed by the test; a cold buffer
        ///     makes the immediate reservation fall short, and the reservation that rides the round's
        ///     batch is then served from the buffer as the statement would have been.
        /// </summary>
        /// <param name="allocator">The allocator.</param>
        private sealed class TestIds(IdAllocator allocator) : IIdAllocator, IIdBuffer
        {
            /// <summary>The allocator, whose buffers the test warms.</summary>
            public IdAllocator Allocator { get; } = allocator;

            /// <summary>Whether the immediate reservation falls short whatever the buffer holds.</summary>
            public bool Cold { get; set; }

            /// <summary>The batches each reservation rode, in order.</summary>
            public List<IDataBatch> Reserved { get; } = [];

            /// <inheritdoc />
            public IdReservation Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows)
            {
                Reserved.Add(batch);
                return Allocator.ReserveBuffered(Tenant, location: null, entityType, rows)
                    ?? throw new InvalidOperationException("The test warms the buffers the reservation statement stands in for.");
            }

            /// <inheritdoc />
            public IdReservation? ReserveBuffered(ITenantDatabase database, Type entityType, IReadOnlyList<object> rows)
            {
                // The rig's database is no batch connection and names no location: the warmed buffers serve it.
                return Cold ? null : Allocator.ReserveBuffered(Tenant, location: null, entityType, rows);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<object>> TakeAsync(ITenantDatabase database, Type entityType, int count, CancellationToken cancellationToken = default)
            {
                return Allocator.TakeAsync(database, entityType, count, cancellationToken);
            }
        }
    }
}
