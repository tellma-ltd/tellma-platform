// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Actions
{
    // Spec 0015 §11.1 and §11.2 step 4: one Persist batch through the runner. Between the executor's
    // fixed prefix and suffix it carries the access guards' lock (a transaction-head statement,
    // appended beside the invariants once every write is declared), the emitter for the rows Save
    // marked (ConcurrencyMode.Override: an action is a command over rows it just loaded), the
    // updates over the target ids under the caller's Read filter and the action grant, all or
    // nothing (the built-in action's own, then those Update composed; an update composed with a
    // condition instead skips a target that no longer meets it), the statements the hook, the
    // components and the method appended, the participants over an ActionPersistContext, the guards'
    // invariants, the post-check when the action asks for it, and the read-back when the action
    // answers the target ids. A re-entered compose re-runs the validators in memory, recomposes the
    // pipeline's own statements under the fresh grants and replays the rest (§5.1). The groups the
    // tasks enlisted (§13.3) sit around the action's statements: the BeforeHost ones ahead of the
    // stack's own writes, the AfterHost ones after its participants, and the access guards join
    // once the batch is composed, over every table any statement of it writes. The method's Persisted completes once the batch committed,
    // before the post-commit phase (§13.2), whose groups' part follows the action's own.
    /// <summary>One action of one stack: the persist, its translation and its post-commit phase.</summary>
    internal sealed partial class ActionRun<TEntity, TKey>
    {
        /// <summary>The rows the context's Save marked, each once, in marking order.</summary>
        private TEntity[] _marked = [];

        /// <summary>The updates of the target ids: the built-in action's own, then those Update composed, each with its condition.</summary>
        private List<ComposedUpdate> _updates = [];

        /// <summary>The times the runner entered the persist's compose.</summary>
        private int _entries;

        /// <summary>The names the first entry declared, which the participants hold: the emitter's saved ids and captures, and the target ids' table.</summary>
        private (SqlIdentifier? Saved, IReadOnlyList<ColumnCapture> Captures, SqlIdentifier Affected)? _firstNames;

        /// <summary>The recording of the participants' appends.</summary>
        private RecordingBatch? _participants;

        /// <summary>The read-back composed last; null when the action answers no rows.</summary>
        private DetailsComposition<TEntity, TKey>? _readBack;

        /// <summary>The emitter's handle of the entry composed last; null when no row was marked.</summary>
        private SaveHandle? _saved;

        /// <summary>Takes what the validators and the method composed, and the built-in action's own update.</summary>
        /// <param name="context">The context they composed on; null on the one-round-trip path.</param>
        private void Compose(ActionContext<TEntity, TKey>? context)
        {
            List<ComposedUpdate> updates = [];
            if (_descriptor.IsBuiltIn)
            {
                updates.Add(new ComposedUpdate(BuiltInAssignment(), Condition: null));
            }

            if (context is not null)
            {
                updates.AddRange(context.Updates);
                _marked = [.. context.SavedRows.Distinct(ReferenceEqualityComparer.Instance).Cast<TEntity>()];
            }

            _updates = updates;
        }

        /// <summary>Persists the action, commits it, and answers.</summary>
        /// <returns>The method's own result, or the target ids and, when asked, the rows read back.</returns>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        private async Task<object?> PersistAsync()
        {
            // --- The one-round-trip path composes the built-in update alone ---
            if (_context is null)
            {
                Compose(context: null);
            }

            // --- The tenant-state verdict, as the action declares it ---
            PersistVerdict.Ensure(Services.Accessor.Current, _descriptor.Mutation, _descriptor.AllowWhenReadOnly, Services.Frames.Open);

            // --- One Persist batch ---
            BatchOutcome outcome;
            try
            {
                outcome = await Services.Runner.RunAsync(BatchPurpose.Persist, ComposePersistAsync, static (_, outcome) => outcome, _cancellationToken).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // The probe found a row the batch inserted: the action committed, its response lost with the connection.
                return await AfterProvenCommitAsync().ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous)
            {
                // An update cannot be probed: the caller reloads to learn whether it committed.
                throw DataAccessTranslation.Unavailable(ambiguous);
            }
            catch (DataAccessException failure)
            {
                // The runner may have refreshed the context in its own frame: the conflict names its
                // modifier in the culture of the context the batch was composed under. A failure of a
                // statement a group composed is the group's.
                Services.Accessor.Current.ApplyCultures();
                if (Enlistments?.Translate(failure) is Exception grouped)
                {
                    if (ReferenceEquals(grouped, failure))
                    {
                        throw;
                    }

                    throw grouped;
                }

                if (PersistFailures.Translate(failure, FailureContext(), Services.Metrics) is Exception translated)
                {
                    throw translated;
                }

                throw;
            }

            // --- The commit: the method resumes, the post-commit phase runs, then the answer ---
            Services.Accessor.Current.ApplyCultures();
            ExceptionDispatchInfo? failed = await ResumeMethodAsync().ConfigureAwait(false);
            await ResumeReleasedAsync().ConfigureAwait(false);
            await AfterPersistAsync(outcome).ConfigureAwait(false);
            RaiseAfterCommit(failed);
            _partial?.Throw();
            return OwnResult ? _result : _readBack?.Complete() ?? IdsOnly();
        }

        // Spec 0015 §8's rule for a committed write: once the action committed, nothing answers it
        // with an error a caller might retry into a second commit. A method answering no value of its
        // own that fails after the commit is logged, and the action answers its ids; a method
        // answering a value has no other answer to give, so its failure is raised. A write a task
        // composed after reading Persisted, and the persist committed without, is an authoring bug
        // and never answered as success: it is logged and raised for every action.
        /// <summary>Raises, or logs, what failed after the action committed.</summary>
        /// <param name="failed">The method's failure after the commit; null when it returned.</param>
        private void RaiseAfterCommit(ExceptionDispatchInfo? failed)
        {
            // --- A write the persist committed without ---
            string? traceId = Activity.Current?.TraceId.ToString();
            if (ComposedLate() is Exception late)
            {
                CrudLog.ActionComposedAfterPersisted(Services.Logger, Resource, OperationName, traceId, late);
                if (failed is not null && !ReferenceEquals(failed.SourceException, late))
                {
                    CrudLog.ActionMethodFailedAfterCommit(Services.Logger, _method?.Name ?? _descriptor.Name, Resource, OperationName, traceId, failed.SourceException);
                }

                ExceptionDispatchInfo.Throw(late);
            }

            // --- The method's own failure ---
            if (failed is null)
            {
                return;
            }

            if (OwnResult)
            {
                failed.Throw();
            }

            CrudLog.ActionMethodFailedAfterCommit(Services.Logger, _method?.Name ?? _descriptor.Name, Resource, OperationName, traceId, failed.SourceException);
        }

        // Spec 0015 §11.2 step 4, in this order on every entry; compose performs no I/O, and the
        // participants it awaits perform none either.
        /// <summary>Composes the persist batch: on the first entry calling the participants, on a re-entry replaying them.</summary>
        /// <param name="user">The connected user the batch is composed for.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>A task that completes when the batch is composed.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants the action.</exception>
        /// <exception cref="NotFoundException">The connected user reads no row of the stack, so every target is hidden.</exception>
        /// <exception cref="StaleContextException">On a re-entry, what the validators read moved, or the permission set moved under a group they validated.</exception>
        private async ValueTask ComposePersistAsync(ConnectedUser user, IDataBatch batch)
        {
            // --- Every entry is abandoned unsent when a task composed after reading Persisted, a flow
            // that left the rounds failed, or a task reported an error once the rounds ended ---
            ThrowIfReleasedFlowFailed();
            if (_context is not null && _errors.HasErrors)
            {
                throw new ValidationException(_errors.Items);
            }

            // --- A re-entry, which the operation's frame counts: the validators re-run in memory under
            // the fresh user ---
            bool first = ++_entries == 1;
            if (!first && _context is not null)
            {
                await RevalidateAsync(user).ConfigureAwait(false);
            }

            // --- The action grant, decided for the connected user ---
            Composer = user;
            AccessDecision granted = Granted(user, _descriptor.Action);

            // --- A re-entry for a moved permission set never commits groups validated under the old one ---
            EnsureGroupsJudgedUnder(user, Enlistments);
            batch.Limits = QueryexProfiles.PipelineLimits;
            IReadOnlySet<TableName> callers = batch.CallerAuthority;
            DependOnServed(batch);

            // --- The groups enlisted ahead of the action, then the stack's own writes, under its
            // authority: the emitter, the target ids, the updates ---
            await ComposeEnlistedAsync(batch, EnlistOrder.BeforeHost, first).ConfigureAwait(false);
            Authorize(batch, StackTables);
            SaveHandle? saved = _marked.Length > 0 ? batch.Save(_marked, ConcurrencyMode.Override) : null;
            _saved = saved;
            SqlIdentifier affected = KeyTables.Bind(batch, _ids);
            ComposeUpdates(batch, user, granted, affected);

            // --- The statements the hook, the components and the method appended, in their order ---
            _recording?.Replay(batch);

            // --- The participants: called on the first entry, replayed on a re-entry ---
            if (first)
            {
                // The emitter's captures of the marked rows reach the participants, so a blob column an
                // action writes is released and confirmed as a save's is (spec 0017 §4.1).
                IReadOnlyList<ColumnCapture> captures = saved?.Captures ?? [];
                _firstNames = (saved?.SavedIds, captures, affected);
                _participants = new RecordingBatch(batch, CallbackFailed);
                await ContributeAsync(_behavior, PersistContext(_participants, affected, _targets, captures)).ConfigureAwait(false);
                _participants.Seal();
            }
            else
            {
                EnsureSameNames(saved, affected);
                _participants!.Replay(batch);
            }

            // --- The groups enlisted after the action ---
            await ComposeEnlistedAsync(batch, EnlistOrder.AfterHost, first).ConfigureAwait(false);

            // --- The access guards' lock and invariants, after every contribution, when a write meets a
            // security table: decided over the stack's tables and every statement's declared writes, a
            // group's, a participant's or the caller's raw ones included (spec 0014 §8.3); the lock is
            // a transaction-head statement, written first in the transaction ---
            Authorize(batch, callers);
            _ = PersistGuards.Contribute(Services.AccessGuards, Services.Accessor.Current, batch, StackTables);

            // --- The post-check over the marked and the updated ids, when the action asks for it ---
            if (_descriptor.PostCheck && granted.Outcome == AccessOutcome.Filtered)
            {
                ComposePostCheck(batch, granted, saved, affected);
            }

            // --- The read-back, when the action answers the target ids ---
            _readBack = _options.ReturnEntities && !OwnResult
                ? Services.Details.Compose(_stack, _behavior, _options.Details ?? DetailsRequest.None, _ids, affected, user, batch)
                : null;
        }

        // Spec 0015 §11.1: the update counts every id under the caller's Read filter (50404, hidden
        // equals missing), then under the action grant (50403), before it writes; rows already holding
        // the values are skipped, unstamped. A caller who reads no row of the stack finds no target.
        /// <summary>Composes the updates of the target ids.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="user">The connected user.</param>
        /// <param name="granted">The action's decision.</param>
        /// <param name="affected">The target ids' table.</param>
        /// <exception cref="NotFoundException">The connected user reads no row of the stack.</exception>
        private void ComposeUpdates(IDataBatch batch, ConnectedUser user, AccessDecision granted, SqlIdentifier affected)
        {
            if (_updates.Count == 0)
            {
                return;
            }

            AccessDecision read = Decide(user, Resource, AccessActions.Read);
            if (read.Outcome == AccessOutcome.Denied)
            {
                throw new NotFoundException(Resource, [.. _ids.Select(static id => id.ToString(null, CultureInfo.InvariantCulture))]);
            }

            IReadOnlyList<object> ids = [.. _ids.Select(static id => (object)id)];
            foreach ((IReadOnlyDictionary<string, object?> assignments, FilterTree? condition, string? refusalCode) in _updates)
            {
                if (condition is null)
                {
                    _ = batch.Update(new UpdateSpec<TEntity>.ByIds(ids, read.Filter, granted.Filter, assignments, Stamp: true, AllOrNothing: true));
                }
                else if (refusalCode is not null)
                {
                    // A refusing condition: every target, existing, must still meet it, or the batch
                    // fails with the action's code; then the update is all or nothing as an
                    // unconditioned one, and stamps every target. Its filter keeps the condition, so a
                    // target changed between the count and the update fails the row-security count
                    // rather than being overwritten.
                    EntityDescriptor root = _map.Descriptor(_root);
                    string key = root.Key.Name;
                    batch.Assert(
                        new QuerySpec { Root = root, Select = key, Filter = condition, Restrictions = [new KeySetRestriction(key, affected.Name)] },
                        arguments: null,
                        expected: _ids.Length,
                        TellmaSqlErrors.Invariant,
                        refusalCode);
                    _ = batch.Update(new UpdateSpec<TEntity>.ByIds(
                        ids, read.Filter, Conjoin(read: null, granted.Filter, condition), assignments, Stamp: true, AllOrNothing: true, StampUnchanged: true));
                }
                else
                {
                    // A skipping condition repeats the action's precondition: a target that changed
                    // since the first round, and no longer meets it, the caller's Read filter or the
                    // grant, is skipped rather than refused; the first round already reported the
                    // missing and the forbidden targets.
                    _ = batch.Update(new UpdateSpec<TEntity>.ByIds(ids, read.Filter, Conjoin(read.Filter, granted.Filter, condition), assignments, Stamp: true, AllOrNothing: false));
                }
            }
        }

        /// <summary>The caller's Read filter, the grant's filter and an update's condition, all of which a target must meet.</summary>
        /// <param name="read">The caller's Read filter; null for an unrestricted read.</param>
        /// <param name="grant">The action grant's filter; null for an unrestricted grant.</param>
        /// <param name="condition">The update's condition.</param>
        /// <returns>The conjunction of the filters that restrict.</returns>
        private static FilterTree Conjoin(FilterTree? read, FilterTree? grant, FilterTree condition)
        {
            FilterTree[] filters = [.. new[] { read, grant }.OfType<FilterTree>(), condition];
            return filters.Length == 1 ? condition : FilterTree.And(filters);
        }

        // Spec 0015 §9.3: an action with PostCheck asserts, after every write and before the tag
        // bumps, that the rows it wrote are still under the grant's filter.
        /// <summary>Composes the post-check over the rows the emitter wrote and the target ids the updates wrote.</summary>
        /// <param name="batch">The batch.</param>
        /// <param name="granted">The action's decision, filtered.</param>
        /// <param name="saved">The emitter's handle; null when no row was marked.</param>
        /// <param name="affected">The target ids' table.</param>
        private void ComposePostCheck(IDataBatch batch, AccessDecision granted, SaveHandle? saved, SqlIdentifier affected)
        {
            EntityDescriptor root = _map.Descriptor(_root);
            string key = root.Key.Name;
            if (saved is not null)
            {
                int rows = _marked.Select(static row => row.Id).Distinct().Count();
                batch.Assert(
                    new QuerySpec { Root = root, Select = key, Filter = granted.Filter, Restrictions = [new KeySetRestriction(key, saved.SavedIds.Name)] },
                    arguments: null,
                    expected: rows,
                    TellmaSqlErrors.RowSecurity,
                    RowSecurityCode);
            }

            if (_updates.Count > 0)
            {
                batch.Assert(
                    new QuerySpec { Root = root, Select = key, Filter = granted.Filter, Restrictions = [new KeySetRestriction(key, affected.Name)] },
                    arguments: null,
                    expected: _ids.Length,
                    TellmaSqlErrors.RowSecurity,
                    RowSecurityCode);
            }
        }

        /// <summary>Completes the method's Persisted and waits for the method to return.</summary>
        /// <returns>The method's failure after the commit; null when it returned, or for a built-in action.</returns>
        private async Task<ExceptionDispatchInfo?> ResumeMethodAsync()
        {
            _ = _context?.CompletePersisted();
            if (_methodTask is not Task method)
            {
                return null;
            }

            try
            {
                await method.ConfigureAwait(false);
                return null;
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                // The action committed: its post-commit phase runs before the failure is raised.
                return ExceptionDispatchInfo.Capture(failure);
            }
        }

        // Spec 0015 §8, as a save's: an action whose response was lost around COMMIT committed when
        // the probe found a row the batch inserted. A plain Read re-issues the read-back over the
        // target ids; its failure never answers the committed action with an error a caller might
        // retry: it is logged, and the action answers its ids alone. A method waiting for its persist
        // resumes, though the lost batch completed none of the results it appended; a failure that
        // follows is logged as any failure after a commit, and raised only by a method answering a
        // value of its own.
        /// <summary>Answers an action whose commit the probe proved, then runs its post-commit phase.</summary>
        /// <returns>The method's own result, or the target ids and, when asked and the read answered, the rows.</returns>
        private async Task<object?> AfterProvenCommitAsync()
        {
            // --- The method and the released flows resume, and may fail reading what the lost batch never answered ---
            ExceptionDispatchInfo? failed = await ResumeMethodAsync().ConfigureAwait(false);
            await ResumeReleasedAsync().ConfigureAwait(false);

            // --- The read-back over the target ids, when the action answers them ---
            DetailsComposition<TEntity, TKey>? composition = null;
            bool answered = false;
            if (_options.ReturnEntities && !OwnResult)
            {
                try
                {
                    _ = await Services.Runner.RunAsync(
                        BatchPurpose.Read,
                        (user, batch) =>
                        {
                            batch.Limits = QueryexProfiles.PipelineLimits;
                            composition = Services.Details.Compose(_stack, _behavior, _options.Details ?? DetailsRequest.None, _ids, KeyTables.Bind(batch, _ids), user, batch);
                            return ValueTask.CompletedTask;
                        },
                        static (_, outcome) => outcome,
                        CancellationToken.None).ConfigureAwait(false);
                    Services.Accessor.Current.ApplyCultures();
                    answered = true;
                }
                catch (Exception failure) when (failure is not OutOfMemoryException)
                {
                    // The action committed: the ids answer it.
                    ProvenCommitReadFailed(failure);
                }
            }

            await AfterPersistAsync(committed: null).ConfigureAwait(false);

            // --- The answer: a method answering a value of its own has none to give once it failed,
            // so its failure is raised; any other action committed, and its ids answer it ---
            RaiseAfterCommit(failed);
            _partial?.Throw();
            return OwnResult ? _result : answered && composition is not null ? composition.Complete() : IdsOnly();
        }

        /// <summary>Appends the groups of one order enlisted with the action, each with its own groups around it.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="order">The groups' order.</param>
        /// <param name="first">Whether this is the batch's first composition.</param>
        /// <returns>A task that completes when the groups are composed.</returns>
        private ValueTask ComposeEnlistedAsync(IDataBatch batch, EnlistOrder order, bool first)
        {
            return Enlistments is null ? ValueTask.CompletedTask : _frame!.ComposeGroupsAsync(batch, order, first);
        }

        /// <summary>Runs the post-commit phase of a committed action, then its groups'.</summary>
        /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        private async Task AfterPersistAsync(BatchOutcome? committed)
        {
            ActionPersistOutcome<TEntity, TKey> outcome = new()
            {
                Entities = _targets,

                // The rows the emitter saved carry the batch's stamp; an update-only action's rows are
                // stamped in SQL, so it reports the clock at the commit.
                Stamp = _saved is { Receipt.IsCompleted: true } saved ? saved.Receipt.Value.Stamp : Services.Time.GetUtcNow(),
                Context = Services.Accessor.Current,
                VersionTags = TagsAfter(committed),
                UserVersionTags = committed?.UserVersionTags,
                Action = _descriptor.Name,
                Arguments = _arguments,
                Ids = _ids,
            };
            await AfterCommitAsync(_behavior, outcome).ConfigureAwait(false);

            // --- The groups' post-commit phase, in the order the batch composed them ---
            if (Enlistments is not null)
            {
                await _frame!.AfterGroupsCommitAsync(committed).ConfigureAwait(false);
            }
        }

        /// <summary>Refuses a recomposed persist whose batch-local names differ from those the participants hold.</summary>
        /// <param name="saved">The recomposed emitter's handle; null when no row was marked.</param>
        /// <param name="affected">The recomposed target ids' table.</param>
        /// <exception cref="InvalidOperationException">A name differs.</exception>
        private void EnsureSameNames(SaveHandle? saved, SqlIdentifier affected)
        {
            (SqlIdentifier? firstSaved, IReadOnlyList<ColumnCapture> firstCaptures, SqlIdentifier firstAffected) = _firstNames
                ?? throw new InvalidOperationException("A re-entered persist follows a first entry that composed the action.");
            if (firstSaved != saved?.SavedIds || !firstCaptures.SequenceEqual(saved?.Captures ?? []) || firstAffected != affected)
            {
                throw new InvalidOperationException(
                    $"The recomposed persist batch named the target ids '{affected.Name}' where its first entry named '{firstAffected.Name}', so the participants' recorded statements would address the wrong tables. A persist's compose appends the same statements in the same order on every entry.");
            }
        }

        /// <summary>What the action knows about its persist, for the translation of its failures.</summary>
        /// <returns>The context.</returns>
        private PersistFailureContext FailureContext()
        {
            return new PersistFailureContext(Resource, _descriptor.Action, _root, [], Services.Accessor.Current.ContentLanguageIndex)
            {
                Model = Services.Metadata.All,
            };
        }
    }
}
