// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Xunit;

namespace Tellma.Core.Testing.Crud
{
    // Spec 0015 §7.4 and §8.3: a row the caller's read grant hides answers exactly as a row that
    // does not exist, and a stale stamp, an override and a row deleted since it was read answer as
    // the concurrency rules say.
    /// <content>Hidden rows, stamps and missing rows.</content>
    public abstract partial class StackConformanceTests<TEntity, TKey>
    {
        /// <summary>
        ///     On every operation that names ids, a row the caller's read grant hides answers as a row
        ///     that does not exist: the same refusal, naming the id asked for, at the same round trips.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_row_the_read_grant_hides_answers_as_a_missing_row_on_every_operation_naming_ids()
        {
            Assert.SkipWhen(Stack.Capabilities.Contains(Cacheable), $"The stack {Stack.Resource} is cacheable, so its Read grant takes no filter that could hide a row.");
            Assert.SkipUnless(Serves(StackOperations.Details), $"The stack {Stack.Resource} serves no read by id.");

            // --- A row the caller's grants hide, and one they show; every grant of the stack that
            // takes a filter takes the same one, so the hidden row is no write's target either ---
            TEntity hidden = await CreateNewAsync();
            TEntity visible = await CreateNewAsync();
            StackConformanceCaller caller = await CallerAsync(filter: $"{Metadata.Key.Name} != {Text(hidden.Id)}");
            TEntity loaded = await LoadAsync(hidden.Id);

            // --- Each operation, given the id to name ---
            List<(string Name, Func<EntityService<TEntity, TKey>, TKey, Task> Call)> operations =
            [
                ("get", (service, id) => service.GetByIdAsync(id, DetailsRequest.None, Token)),
            ];

            if (Serves(StackOperations.Save))
            {
                operations.Add(("save", (service, id) => SaveAs(service, loaded, id)));
            }

            if (Serves(StackOperations.Delete))
            {
                operations.Add(("delete", (service, id) => service.DeleteByIdsAsync([id], null, Token)));
            }

            if (Serves(StackOperations.Delete, Tree))
            {
                operations.Add(("delete_with_descendants", (service, id) => service.Pipeline.DeleteWithDescendantsAsync([id], Token)));
            }

            if (ServesActivation())
            {
                operations.Add(("activate", (service, id) => service.ExecuteActionAsync(ActivateAction, [id], null, new ActionOptions(), Token)));
                operations.Add(("deactivate", (service, id) => service.ExecuteActionAsync(DeactivateAction, [id], null, new ActionOptions(), Token)));
            }

            // --- A read by ids answers the rows it finds, in request order: the visible row alone ---
            foreach (TKey id in (TKey[])[hidden.Id, MissingId()])
            {
                EntitiesResult<TEntity> read = (await CountedAsync(caller, scope => Service(scope).GetByIdsAsync([visible.Id, id], DetailsRequest.None, Token))).Result;
                Assert.True(read.Ids.SequenceEqual([long.CreateChecked(visible.Id)]), $"{Stack.Resource}: a read by ids of a visible row and {Text(id)} answered {string.Join(",", read.Ids)}.");
            }

            // --- Every other operation: the hidden id and a missing one, the same refusal, the id aside ---
            foreach ((string name, Func<EntityService<TEntity, TKey>, TKey, Task> call) in operations)
            {
                TKey missing = MissingId();
                Counted<Exception?> onHidden = await AttemptAsync(caller, service => call(service, hidden.Id));
                Counted<Exception?> onMissing = await AttemptAsync(caller, service => call(service, missing));

                Assert.True(onHidden.Result is NotFoundException, $"{Stack.Resource}: {name} of a hidden row was answered {onHidden.Result?.GetType().Name ?? "with a result"} rather than as missing.");
                var refusal = (NotFoundException)onHidden.Result!;
                Assert.True(refusal.Resource == Stack.Resource && refusal.Ids.SequenceEqual([Text(hidden.Id)]), $"{Stack.Resource}: {name} of a hidden row named {refusal.Resource} {string.Join(",", refusal.Ids)}.");
                Assert.Equal(Answer(onMissing, missing), Answer(onHidden, hidden.Id));
            }

            // --- The hidden row is untouched ---
            Assert.True(await ExistsAsync(hidden.Id, includeInactive: false), $"{Stack.Resource}: a write refused as missing changed the hidden row.");

            // A refusal as text, the id it names replaced, and the round trips it took.
            static string Answer(Counted<Exception?> attempt, TKey id)
            {
                string text = attempt.Result switch
                {
                    null => "answered",
                    NotFoundException missing => $"NotFound {missing.Resource} [{string.Join(",", missing.Ids)}]",
                    ValidationException invalid => $"Validation [{string.Join(", ", Lines(invalid))}]",
                    Exception other => other.GetType().Name,
                };
                return $"{text.Replace(Text(id), "#", StringComparison.Ordinal)} after {attempt.RoundTrips} round trips";
            }
        }

        /// <summary>
        ///     A save whose stamp another save moved past is a conflict naming its modifier; the same
        ///     save under the override commits; a row deleted since it was read is not found under
        ///     either mode; and an update without a stamp is refused.
        /// </summary>
        /// <returns>A task that completes when the case ran.</returns>
        [Fact]
        public async Task A_stale_stamp_is_a_conflict_an_override_saves_past_it_and_a_deleted_row_is_not_found_under_both_modes()
        {
            Assert.SkipUnless(Serves(StackOperations.Save), $"The stack {Stack.Resource} serves no save.");
            Assert.SkipUnless(Metadata.IsTopLevel, $"The stack {Stack.Resource}'s rows carry no stamp.");
            StackConformanceCaller writer = await WriterAsync();

            // --- Two reads of one row; the first saves, so the second's stamp is stale ---
            TEntity stored = await CreateNewAsync();
            TEntity first = await LoadAsync(stored.Id);
            TEntity second = await LoadAsync(stored.Id);
            Change(first, NextOrdinal());
            _ = await SaveAsync([first]);
            Change(second, NextOrdinal());

            ConcurrencyException conflict = await Assert.ThrowsAsync<ConcurrencyException>(() => SaveAsync([second]));
            ConcurrencyConflict only = Assert.Single(conflict.Conflicts);
            Assert.Equal((long.CreateChecked(stored.Id), writer.UserId, false), (only.Id, only.ModifiedById, only.IsMissing));
            Assert.False(string.IsNullOrEmpty(only.ModifiedByName), $"{Stack.Resource}: the conflict names no modifier.");
            Assert.False(string.IsNullOrEmpty(only.ModifiedAt), $"{Stack.Resource}: the conflict names no stamp.");

            // --- The override saves past the stale stamp ---
            TEntity overridden = Assert.Single((await SaveAsync([second], ConcurrencyMode.Override)).Entities);
            Assert.True(StampOf(overridden) > StampOf(first), $"{Stack.Resource}: the override did not move the row's stamp.");

            // --- An update that sends no stamp is refused under the check ---
            TEntity unstamped = await LoadAsync(stored.Id);
            Change(unstamped, NextOrdinal());
            Property(StampProperty).Setter(unstamped, default(DateTimeOffset));
            ValidationException refused = await Assert.ThrowsAsync<ValidationException>(() => SaveAsync([unstamped]));
            Assert.Contains($"[0].{StampProperty} {ValidationCodes.ConcurrencyStampRequired}", Lines(refused));

            // --- A row deleted since it was read: not found, under the check and the override alike ---
            if (Serves(StackOperations.Delete))
            {
                TEntity doomed = await CreateNewAsync();
                TEntity read = await LoadAsync(doomed.Id);
                _ = await AsWriterAsync(service => service.DeleteByIdsAsync([doomed.Id], null, Token));
                Change(read, NextOrdinal());
                foreach (ConcurrencyMode mode in (ConcurrencyMode[])[ConcurrencyMode.Check, ConcurrencyMode.Override])
                {
                    NotFoundException missing = await Assert.ThrowsAsync<NotFoundException>(() => SaveAsync([read], mode));
                    Assert.Equal((Stack.Resource, Text(doomed.Id)), (missing.Resource, Assert.Single(missing.Ids)));
                }
            }
        }

        /// <summary>Saves a loaded row as the row of another id.</summary>
        /// <param name="service">The stack's service.</param>
        /// <param name="loaded">The row, as loaded.</param>
        /// <param name="id">The id to save it under.</param>
        /// <returns>The rows read back.</returns>
        private static Task<EntitiesResult<TEntity>> SaveAs(EntityService<TEntity, TKey> service, TEntity loaded, TKey id)
        {
            loaded.Id = id;
            return service.SaveAsync([loaded], new SaveOptions(), Token);
        }

        /// <summary>The stamp of a stored root.</summary>
        /// <param name="entity">The root.</param>
        /// <returns>The stamp.</returns>
        private DateTimeOffset StampOf(TEntity entity)
        {
            return (DateTimeOffset)Property(StampProperty).Getter(entity)!;
        }
    }
}
