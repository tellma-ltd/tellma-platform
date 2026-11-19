// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics.Metrics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §11.1, §12, §14 and §16.1: what each capability's recipe promises, and the
    // instruments every operation records.
    /// <content>The capability recipes and the instruments.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>
        ///     An activatable stack's queries read an inactive row only when asked for inactive rows,
        ///     whether its actions or the closing class stored the row inactive.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task An_activatable_stack_reads_inactive_rows_only_when_asked()
        {
            Assert.SkipUnless(Stack.Capabilities.Contains(Activatable), $"The stack {Stack.Resource} is not activatable.");
            Assert.SkipUnless(Serves(StackOperations.Query), $"The stack {Stack.Resource} serves no query.");

            // --- An inactive row: deactivated by the stack's action, or, on a stack without its
            // actions, stored inactive by the closing class ---
            TEntity created;
            string how;
            if (ServesActivation())
            {
                created = await CreateNewAsync();
                _ = await AsWriterAsync(service => service.ExecuteActionAsync(DeactivateAction, [created.Id], null, new ActionOptions(), Token));
                how = "deactivated";
            }
            else
            {
                TEntity entity = NewValidEntity(NextOrdinal());
                Property(ActiveProperty).Setter(entity, false);
                created = await CreateAsync(entity);
                how = $"stored inactive (or {nameof(CreateAsync)} stored it active: store the flag the entity holds)";
            }

            Assert.False(await ExistsAsync(created.Id, includeInactive: false), $"{Stack.Resource}: a query read a row {how} without being asked for inactive rows.");
            Assert.True(await ExistsAsync(created.Id, includeInactive: true), $"{Stack.Resource}: a query asked for inactive rows did not read a row {how}.");
        }

        /// <summary>
        ///     An activatable stack that serves its actions stores new rows active whatever the payload
        ///     says, leaves a row already in the state an action asks for unstamped, and keeps the
        ///     stored flag on an update.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task An_activatable_stack_creates_rows_active_keeps_the_flag_on_save_and_changes_it_only_by_its_actions()
        {
            Assert.SkipUnless(Stack.Capabilities.Contains(Activatable), $"The stack {Stack.Resource} is not activatable.");
            Assert.SkipUnless(ServesActivation(), $"The stack {Stack.Resource} serves no save, so it has no activation actions.");
            PropertyMetadata active = Property(ActiveProperty);

            // --- Created active, though the payload says inactive ---
            TEntity entity = NewValidEntity(NextOrdinal());
            active.Setter(entity, false);
            TEntity created = await CreateAsync(entity);
            Assert.True(await ExistsAsync(created.Id, includeInactive: false), $"{Stack.Resource}: a row created inactive by its payload was stored inactive.");

            // --- An action that finds the row in its state already leaves it unstamped ---
            DateTimeOffset? stamp = Metadata.IsTopLevel ? StampOf(await LoadAsync(created.Id)) : null;
            _ = await AsWriterAsync(service => service.ExecuteActionAsync(ActivateAction, [created.Id], null, new ActionOptions(), Token));
            Assert.True(stamp is null || stamp == StampOf(await LoadAsync(created.Id)), $"{Stack.Resource}: activating an active row stamped it.");

            // --- Deactivated by its action, then an update keeps the stored flag, whatever the payload says ---
            _ = await AsWriterAsync(service => service.ExecuteActionAsync(DeactivateAction, [created.Id], null, new ActionOptions(), Token));
            Assert.False(await ExistsAsync(created.Id, includeInactive: false), $"{Stack.Resource}: the deactivate left the row active.");
            TEntity loaded = await LoadAsync(created.Id);
            active.Setter(loaded, true);
            Change(loaded, NextOrdinal());
            _ = await SaveAsync([loaded]);
            Assert.False(await ExistsAsync(created.Id, includeInactive: false), $"{Stack.Resource}: an update's payload reactivated the row.");
        }

        /// <summary>
        ///     A tree stack reads a parent's children, refuses a parent under the row's own subtree as
        ///     a cycle, and deletes a subtree whole.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_tree_stack_reads_children_by_parent_refuses_a_cycle_and_deletes_a_subtree_whole()
        {
            Assert.SkipUnless(Stack.Capabilities.Contains(Tree), $"The stack {Stack.Resource} is not a tree.");
            TreeMetadata tree = Metadata.Tree!;

            // --- A parent, its child and its grandchild ---
            TEntity parent = await CreateNewAsync();
            TEntity child = await CreateAsync(NewChildOf(parent, NextOrdinal()));
            TEntity grandchild = await CreateAsync(NewChildOf(child, NextOrdinal()));

            // --- The parent's children, and not their own ---
            if (Serves(StackOperations.Query))
            {
                Assert.Equal([child.Id], Keys(await AsWriterAsync(service => service.Pipeline.GetByParentIdsAsync(ByParents([parent.Id]), Token))));
            }

            // --- The parent under its own grandchild closes a cycle ---
            if (Serves(StackOperations.Save))
            {
                TEntity moving = await LoadAsync(parent.Id);
                tree.ParentKey.Setter(moving, grandchild.Id);
                ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([moving]));
                Assert.Equal([$"[0].{tree.ParentKey.Name} {ValidationCodes.TreeCycle}"], Lines(refused));
            }

            // --- The subtree, deleted whole ---
            if (Serves(StackOperations.Delete))
            {
                AffectedResult deleted = await AsWriterAsync(service => service.Pipeline.DeleteWithDescendantsAsync([parent.Id], Token));
                Assert.Equal(3, deleted.Count);
                foreach (TKey id in (TKey[])[parent.Id, child.Id, grandchild.Id])
                {
                    Assert.False(await ExistsAsync(id), $"{Stack.Resource}: the subtree's row {Text(id)} survived its delete.");
                }
            }
        }

        /// <summary>
        ///     A cacheable stack's stored row reaches its list, and the list is served from the cache at
        ///     no round trip, or at two on every read beyond the list's row limit.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_cacheable_stack_serves_its_list_from_the_cache_and_a_stored_row_reaches_it()
        {
            Assert.SkipUnless(Serves(StackOperations.Details, Cacheable), $"The stack {Stack.Resource} is not cacheable.");
            StackConformanceCaller writer = await WriterAsync();

            // --- Filled once; a stored row moves the entity's tag, and once a batch reads the tags, the list refills ---
            _ = await AsWriterAsync(service => service.GetAllCachedAsync(DetailsRequest.None, Token));
            TEntity stored = await CreateNewAsync();
            EntitiesResult<TEntity> refreshed = await AsWriterAsync(async service =>
            {
                if (Serves(StackOperations.Query))
                {
                    _ = await service.QueryAsync(ById(stored.Id), Token);
                }

                return await service.GetAllCachedAsync(DetailsRequest.None, Token);
            });
            Assert.Contains(long.CreateChecked(stored.Id), refreshed.Ids);

            // --- Then served from the cache, unless the table outgrew the list's row limit ---
            bool? oversized = await OversizedAsync();
            Assert.SkipWhen(oversized is null, $"The stack {Stack.Resource} serves no query to count its rows by, so the suite cannot tell whether its cached list is beyond its row limit.");
            _ = await AsWriterAsync(service => service.GetAllCachedAsync(DetailsRequest.None, Token));
            AssertTrips(oversized == true ? 2 : 0, await CountedAsync(writer, scope => Service(scope).GetAllCachedAsync(DetailsRequest.None, Token)), "a cached list read");
        }

        /// <summary>
        ///     Each searchable property finds the row that holds the term, a fragment from inside its
        ///     value finds the row only by a contains search, and a term no row holds finds nothing.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Each_searchable_property_finds_its_row_and_a_term_no_row_holds_finds_none()
        {
            Assert.SkipUnless(Serves(StackOperations.Query), $"The stack {Stack.Resource} serves no query.");
            Assert.SkipWhen(Stack.SearchableProperties.Count == 0, $"The stack {Stack.Resource} has no searchable properties.");
            TEntity stored = await CreateNewAsync();

            // --- Every searchable value of the row, so a fragment's expected answer weighs each property's kind ---
            List<(string Name, SearchKind Kind, string Value)> values = [];
            foreach ((string name, SearchKind kind) in Stack.SearchableProperties)
            {
                string value = Property(name).Getter(stored) as string ?? string.Empty;
                Assert.True(value.Length >= 2, $"{nameof(NewValidEntity)} gives the searchable {name} of {Stack.Resource} the value '{value}': give it at least two characters, so a fragment from inside it tells a prefix search from a contains search.");
                values.Add((name, kind, value));
            }

            foreach ((string name, SearchKind kind, string term) in values)
            {
                // --- The row's own value, a fragment from inside it, then a term no row holds; the
                // filter keeps the read to the row ---
                string fragment = term[1..];
                bool fragmentFinds = values.Any(other => other.Kind == SearchKind.Prefix
                    ? other.Value.StartsWith(fragment, StringComparison.OrdinalIgnoreCase)
                    : other.Value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
                QueryRequest found = ById(stored.Id);
                found.Search = term;
                QueryRequest inside = ById(stored.Id);
                inside.Search = fragment;
                QueryRequest none = ById(stored.Id);
                none.Search = "conformance-absent-" + Guid.NewGuid().ToString("N");

                Assert.True((await AsWriterAsync(service => service.QueryAsync(found, Token))).Rows.RowCount == 1, $"{Stack.Resource}: a {kind} search of {name} for '{term}' missed its row.");
                Assert.True(
                    (await AsWriterAsync(service => service.QueryAsync(inside, Token))).Rows.RowCount == (fragmentFinds ? 1 : 0),
                    $"{Stack.Resource}: a search for '{fragment}', from inside the {kind} {name} '{term}', {(fragmentFinds ? "missed" : "found")} the row.");
                Assert.True((await AsWriterAsync(service => service.QueryAsync(none, Token))).Rows.RowCount == 0, $"{Stack.Resource}: a search for a term no row holds found the row.");
            }
        }

        /// <summary>Every operation the case runs records its instruments within their closed tag sets, each with the outcome it had.</summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task Every_operation_records_its_instruments_within_their_closed_tag_sets()
        {
            StackConformanceCaller writer = await WriterAsync();
            TEntity stored = await CreateNewAsync();
            using CrudInstrumentCollector collector = new(Harness.Services.GetRequiredService<IMeterFactory>());

            // --- Each operation the stack serves, one answering and one refusing ---
            List<(string Operation, string Outcome)> expected = [];
            if (Serves(StackOperations.Query))
            {
                _ = await AsWriterAsync(service => service.QueryAsync(ById(stored.Id), Token));
                expected.Add(("query", "ok"));
            }

            if (Serves(StackOperations.Details))
            {
                _ = await AsWriterAsync(service => service.GetByIdAsync(stored.Id, DetailsRequest.None, Token));
                _ = await AttemptAsync(writer, service => service.GetByIdAsync(MissingId(), DetailsRequest.None, Token));
                expected.AddRange([("get", "ok"), ("get", "not_found")]);
            }

            if (Serves(StackOperations.Save))
            {
                TEntity loaded = await LoadAsync(stored.Id);
                Change(loaded, NextOrdinal());
                _ = await SaveAsync([loaded]);
                expected.Add(("save", "ok"));
            }

            if (Serves(StackOperations.Delete))
            {
                _ = await AttemptAsync(writer, service => service.DeleteByIdsAsync([MissingId()], null, Token));
                _ = await AsWriterAsync(service => service.DeleteByIdsAsync([stored.Id], null, Token));
                expected.AddRange([("delete", "not_found"), ("delete", "ok")]);
            }

            // --- Every measurement within its instrument's shape, and every operation counted with its outcome ---
            collector.AssertClosed();
            List<(string?, string?)> counted = [.. collector.Snapshot(CrudTelemetryNames.Operations).Select(static measurement => (measurement.Tag(CrudTelemetryNames.OperationTag), measurement.Tag(CrudTelemetryNames.OutcomeTag)))];
            List<(string?, string?)> timed = [.. collector.Snapshot(CrudTelemetryNames.OperationDuration).Select(static measurement => (measurement.Tag(CrudTelemetryNames.OperationTag), measurement.Tag(CrudTelemetryNames.OutcomeTag)))];
            Assert.All(expected, pair =>
            {
                Assert.Contains(pair, counted);
                Assert.Contains(pair, timed);
            });

            // --- A save counts its entities and its validation rounds ---
            if (Serves(StackOperations.Save))
            {
                Assert.True(
                    collector.Snapshot(CrudTelemetryNames.SaveEntities).Any(static measurement => measurement.Value >= 1 && measurement.Tag(CrudTelemetryNames.SourceTag) == "web"),
                    $"{Stack.Resource}: the save counted no entities: {string.Join("; ", collector.Snapshot(CrudTelemetryNames.SaveEntities))}");
                Assert.True(
                    collector.Snapshot(CrudTelemetryNames.ValidationRounds).Any(static measurement => measurement.Tag(CrudTelemetryNames.OperationTag) == "save"),
                    $"{Stack.Resource}: the save recorded no validation rounds: {string.Join("; ", collector.Snapshot(CrudTelemetryNames.ValidationRounds))}");
            }
        }
    }
}
