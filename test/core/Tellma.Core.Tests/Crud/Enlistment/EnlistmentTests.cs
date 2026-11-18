// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Ids;
using Tellma.Core.Tests.Crud.Query;
using Tellma.Core.Tests.Crud.Save;
using Tellma.Core.Tests.Crud.Scripted;

namespace Tellma.Core.Tests.Crud.Enlistment
{
    /// <summary>
    ///     Enlisted writes over the scripted batch: a target's save and delete composed into the host's
    ///     one persist batch, around the host's statements and nested around their own host's, each
    ///     counted under the host's outcome; the pair gate; the front door refused while a frame
    ///     composes; path maps composed through the nesting; one enlistment per row and target; a
    ///     failure attributed to the group whose statement raised it; the enlisted stamp under Check.
    /// </summary>
    public sealed partial class EnlistmentTests : IAsyncDisposable
    {
        /// <summary>The fixture tenant.</summary>
        private const int Tenant = 1;

        /// <summary>The stamp every stored row carries.</summary>
        private static readonly DateTimeOffset Stamp = new(2026, 11, 16, 8, 30, 0, 123, TimeSpan.Zero);

        /// <summary>The stamp a persist's save statements report.</summary>
        private static readonly DateTimeOffset Committed = new(2026, 11, 17, 9, 0, 0, 456, TimeSpan.Zero);

        /// <summary>The caller's grants: the orders' Save, Read, Delete and the entity action's securable.</summary>
        private static readonly (string Resource, string Action, string? Filter)[] Grants =
        [
            (EnlistmentModel.Orders, AccessActions.Save, null),
            (EnlistmentModel.Orders, AccessActions.Read, null),
            (EnlistmentModel.Orders, AccessActions.Delete, null),
            (EnlistmentModel.Orders, EnlistmentModel.TouchAction, null),
        ];

        /// <summary>The model's hooks and methods, shared with the rig's scopes.</summary>
        private readonly EnHooks _hooks = new();

        /// <summary>The access guards, which record their calls and append a statement each.</summary>
        private readonly PsGuards _guards = new();

        /// <summary>The composed model.</summary>
        private readonly QueryRig _rig;

        /// <summary>The ids the saves reserve.</summary>
        private readonly EnIds _ids;

        /// <summary>The note ids the before-image and target loads find.</summary>
        private readonly HashSet<int> _storedNotes = [];

        /// <summary>The order ids the before-image loads find.</summary>
        private readonly HashSet<int> _storedOrders = [];

        /// <summary>The services of the scope the current operation runs in; null between operations.</summary>
        private IServiceProvider? _scoped;

        /// <summary>Composes the model with the test's ids, interceptors and guards, grants the orders, and scripts every statement.</summary>
        public EnlistmentTests()
        {
            _rig = new QueryRig(EnlistmentModel.Compose, services =>
            {
                _ = services.AddSingleton(_hooks);
                _ = services.AddFakeLogging();
                _ = services.AddSingleton(static provider => new EnIds(provider.GetRequiredService<IdAllocator>()));
                _ = services.Replace(ServiceDescriptor.Singleton<IIdAllocator>(static provider => provider.GetRequiredService<EnIds>()));
                _ = services.Replace(ServiceDescriptor.Singleton<IIdBuffer>(static provider => provider.GetRequiredService<EnIds>()));
                _ = services.AddSingleton<IApiActionInterceptor>(new RecordingInterceptor("outer", _hooks));
                _ = services.AddSingleton<IApiActionInterceptor>(new RecordingInterceptor("inner", _hooks));
                _ = services.Replace(ServiceDescriptor.Singleton<IAccessGuards>(_guards));
            });
            _ids = _rig.Services.GetRequiredService<EnIds>();
            _ids.Allocator.Replenish(Tenant, typeof(EnOrder), 101, 199);
            _ids.Allocator.Replenish(Tenant, typeof(EnNote), 201, 299);
            _ids.Allocator.Replenish(Tenant, typeof(EnTag), 301, 399);
            _ids.Allocator.Replenish(Tenant, typeof(User), 401, 499);
            _rig.Grant(Grants);

            // --- Every statement a frame may append: the rows the loads find, unfiltered, and every
            // persist's saves, deletes and raw statements ---
            _ = _rig.Script
                .OnQuery(statement => (IReadOnlyList<EnNote>)[.. statement.KeySet<IdList>().Where(row => _storedNotes.Contains(row.Id)).Select(static row => StoredNote(row.Id))])
                .OnQuery(statement => (IReadOnlyList<EnOrder>)[.. statement.KeySet<IdList>().Where(row => _storedOrders.Contains(row.Id)).Select(static row => StoredOrder(row.Id))])
                .OnQuery<User>(static _ => [])
                .OnSave<EnOrder>(static _ => new SaveReceipt(Committed, 1, 0, 0))
                .OnSave<EnNote>(static _ => new SaveReceipt(Committed, 1, 0, 0))
                .OnSave<EnTag>(static _ => new SaveReceipt(Committed, 1, 0, 0))
                .OnSave<User>(static _ => new SaveReceipt(Committed, 1, 0, 0))
                .OnDelete<EnNote>(static statement => new DeleteReceipt(new HashSet<object>(((DeleteSpec<EnNote>.ByIds)statement.Statement!).Ids)))
                .OnDelete<EnOrder>(static statement => new DeleteReceipt(new HashSet<object>(((DeleteSpec<EnOrder>.ByIds)statement.Statement!).Ids)))
                .OnSql(static _ => RawResult.Empty);
        }

        /// <summary>The test's cancellation.</summary>
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        /// <summary>The persist batches, in order.</summary>
        private ScriptedBatch[] Persists => [.. _rig.Script.Batches.Where(static batch => batch.Purpose == BatchPurpose.Persist)];

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            return _rig.DisposeAsync();
        }

        // --- One batch ---

        [Fact]
        public async Task A_save_enlisted_from_a_validator_commits_in_the_hosts_one_batch_after_its_statements_and_runs_the_targets_hooks()
        {
            EnNote note = Note("n");
            string? seen = null;
            _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [note]);
            _hooks.NoteValidate = (_, context) =>
            {
                seen = $"{context.Entities[0].Id} {context.Enlistment?.TargetResource} {context.Host.Operation}";
                return Task.CompletedTask;
            };

            _ = await SaveOrdersAsync(Order("A"));

            // --- One round trip, the persist: the host's save, then the target's ---
            Assert.Equal(1, _rig.Script.RoundTrips);
            ScriptedBatch persist = Assert.Single(Persists);
            Assert.Equal([typeof(EnOrder), typeof(EnNote)], persist.Of(ScriptedKind.Save).Select(static statement => statement.Entity));

            // --- The target validated with its final id under its own host; the rows carry the batch's stamp ---
            Assert.Equal($"201 {EnlistmentModel.Notes} {EnlistmentModel.Orders}:save", seen);
            Assert.Equal((201, Committed), (note.Id, note.ModifiedAt));
            Assert.Equal([EnlistmentModel.Orders, EnlistmentModel.Notes], _hooks.Committed);
        }

        [Fact]
        public async Task An_enlisted_update_loads_its_before_image_unfiltered_in_the_round_its_writer_waits_for_adding_no_round_trip()
        {
            _storedNotes.Add(5);
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [Note("changed", id: 5)]);
                _ = context.Loader.ByIds<EnOrder, int>([9]);
                await context.Loader.LoadAsync();
            };

            _ = await SaveOrdersAsync(Order("A"));

            // --- The note's image rides the validator's own round: one round, then the persist ---
            Assert.Equal(2, _rig.Script.RoundTrips);
            ScriptedBatch round = _rig.Script.Batches[0];
            Assert.Equal(BatchPurpose.Validate, round.Purpose);
            ScriptedStatement image = Assert.Single(round.Of(ScriptedKind.Query), static statement => statement.Entity == typeof(EnNote));
            Assert.Null(image.Query<EnNote>().Filter);
            Assert.Single(round.Of(ScriptedKind.Query), static statement => statement.Entity == typeof(EnOrder));
            Assert.Equal([typeof(EnOrder), typeof(EnNote)], Assert.Single(Persists).Of(ScriptedKind.Save).Select(static statement => statement.Entity));
        }

        [Fact]
        public async Task An_enlisted_delete_loads_its_targets_unfiltered_validates_them_and_deletes_them_ahead_of_the_host_with_no_filter_and_no_stamps()
        {
            _storedNotes.Add(5);
            IReadOnlyList<int>? validated = null;
            _hooks.OrderValidate = (service, context) =>
            {
                _ = service.EnlistDelete<EnNote, int>(context.Host, [5, 6, 5], new EnlistDeleteOptions { MapPath = Under(ValidationPath.Root.Index(0).Property("Notes")) });
                return Task.CompletedTask;
            };
            _hooks.NoteValidateDelete = async (_, context) =>
            {
                // The targets load in the host's next round, which the validator joins.
                await context.Loader.LoadAsync();
                validated = [.. context.Entities.Select(static note => note.Id)];
            };

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));

            // --- The present row validated; the missing one not found at the mapped path, with its id ---
            Assert.Equal([5], validated);
            ValidationError missing = Assert.Single(refused.Errors);
            Assert.Equal(("[0].Notes", ValidationCodes.EntityNotFound), (missing.Path.ToString(), missing.Code));
            Assert.Equal([6L], Assert.IsType<long[]>(missing.Arguments["ids"]));
            Assert.Equal(EnlistmentModel.Notes, missing.Arguments["enlistedResource"]);
            Assert.Empty(Persists);

            // --- Every id present: the delete ahead of the host's save, by its ids alone ---
            _storedNotes.Add(6);
            _ = await SaveOrdersAsync(Order("A"));
            ScriptedBatch persist = Assert.Single(Persists);
            ScriptedStatement delete = Assert.Single(persist.Of(ScriptedKind.Delete));
            Assert.True(delete.Ordinal < Assert.Single(persist.Of(ScriptedKind.Save)).Ordinal);
            DeleteSpec<EnNote>.ByIds spec = Assert.IsType<DeleteSpec<EnNote>.ByIds>(delete.Statement);
            Assert.Equal([5, 6], spec.Ids.Cast<int>());
            Assert.Equal((null, null, null), (spec.ReadFilter, spec.Filter, spec.ExpectedStamps));
            Assert.Equal([EnlistmentModel.Orders, EnlistmentModel.Notes], _hooks.Committed);
        }

        [Fact]
        public async Task Each_enlisted_write_is_counted_once_under_its_hosts_operation_its_kind_and_its_hosts_outcome()
        {
            using MetricCollector<long> enlistments = new(_rig.Services.GetRequiredService<IMeterFactory>(), CrudTelemetryNames.MeterName, CrudTelemetryNames.Enlistments);
            _storedNotes.Add(5);
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [Note("n")]);
                _ = service.EnlistDelete<EnNote, int>(context.Host, [5]);
            };

            // --- Committed: one save and one delete under the host's save ---
            _ = await SaveOrdersAsync(Order("A"));
            Assert.Equal(
                ["save save ok", "save delete ok"],
                enlistments.GetMeasurementSnapshot().Select(static measurement => Tagged(measurement)));

            // --- Refused by the target's validator: both counted under the host's validation outcome ---
            enlistments.Clear();
            _hooks.NoteValidate = static (_, context) =>
            {
                context.Errors.Add(ValidationPath.Root, ValidationCodes.Required);
                return Task.CompletedTask;
            };
            _ = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal(
                ["save save validation", "save delete validation"],
                enlistments.GetMeasurementSnapshot().Select(static measurement => Tagged(measurement)));
        }

        // --- The pair gate ---

        [Fact]
        public async Task A_writer_the_composition_never_registered_is_refused_as_an_undeclared_pair()
        {
            _hooks.OrderValidate = async (service, context) => _ = await new EnOutsider().EnlistSaveAsync(context.Host, [Note("n")]);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));

            Assert.Contains("holds no such pair", refused.Message, StringComparison.Ordinal);
            Assert.Empty(Persists);
        }

        [Fact]
        public async Task A_writer_that_declares_the_pair_but_takes_no_part_in_the_frame_is_refused()
        {
            _hooks.OrderValidate = async (service, context) => _ = await Scoped<EnOtherService>().EnlistSaveAsync(context.Host, [Note("n")]);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));

            Assert.Contains("takes no part in that frame", refused.Message, StringComparison.Ordinal);
            Assert.Empty(Persists);
        }

        [Fact]
        public async Task An_enlistment_from_preprocessing_or_from_a_persist_hook_is_refused_as_a_closed_phase()
        {
            // --- Preprocessing: the frame is not validating yet ---
            _hooks.OrderPreprocess = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Note("n")]);
            InvalidOperationException early = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Contains("while it was not validating", early.Message, StringComparison.Ordinal);
            _hooks.OrderPreprocess = null;

            // --- The persist hook: the frame's rounds are over ---
            IWriteHost? host = null;
            _hooks.OrderValidate = (_, context) =>
            {
                host = context.Host;
                return Task.CompletedTask;
            };
            _hooks.OrderContribute = async (service, context) => _ = await service.EnlistSaveAsync(host!, [Note("n")]);
            InvalidOperationException late = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Contains("while it was not validating", late.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_self_pair_from_a_pipeline_frame_is_refused()
        {
            _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(context.Host, [Order("B")]);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));

            Assert.Contains("to its own stack", refused.Message, StringComparison.Ordinal);
            Assert.Empty(Persists);
        }

        [Fact]
        public async Task A_service_call_through_the_front_door_from_inside_a_composing_frame_is_refused()
        {
            _hooks.OrderValidate = async (_, _) => _ = await Scoped<EnNoteService>().SaveAsync([Note("n")], IdsOnly(), Token);

            InvalidOperationException refused = await Assert.ThrowsAsync<InvalidOperationException>(() => SaveOrdersAsync(Order("A")));

            Assert.Contains("which is composing", refused.Message, StringComparison.Ordinal);
            Assert.Empty(Persists);
        }

        // --- Order and nesting ---

        [Fact]
        public async Task Groups_sit_around_their_immediate_hosts_statements_so_a_before_group_with_an_after_group_of_its_own_composes_B_C_A()
        {
            // --- A enlists B ahead of it and D after it; each note enlists a tag after itself ---
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [Note("B")], new EnlistSaveOptions { Order = EnlistOrder.BeforeHost });
                _ = await service.EnlistSaveAsync(context.Host, [Note("D")]);
            };
            _hooks.NoteValidate = async (service, context) =>
                _ = await service.EnlistSaveAsync(context.Host, [new EnTag { Label = $"tag of {context.Entities[0].Text}" }]);

            _ = await SaveOrdersAsync(Order("A"));

            // --- B, its tag, A, then D and its tag: each composite group in its declared position. The
            // host's post-commit runs first, then each group's in composition order; tags have no service ---
            ScriptedBatch persist = Assert.Single(Persists);
            Assert.Equal(
                ["note B", "tag tag of B", "order A", "note D", "tag tag of D"],
                persist.Of(ScriptedKind.Save).Select(static statement => Describe(statement)));
            Assert.Equal(1, _rig.Script.RoundTrips);
            Assert.Equal([EnlistmentModel.Orders, EnlistmentModel.Notes, EnlistmentModel.Notes], _hooks.Committed);
        }

        [Fact]
        public async Task Path_maps_compose_through_the_nesting_and_every_rerooted_error_names_the_innermost_enlisted_stack()
        {
            _hooks.OrderValidate = async (service, context) =>
                _ = await service.EnlistSaveAsync(context.Host, [Note("ok"), new EnNote { Text = null }], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Index(0).Property("Notes")) });
            _hooks.NoteValidate = async (service, context) =>
                _ = await service.EnlistSaveAsync(context.Host, [new EnTag { Label = null }], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Property("Tags")) });

            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));

            // --- The note's error under the order's map; the tag's under both, the innermost stack named ---
            Assert.Equal(
                [$"[0].Notes[1].Text {ValidationCodes.Required} {EnlistmentModel.Notes}"],
                refused.Errors.Select(static error => $"{error.Path} {error.Code} {error.Arguments["enlistedResource"]}"));
            Assert.Empty(Persists);

            // --- Valid notes: the tag's own error re-rooted through both maps ---
            _hooks.OrderValidate = async (service, context) =>
                _ = await service.EnlistSaveAsync(context.Host, [Note("ok")], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Index(0).Property("Notes")) });
            refused = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal(
                [$"[0].Notes.Tags[0].Label {ValidationCodes.Required} {EnlistmentModel.Tags}"],
                refused.Errors.Select(static error => $"{error.Path} {error.Code} {error.Arguments["enlistedResource"]}"));
        }

        [Fact]
        public async Task A_row_enlisted_twice_with_one_frame_is_refused_at_the_second_enlistment()
        {
            _storedNotes.Add(5);
            Exception? second = null;
            _hooks.OrderValidate = async (service, context) =>
            {
                _ = await service.EnlistSaveAsync(context.Host, [Note("first", id: 5)]);
                try
                {
                    _ = service.EnlistDelete<EnNote, int>(context.Host, [5]);
                }
                catch (InvalidOperationException refused)
                {
                    second = refused;
                }
            };

            _ = await SaveOrdersAsync(Order("A"));

            // --- The second refused, the first composed alone ---
            Assert.Contains("was enlisted twice in the frame", Assert.IsType<InvalidOperationException>(second).Message, StringComparison.Ordinal);
            ScriptedBatch persist = Assert.Single(Persists);
            Assert.Empty(persist.Of(ScriptedKind.Delete));
            Assert.Equal([typeof(EnOrder), typeof(EnNote)], persist.Of(ScriptedKind.Save).Select(static statement => statement.Entity));
        }

        // --- Attribution and concurrency ---

        [Fact]
        public async Task A_persist_failure_is_translated_by_the_group_whose_statement_raised_it_and_rerooted_onto_the_hosts_paths()
        {
            _hooks.OrderValidate = async (service, context) =>
                _ = await service.EnlistSaveAsync(context.Host, [Note("n")], new EnlistSaveOptions { MapPath = Under(ValidationPath.Root.Index(0).Property("Notes")) });

            // --- Raised by the note's save: the group's translation, at the mapped path ---
            FailPersistAt(typeof(EnNote), static ordinal => new TreeDepthExceededException(typeof(EnNote), ordinal));
            ValidationException deep = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$"[0].Notes {ValidationCodes.TreeTooDeep} {EnlistmentModel.Notes}"], Lines(deep));

            // --- A missing row of the group: not found at the mapped path with its ids, never a 404 ---
            FailPersistAt(typeof(EnNote), static ordinal => new BatchAssertionFailedException(TellmaSqlErrors.NotFound, ValidationCodes.EntityNotFound, ordinal, missingIds: [7]));
            ValidationException missing = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$"[0].Notes {ValidationCodes.EntityNotFound} {EnlistmentModel.Notes}"], Lines(missing));

            // --- Raised by the host's own save: the host's translation, at its own root ---
            FailPersistAt(typeof(EnOrder), static ordinal => new TreeDepthExceededException(typeof(EnOrder), ordinal));
            ValidationException own = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$" {ValidationCodes.TreeTooDeep} -"], Lines(own));
        }

        [Fact]
        public async Task An_enlisted_update_under_Check_refuses_a_default_stamp_at_the_mapped_path_and_carries_an_echoed_one_to_the_emitter()
        {
            _storedNotes.Add(5);
            EnNote note = Note("changed", id: 5);
            _hooks.OrderValidate = async (service, context) => _ = await service.EnlistSaveAsync(
                context.Host,
                [note],
                new EnlistSaveOptions { Concurrency = ConcurrencyMode.Check, MapPath = Under(ValidationPath.Root.Index(0).Property("Note")) });

            // --- No stamp echoed: refused before any persist ---
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveOrdersAsync(Order("A")));
            Assert.Equal([$"[0].Note[0].ModifiedAt {ValidationCodes.ConcurrencyStampRequired} {EnlistmentModel.Notes}"], Lines(refused));
            Assert.Empty(Persists);

            // --- The stamp it loaded: the emitter compares it under Check. The row is read as the batch
            // runs, since the commit restamps it ---
            note = Note("changed", id: 5);
            note.ModifiedAt = Stamp;
            DateTimeOffset? echoed = null;
            _ = _rig.Script.OnSave<EnNote>(statement =>
            {
                echoed = Assert.Single((IReadOnlyList<EnNote>)statement.Statement!).ModifiedAt;
                return new SaveReceipt(Committed, 1, 0, 0);
            });
            _ = await SaveOrdersAsync(Order("A"));
            ScriptedStatement save = Assert.Single(Assert.Single(Persists).Of(ScriptedKind.Save), static statement => statement.Entity == typeof(EnNote));
            Assert.Equal(ConcurrencyMode.Check, save.Options);
            Assert.Equal(Stamp, echoed);
        }

        // --- Helpers ---

        /// <summary>A new order.</summary>
        /// <param name="name">The name.</param>
        /// <returns>The order.</returns>
        private static EnOrder Order(string name)
        {
            return new EnOrder { Name = name };
        }

        /// <summary>A note: new, or an update of a stored one.</summary>
        /// <param name="text">The text.</param>
        /// <param name="id">The stored note's id; 0 for a new one.</param>
        /// <returns>The note.</returns>
        private static EnNote Note(string text, int id = 0)
        {
            return new EnNote { Id = id, Text = text };
        }

        /// <summary>A stored note as a load finds it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The note.</returns>
        private static EnNote StoredNote(int id)
        {
            return new EnNote { Id = id, Text = "stored", Origin = "stored", CreatedAt = Stamp, CreatedById = 7, ModifiedAt = Stamp, ModifiedById = 7 };
        }

        /// <summary>A stored order as a load finds it.</summary>
        /// <param name="id">The id.</param>
        /// <returns>The order.</returns>
        private static EnOrder StoredOrder(int id)
        {
            return new EnOrder { Id = id, Name = "stored", CreatedAt = Stamp, CreatedById = 7, ModifiedAt = Stamp, ModifiedById = 7 };
        }

        /// <summary>A path map that places the target's paths under a prefix.</summary>
        /// <param name="prefix">The prefix.</param>
        /// <returns>The map.</returns>
        private static Func<ValidationPath, ValidationPath> Under(ValidationPath prefix)
        {
            return path => new ValidationPath([.. prefix.Segments, .. path.Segments]);
        }

        /// <summary>Describes a counted enlistment by its operation, its kind and its outcome.</summary>
        /// <param name="measurement">The measurement.</param>
        /// <returns>The description.</returns>
        private static string Tagged(CollectedMeasurement<long> measurement)
        {
            return $"{measurement.Tags[CrudTelemetryNames.OperationTag]} {measurement.Tags[CrudTelemetryNames.KindTag]} {measurement.Tags[CrudTelemetryNames.OutcomeTag]}";
        }

        /// <summary>Describes a recorded save by its entity and its first row's name.</summary>
        /// <param name="statement">The save.</param>
        /// <returns>The description.</returns>
        private static string Describe(ScriptedStatement statement)
        {
            return statement.Statement switch
            {
                IReadOnlyList<EnOrder> orders => $"order {orders[0].Name}",
                IReadOnlyList<EnNote> notes => $"note {notes[0].Text}",
                IReadOnlyList<EnTag> tags => $"tag {tags[0].Label}",
                _ => statement.ToString(),
            };
        }

        /// <summary>Each error as its path, its code and the enlisted stack it names, or <c>-</c>.</summary>
        /// <param name="refusal">The refusal.</param>
        /// <returns>The lines, in order.</returns>
        private static string[] Lines(ValidationException refusal)
        {
            return [.. refusal.Errors.Select(static error => $"{error.Path} {error.Code} {error.Arguments.GetValueOrDefault("enlistedResource") ?? "-"}")];
        }

        /// <summary>Saves orders in a scope of their own, answering ids only.</summary>
        /// <param name="orders">The orders.</param>
        /// <returns>The save's result.</returns>
        private async Task<EntitiesResult<EnOrder>> SaveOrdersAsync(params EnOrder[] orders)
        {
            await using AsyncServiceScope scope = _rig.Scope();
            _scoped = scope.ServiceProvider;
            _hooks.Committed.Clear();
            try
            {
                return await scope.ServiceProvider.GetRequiredService<EnOrderService>().SaveAsync(orders, IdsOnly(), Token);
            }
            finally
            {
                _scoped = null;
            }
        }

        /// <summary>A service of the scope the current operation runs in.</summary>
        /// <typeparam name="T">The service.</typeparam>
        /// <returns>The service.</returns>
        private T Scoped<T>()
            where T : notnull
        {
            return (_scoped ?? throw new InvalidOperationException("No operation runs.")).GetRequiredService<T>();
        }

        /// <summary>Makes the next persist batch fail at a save of an entity, as the executor attributes a server error.</summary>
        /// <param name="entity">The entity whose save raises the error.</param>
        /// <param name="failure">Creates the failure from the statement's ordinal.</param>
        /// <param name="occurrence">Which of the entity's saves raises it, from 0 for the first.</param>
        private void FailPersistAt(Type entity, Func<int, DataAccessException> failure, int occurrence = 0)
        {
            _ = _rig.Script.OnExecute((batch, _) => batch.Purpose != BatchPurpose.Persist
                ? Task.CompletedTask
                : throw failure(batch.Of(ScriptedKind.Save).Where(statement => statement.Entity == entity).ElementAt(occurrence).Ordinal));
        }

        /// <summary>The options of a save that answers ids only.</summary>
        /// <returns>The options.</returns>
        private static SaveOptions IdsOnly()
        {
            return new SaveOptions { ReturnEntities = false };
        }

        /// <summary>
        ///     The ids the saves reserve: the allocator's own buffers, warmed by the test, so every
        ///     reservation is served at once.
        /// </summary>
        /// <param name="allocator">The allocator.</param>
        private sealed class EnIds(IdAllocator allocator) : IIdAllocator, IIdBuffer
        {
            /// <summary>The allocator, whose buffers the test warms.</summary>
            public IdAllocator Allocator { get; } = allocator;

            /// <inheritdoc />
            public IdReservation Reserve(IDataBatch batch, Type entityType, IReadOnlyList<object> rows)
            {
                return Allocator.ReserveBuffered(Tenant, location: null, entityType, rows)
                    ?? throw new InvalidOperationException("The test warms the buffers the reservation statement stands in for.");
            }

            /// <inheritdoc />
            public IdReservation? ReserveBuffered(ITenantDatabase database, Type entityType, IReadOnlyList<object> rows)
            {
                return Allocator.ReserveBuffered(Tenant, location: null, entityType, rows);
            }

            /// <inheritdoc />
            public Task<IReadOnlyList<object>> TakeAsync(ITenantDatabase database, Type entityType, int count, CancellationToken cancellationToken = default)
            {
                return Allocator.TakeAsync(database, entityType, count, cancellationToken);
            }
        }

        /// <summary>An interceptor that records when it runs around the method, by its name.</summary>
        /// <param name="name">The name.</param>
        /// <param name="hooks">The shared body, which holds the record.</param>
        private sealed class RecordingInterceptor(string name, EnHooks hooks) : IApiActionInterceptor
        {
            /// <inheritdoc />
            public async Task<object?> InvokeAsync(ApiActionDescriptor action, object? body, Func<Task<object?>> next, CancellationToken cancellationToken)
            {
                hooks.Calls.Add($"{name} before");
                object? result = await next();
                hooks.Calls.Add($"{name} after");
                return result;
            }
        }
    }
}
