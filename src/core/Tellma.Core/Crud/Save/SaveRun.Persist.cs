// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
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
        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

        /// <summary>The code the post-check throws with, which the executor raises as a row-security failure.</summary>
        private const string RowSecurityCode = "RowSecurity";

        /// <summary>The wait a caller is told before reloading after a commit the database could not confirm.</summary>
        private static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>The security tables whose writes the access guards hold to their invariants.</summary>
        private static readonly TableName[] SecurityTables =
        [
            new("core", "Users"),
            new("core", "Roles"),
            new("core", "RoleMemberships"),
            new("core", "Permissions"),
        ];

        /// <summary>The <c>entity:*</c> dependencies the validation rounds were served under, by name, each as first served.</summary>
        private readonly Dictionary<string, VersionTagDependency> _served = new(StringComparer.Ordinal);

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
        private IReadOnlySet<TableName> StackTables => field ??= Tables(_root);

        /// <summary>Persists the validated payload, commits it, and runs the post-commit phase.</summary>
        /// <returns>The saved rows' ids, and the rows when asked for.</returns>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        private async Task<EntitiesResult<TEntity>> PersistAsync()
        {
            // --- The tenant-state verdict: a save is a mutation without AllowWhenReadOnly ---
            PersistVerdict.Ensure(_operation.Accessor.Current, mutation: true, allowWhenReadOnly: false, _operation.Frames.Open);

            // --- One Persist batch; a cause the validation rounds left is not this batch's ---
            _ = _operation.Recompositions.Take();
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

            // --- The commit: the response, then the post-commit phase on the committed rows ---
            _operation.Accessor.Current.ApplyCultures();
            SaveHandle handle = _handle ?? throw new InvalidOperationException("A committed persist composed the emitter's statement.");
            EntitiesResult<TEntity> result = _readBack?.Complete() ?? IdsOnly();
            await AfterCommitAsync(outcome, handle.Receipt.Value.Stamp).ConfigureAwait(false);
            return result;
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
            // --- A re-entry: counted, then the validators re-run in memory under the fresh user ---
            bool first = ++_entries == 1;
            if (!first)
            {
                if (_operation.Recompositions.Take() is RecomposeCause cause)
                {
                    _operation.Metrics.CountStaleRerun(cause);
                }

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
            if (_served.Count > 0)
            {
                batch.DependsOn([.. _served.Values]);
            }

            // --- Step 4, ahead of item 1: the access guards' lock, when the stack writes a security table ---
            bool guarded = !_operation.Accessor.Current.IsSystem && StackTables.Any(IsSecurityTable);
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

            // A participant only appends: a round trip of its own is refused before it is sent.
            using (ComposeFence.Close(static () => new InvalidOperationException(
                "A persist participant appends to the operation's batch and performs no I/O of its own. Load what it needs in the validation rounds, or append a read whose result it reads after the commit.")))
            {
                await _behavior.ContributeAsync(context).ConfigureAwait(false);
                foreach (IPersistEffect<TEntity> effect in _operation.Services.GetServices<IPersistEffect<TEntity>>())
                {
                    await effect.ContributeAsync(context).ConfigureAwait(false);
                }
            }

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
            VersionTagDependency[] moved = [.. _served.Values.Where(served => user.TenantTags.TryGetValue(served.Name, out Guid fresh) && fresh != served.ExpectedTag.Value)];
            if (moved.Length > 0)
            {
                throw new StaleContextException(moved);
            }

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
        // executor's probe found, committed. The read-back is re-issued as a plain Read, under the
        // caller's Read filter, and the post-commit phase proceeds, whatever the caller's token says.
        /// <summary>Reads back a save whose commit the probe proved, then runs its post-commit phase.</summary>
        /// <returns>The saved rows' ids, and the rows when asked for.</returns>
        private async Task<EntitiesResult<TEntity>> AfterProvenCommitAsync()
        {
            DetailsComposition<TEntity, TKey>? composition = null;
            TKey[] ids = FinalIds();
            BatchOutcome outcome = await _operation.Runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    composition = _operation.Details.Compose(_stack, _behavior, _options.Details, ids, idsSource: null, user, batch);
                    return ValueTask.CompletedTask;
                },
                static (_, outcome) => outcome,
                CancellationToken.None).ConfigureAwait(false);
            _operation.Accessor.Current.ApplyCultures();

            // The batch's stamp is the roots' modification time, which the rows read back carry.
            EntitiesResult<TEntity> read = composition!.Complete();
            DateTimeOffset stamp = default;
            foreach (object entity in read.Entities)
            {
                if (entity is TopLevelEntity<TKey> stamped && stamped.ModifiedAt > stamp)
                {
                    stamp = stamped.ModifiedAt;
                }
            }

            await AfterCommitAsync(outcome, stamp).ConfigureAwait(false);
            return _options.ReturnEntities ? read : IdsOnly();
        }

        // Spec 0015 §13.2: the hook, then every effect, after the commit is acknowledged and outside
        // any transaction. A failure is logged with the operation's trace id and counted by the
        // component's type name; it never changes the response and is never retried.
        /// <summary>Meters the committed save and runs its post-commit phase.</summary>
        /// <param name="committed">The committed batch's outcome.</param>
        /// <param name="stamp">The batch's stamp.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        private async Task AfterCommitAsync(BatchOutcome committed, DateTimeOffset stamp)
        {
            // --- The save's instruments ---
            _operation.Metrics.RecordSaveEntities(_rows.Count, CrudMetrics.Source(_options.Source), CrudMetrics.Lane(_rows.Count, _operation.LargeBatchThreshold));
            if (_options.Concurrency == ConcurrencyMode.Override && _roots.Exists(static root => !root.IsNew))
            {
                _operation.Metrics.CountOverride();
            }

            // --- The hook, then every effect, each failure contained ---
            SavePersistOutcome<TEntity> outcome = new()
            {
                Entities = _entities,
                Stamp = stamp,
                Context = _operation.Accessor.Current,
                VersionTags = committed.VersionTags,
                UserVersionTags = committed.UserVersionTags,
                Options = _options,
                BeforeImages = _context.BeforeImages,
            };
            await ContainedAsync(_behavior.GetType(), () => _behavior.AfterCommitAsync(outcome)).ConfigureAwait(false);
            foreach (IPersistEffect<TEntity> effect in _operation.Services.GetServices<IPersistEffect<TEntity>>())
            {
                await ContainedAsync(effect.GetType(), () => effect.AfterCommitAsync(outcome)).ConfigureAwait(false);
            }
        }

        /// <summary>Runs one post-commit participant, logging and counting its failure instead of raising it.</summary>
        /// <param name="component">The participant's type.</param>
        /// <param name="step">The participant's step.</param>
        /// <returns>A task that completes when the step returned or failed.</returns>
        private async Task ContainedAsync(Type component, Func<Task> step)
        {
            try
            {
                await step().ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // A post-commit failure never changes the response: the save committed.
                Failed(component, failure);
            }
        }

        /// <summary>Logs and counts a commit callback a participant registered that failed.</summary>
        /// <param name="callback">The callback.</param>
        /// <param name="failure">Its failure.</param>
        private void CallbackFailed(Action<BatchOutcome> callback, Exception failure)
        {
            // A lambda's method lives on a closure class nested in the component that wrote it.
            Type? component = callback.Method.DeclaringType;
            while (component is { IsNested: true } && component.Name.StartsWith('<'))
            {
                component = component.DeclaringType;
            }

            Failed(component ?? callback.GetType(), failure);
        }

        /// <summary>Logs and counts one post-commit failure.</summary>
        /// <param name="component">The failing component's type.</param>
        /// <param name="failure">The failure.</param>
        private void Failed(Type component, Exception failure)
        {
            _operation.Metrics.CountEffectFailure(component.Name);
            CrudLog.EffectFailed(_operation.Logger, component.Name, _stack.Resource, CrudOperationNames.Save, Activity.Current?.TraceId.ToString(), failure);
        }

        /// <summary>Records the <c>entity:*</c> dependencies a validation round's batch was served under, keeping each first served tag.</summary>
        /// <param name="batch">The round's batch, once composed.</param>
        private void Served(IDataBatch batch)
        {
            if (batch is not DataBatch data)
            {
                return;
            }

            foreach (VersionTagDependency dependency in data.Dependencies)
            {
                if (dependency.Name.StartsWith(VersionTagNames.EntityPrefix, StringComparison.Ordinal))
                {
                    _ = _served.TryAdd(dependency.Name, dependency with { OnMismatch = VersionTagMismatchPolicy.Rerun });
                }
            }
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
                _operation.Accessor.Current.ContentLanguageIndex);
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

        /// <summary>Sets the authority of the appender the batch is composed for next, on a batch that takes one.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="tables">The stack-owned tables the appender may write.</param>
        private static void Authorize(IDataBatch batch, IReadOnlySet<TableName> tables)
        {
            if (batch is IBatchAuthority authority)
            {
                authority.SetCallerAuthority(tables);
            }
        }

        /// <summary>Whether a table is one of the security tables the access guards hold to their invariants.</summary>
        /// <param name="table">The table.</param>
        /// <returns>True when it is.</returns>
        private static bool IsSecurityTable(TableName table)
        {
            return SecurityTables.Any(security =>
                string.Equals(security.Schema, table.Schema, StringComparison.OrdinalIgnoreCase) && string.Equals(security.Name, table.Name, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The tables of an entity and of every entity its collections hold, at every depth.</summary>
        /// <param name="root">The entity.</param>
        /// <returns>The tables.</returns>
        private static HashSet<TableName> Tables(EntityMetadata root)
        {
            HashSet<TableName> tables = [];
            Stack<EntityMetadata> pending = new([root]);
            while (pending.TryPop(out EntityMetadata? entity))
            {
                if (tables.Add(entity.Table))
                {
                    foreach (ChildCollectionMetadata collection in entity.Children)
                    {
                        pending.Push(collection.Child);
                    }
                }
            }

            return tables;
        }
    }
}
