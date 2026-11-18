// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §6.1 steps 7 to 10: the tenant-state verdict (§5.1), then one Persist batch through the
    // runner, composed in §6.7's order between the executor's fixed prefix and suffix: the access
    // guards' lock, the emitter, the service hook and the effects through the recording view, the
    // caller's OnPersist, the guards' invariants, the post-check of §9.3 and the read-back of §6.9.
    // A re-entered compose re-runs the validators in memory, recomposes the pipeline's own statements
    // under the fresh grants and replays the recording (§5.1). The executor's failures are
    // translated by §14.2, and a committed save runs its post-commit phase (§13.2) whatever the
    // caller's token says, since a sent persist completes (§14.1).
    /// <summary>One save of one stack: the persist, its translation and its post-commit phase.</summary>
    internal sealed partial class SaveRun<TEntity, TKey>
    {
        /// <summary>The times the runner entered the persist's compose.</summary>
        private int _entries;

        /// <summary>The emitter's handle of the first entry, whose names the participants hold.</summary>
        private SaveHandle? _firstHandle;

        /// <summary>The emitter's handle of the batch composed last.</summary>
        private SaveHandle? _handle;

        /// <summary>The recording of the service hook's and the effects' appends.</summary>
        private RecordingBatch? _participants;

        /// <summary>The recording of the caller's appends; null when the save carries no <see cref="SaveOptions.OnPersist" />.</summary>
        private RecordingBatch? _callerAppends;

        /// <summary>The read-back composed last; null without <see cref="SaveOptions.ReturnEntities" />.</summary>
        private DetailsComposition<TEntity, TKey>? _readBack;

        /// <summary>The tables of the stack: the root's and every child collection's, at every depth.</summary>
        private IReadOnlySet<TableName> StackTables => field ??= StackTableSet.Of(_root);

        /// <summary>Persists the validated payload, commits it, and runs the post-commit phase.</summary>
        /// <returns>The saved rows' ids, and the rows when asked for.</returns>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        private async Task<EntitiesResult<TEntity>> PersistAsync()
        {
            // --- The tenant-state verdict: a save is a mutation without AllowWhenReadOnly ---
            PersistVerdict.Ensure(_operation.Accessor.Current, mutation: true, allowWhenReadOnly: false, _operation.Frames.Open);

            // --- One Persist batch ---
            BatchOutcome outcome;
            try
            {
                outcome = await _operation.Runner.RunAsync(BatchPurpose.Persist, ComposePersistAsync, static (_, outcome) => outcome, _cancellationToken).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found the inserted rows: the save committed, its response lost with the connection.
                return await AfterProvenCommitAsync().ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous)
            {
                // An update-only save cannot be probed: the caller reloads to learn whether it committed.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, ambiguous);
            }
            catch (DataAccessException failure)
            {
                // The runner may have refreshed the context in its own frame: the conflict names its
                // modifier in the culture of the context the batch was composed under.
                _operation.Accessor.Current.ApplyCultures();
                if (PersistFailures.Translate(failure, FailureContext(), _operation.Metrics) is Exception translated)
                {
                    throw translated;
                }

                throw;
            }

            // --- The commit: the post-commit phase on the committed rows, then the response, whose
            // assembly runs contributor code that may fail after the commit ---
            _operation.Accessor.Current.ApplyCultures();
            SaveHandle handle = _handle ?? throw new InvalidOperationException("A committed persist composed the emitter's statement.");
            await AfterCommitAsync(outcome, handle.Receipt.Value.Stamp).ConfigureAwait(false);
            return _readBack?.Complete() ?? IdsOnly();
        }

        // Spec 0015 §6.7: the fixed prefix and suffix are the executor's; between them the pipeline
        // appends its own statements and the participants' recorded ones, in this order on every
        // entry. Compose performs no I/O, and the participants it awaits perform none either.
        /// <summary>Composes the persist batch: on the first entry calling the participants, on a re-entry replaying them.</summary>
        /// <param name="user">The connected user the batch is composed for.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>A task that completes when the batch is composed.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants a save of the stack.</exception>
        private async ValueTask ComposePersistAsync(ConnectedUser user, IDataBatch batch)
        {
            // --- A re-entry, which the operation's frame counts: the validators re-run in memory under the fresh user ---
            bool first = ++_entries == 1;
            if (!first)
            {
                await RevalidateAsync(user).ConfigureAwait(false);
            }

            // --- The save grant, decided for the connected user ---
            AccessDecision save = Decide(user, _stack.Resource, AccessActions.Save);
            if (save.Outcome == AccessOutcome.Denied)
            {
                throw new ForbiddenException(ForbiddenCode, save.Resource, save.Action, ForbiddenReason.NoGrant);
            }

            batch.Limits = QueryexProfiles.PipelineLimits;
            IReadOnlySet<TableName> callers = batch.CallerAuthority;

            // --- The tags the validation rounds were served under, held by the persist's guard ---
            DependOnServed(batch);

            // --- Step 4, ahead of item 1: the access guards' lock, when the stack writes a security table ---
            bool guarded = !_operation.Accessor.Current.IsSystem && StackTableSet.WritesSecurityTable(StackTables);
            if (guarded)
            {
                _operation.AccessGuards.ContributeLock(batch);
            }

            // --- Step 1: the emitter, under the stack's authority ---
            Authorize(batch, StackTables);
            SaveHandle handle = batch.Save(_entities, _options.Concurrency);
            _handle = handle;

            // --- Steps 2 and 3, then OnPersist: called on the first entry, replayed on a re-entry ---
            if (first)
            {
                _firstHandle = handle;
                await ContributeAsync(batch, handle).ConfigureAwait(false);
                if (_options.OnPersist is Action<IDataBatch> onPersist)
                {
                    // The caller's statements, under the caller's authority, never the stack's.
                    Authorize(batch, callers);
                    _callerAppends = new RecordingBatch(batch, CallbackFailed);
                    onPersist(_callerAppends);
                    _callerAppends.Seal();
                }
            }
            else
            {
                EnsureSameNames(handle);
                _participants!.Replay(batch);
                if (_callerAppends is RecordingBatch appends)
                {
                    Authorize(batch, callers);
                    appends.Replay(batch);
                }
            }

            // --- Step 4: the guards' invariants, after every contribution ---
            Authorize(batch, callers);
            if (guarded)
            {
                _operation.AccessGuards.ContributeInvariants(batch);
            }

            // --- Step 5: the post-check over the saved ids, under a filtered grant ---
            if (save.Outcome == AccessOutcome.Filtered)
            {
                EntityDescriptor root = _map.Descriptor(_root);
                string key = root.Key.Name;
                batch.Assert(
                    new QuerySpec { Root = root, Select = key, Filter = save.Filter, Restrictions = [new KeySetRestriction(key, handle.SavedIds.Name)] },
                    arguments: null,
                    expected: _roots.Count,
                    TellmaSqlErrors.RowSecurity,
                    RowSecurityCode);
            }

            // --- Step 6: the read-back inside the transaction, over the saved ids ---
            _readBack = _options.ReturnEntities
                ? _operation.Details.Compose(_stack, _behavior, _options.Details, FinalIds(), handle.SavedIds, user, batch)
                : null;
        }

        /// <summary>Calls the service hook, then every effect in registration order, through the recording view.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="handle">The emitter's handle.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        private async Task ContributeAsync(IDataBatch batch, SaveHandle handle)
        {
            _participants = new RecordingBatch(batch, CallbackFailed);
            SavePersistContext<TEntity> context = new()
            {
                Entities = _entities,
                Context = _operation.Accessor.Current,
                Batch = _participants,
                AffectedIds = handle.SavedIds,
                Captures = handle.Captures,
                Options = _options,
                NewIds = handle.NewIds,
                TouchedIds = handle.TouchedIds,
                BeforeImages = _context.BeforeImages,
                Resource = _stack.Resource,
                Notifier = _operation.Services.GetService<INotifier>(),
                AssignmentsService = _operation.Services.GetService<IAssignments>(),
            };

            await ContributeAsync(_behavior, context).ConfigureAwait(false);
            _participants.Seal();
        }

        // Spec 0015 §5.1 step 1 of a re-entry: the service hook and every validator run again over the
        // hydrated context and the fresh user, the loader answering only from the rounds already run,
        // and no round trip leaving the process. Nothing they raised before is raised again, so a new
        // error or a new unconfirmed warning is what refuses the persist; the confirmations are neither
        // counted nor recorded twice.
        /// <summary>Re-runs the validators in memory for the fresh connected user.</summary>
        /// <param name="user">The fresh connected user.</param>
        /// <returns>A task that completes when every validator returned.</returns>
        /// <exception cref="StaleContextException">A tag the rounds were served under moved, or a validator asked for a load the rounds did not serve.</exception>
        /// <exception cref="ValidationException">A validator reported an error.</exception>
        /// <exception cref="ConfirmationRequiredException">A validator raised an unconfirmed warning.</exception>
        private async Task RevalidateAsync(ConnectedUser user)
        {
            // --- A served tag the fresh snapshot moved past: what the validators read is stale ---
            EnsureServedTagsHold(user);

            // --- The hydrated context, its errors and frame fresh, its loader answering from the rounds run ---
            Composer = user;
            _loader.AnswerOnly();
            using ConfirmationFrame frame = _operation.Confirmations.Frame(_stack.Resource, CrudOperationNames.Save, audited: false);
            ValidationErrors<TEntity> errors = new();
            ValidationScheduler scheduler = new(
                _stack.Limits.MaxValidationRounds,
                CrudOperationNames.Save,
                static (_, _) => throw new InvalidOperationException("A re-run of the validators answers from the rounds already run and dispatches none."),
                metrics: null,
                _cancellationToken);
            SaveContext<TEntity> context = new()
            {
                Entities = _entities,
                Options = _options,
                Context = _operation.Accessor.Current,
                Loader = _loader,
                Errors = errors,
                Host = new PipelineWriteHost(_stack.Resource, CrudOperationNames.Save, frame),
                CancellationToken = _cancellationToken,
                NewRows = _context.NewRows,
                BeforeImages = _context.BeforeImages,
                Round = _context.Round,
            };

            // --- The validators, with every round trip refused as stale ---
            using (ComposeFence.Close(static () => new StaleContextException([])))
            {
                scheduler.Run(_behavior.GetType().Name, () => _behavior.ValidateAsync(context));
                foreach (IEntityValidator<TEntity> validator in _operation.Services.GetServices<IEntityValidator<TEntity>>())
                {
                    scheduler.Run(validator.GetType().Name, () => validator.ValidateAsync(context));
                }

                _ = await scheduler.RunAsync().ConfigureAwait(false);
            }

            ValidationOutcome.Raise(staleStamp: null, errors, frame);
        }

        // Spec 0015 §8: a save whose response was lost around COMMIT, and whose inserted rows the
        // executor's probe found, committed. A plain Read re-issues the read-back over the final ids,
        // as the one inside the transaction reads the saved ids, so every id is answered whatever the
        // caller's Read filter admits, and reads the roots' stamp with it. A failure of that read
        // never answers the committed save with an error a caller might retry, writing its rows
        // again: it is logged, and the save answers its ids alone. The post-commit phase runs either
        // way, and whatever the caller's token says. The batch's commit callbacks do not run: the
        // executor never read the lost batch's results.
        /// <summary>Reads back a save whose commit the probe proved, then runs its post-commit phase.</summary>
        /// <returns>The saved rows' ids, and the rows when asked for and the read answered.</returns>
        private async Task<EntitiesResult<TEntity>> AfterProvenCommitAsync()
        {
            TKey[] ids = FinalIds();
            PropertyMetadata? stampProperty = _roots.Count > 0 ? _roots[0].Rules.Stamp : null;
            DetailsComposition<TEntity, TKey>? composition = null;
            BatchResult<EntityQueryResult<TEntity>>? stamps = null;
            BatchOutcome? outcome = null;
            DateTimeOffset stamp = default;
            bool answered = false;
            try
            {
                outcome = await _operation.Runner.RunAsync(
                    BatchPurpose.Read,
                    (user, batch) =>
                    {
                        // --- The final ids, the roots' stamp over them, and the details when asked for ---
                        batch.Limits = QueryexProfiles.PipelineLimits;
                        SqlIdentifier saved = KeyTables.Bind(batch, ids);
                        stamps = stampProperty is null
                            ? null
                            : batch.Query(new EntityQuery<TEntity> { Select = stampProperty.Name, Restrictions = [new KeySetRestriction(_map.Descriptor(_root).Key.Name, saved.Name)] });
                        composition = _options.ReturnEntities
                            ? _operation.Details.Compose(_stack, _behavior, _options.Details, ids, saved, user, batch)
                            : null;
                        return ValueTask.CompletedTask;
                    },
                    static (_, outcome) => outcome,
                    CancellationToken.None).ConfigureAwait(false);
                _operation.Accessor.Current.ApplyCultures();

                // One batch stamped every root it wrote; a later save of one of them moves its stamp past.
                foreach (TEntity entity in stamps?.Value.Entities ?? [])
                {
                    if ((DateTimeOffset?)stampProperty!.Getter(entity) is DateTimeOffset modified && modified > stamp)
                    {
                        stamp = modified;
                    }
                }

                answered = true;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The save committed: the ids answer it, and the post-commit phase runs on the default stamp.
                ProvenCommitReadFailed(failure);
            }

            await AfterCommitAsync(outcome, stamp).ConfigureAwait(false);
            return answered && composition is not null ? composition.Complete() : IdsOnly();
        }

        // Spec 0015 §13.2: the hook, then every effect, after the commit is acknowledged and outside
        // any transaction. A failure is logged with the operation's trace id and counted by the
        // component's type name; it never changes the response and is never retried.
        /// <summary>Meters the committed save and runs its post-commit phase.</summary>
        /// <param name="committed">The outcome of the batch that committed, or of the read that proved it; null when neither answered.</param>
        /// <param name="stamp">The batch's stamp; the default when no read answered it.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        private async Task AfterCommitAsync(BatchOutcome? committed, DateTimeOffset stamp)
        {
            // --- The save's instruments: the rows at every depth, and an override of a change the caller had not seen ---
            _operation.Metrics.RecordSaveEntities(_rows.Count, CrudMetrics.Source(_options.Source), CrudMetrics.Lane(_rows.Count, _operation.LargeBatchThreshold));
            if (_options.Concurrency == ConcurrencyMode.Override && _roots.Exists(Overrode))
            {
                _operation.Metrics.CountOverride();
            }

            // Without an answer, the tags are this instance's last snapshot of the tenant's.
            VersionTagSnapshot tags = TagsAfter(committed);

            // --- The hook, then every effect, each failure contained ---
            SavePersistOutcome<TEntity> outcome = new()
            {
                Entities = _entities,
                Stamp = stamp,
                Context = _operation.Accessor.Current,
                VersionTags = tags,
                UserVersionTags = committed?.UserVersionTags,
                Options = _options,
                BeforeImages = _context.BeforeImages,
            };
            await AfterCommitAsync(_behavior, outcome).ConfigureAwait(false);
        }

        /// <summary>Whether an update root overwrote a stored row whose stamp differed from the one the caller echoed.</summary>
        /// <param name="root">The root.</param>
        /// <returns>True when it did.</returns>
        private bool Overrode(SaveRow root)
        {
            return !root.IsNew
                && root.Rules.Stamp is PropertyMetadata stamp
                && root.Before is object stored
                && (DateTimeOffset?)stamp.Getter(stored) != _stamps[root.RootIndex];
        }

        /// <summary>Refuses a recomposed emitter whose batch-local names differ from those the participants hold.</summary>
        /// <param name="handle">The recomposed emitter's handle.</param>
        /// <exception cref="InvalidOperationException">A name differs.</exception>
        private void EnsureSameNames(SaveHandle handle)
        {
            SaveHandle first = _firstHandle ?? throw new InvalidOperationException("A re-entered persist follows a first entry that composed the emitter.");
            if (first.SavedIds != handle.SavedIds || first.NewIds != handle.NewIds || first.TouchedIds != handle.TouchedIds || !first.Captures.SequenceEqual(handle.Captures))
            {
                throw new InvalidOperationException(
                    $"The recomposed persist batch named the saved ids '{handle.SavedIds.Name}' where its first entry named '{first.SavedIds.Name}', so the participants' recorded statements would address the wrong tables. A persist's compose appends the same statements in the same order on every entry.");
            }
        }

        /// <summary>What the operation knows about its persist, for the translation of its failures.</summary>
        /// <returns>The context.</returns>
        private PersistFailureContext FailureContext()
        {
            return new PersistFailureContext(
                _stack.Resource,
                AccessActions.Save,
                _root,
                [.. _rows.Select(static row => new PersistRow(row.Entity, row.Row, row.Path))],
                _operation.Accessor.Current.ContentLanguageIndex)
            {
                Model = _operation.Services.GetRequiredService<IEntityMetadataProvider>().All,
                Drops = [.. _drops],
            };
        }

        /// <summary>The payload roots' final ids, in payload order, each once.</summary>
        /// <returns>The ids.</returns>
        private TKey[] FinalIds()
        {
            return [.. _entities.Select(static entity => entity.Id).Distinct()];
        }

        /// <summary>The result of a save that returns no rows: the final ids in payload order.</summary>
        /// <returns>The result.</returns>
        private EntitiesResult<TEntity> IdsOnly()
        {
            return new EntitiesResult<TEntity>
            {
                Ids = [.. _entities.Select(static entity => long.CreateChecked(entity.Id))],
                Entities = [],
            };
        }
    }
}
