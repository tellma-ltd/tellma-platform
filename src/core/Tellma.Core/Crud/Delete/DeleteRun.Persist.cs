// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Save;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Delete
{
    // Spec 0015 §10.1: a hooked delete's round trip 1 loads the targets under the caller's Read filter
    // and counts them under the Delete grant (§9.2), beside every load the validators request before
    // their first await; the validators then run their rounds. The persist is one batch through the
    // runner, composed in §6.7's order: the access guards' lock, the delete, the participants through
    // the recording view, the guards' invariants. A re-entered compose re-runs the validators in
    // memory, keeps round trip 1's verdicts, recomposes the delete under the fresh grants and replays
    // the recording (§5.1). The groups the validators enlisted (§13.3) sit around the delete: the
    // BeforeHost ones ahead of it, the AfterHost ones after its participants, and the access guards
    // join once the batch is composed, over every table any statement of it writes. The executor's failures are translated by §14.2, a
    // group's against the group, and a committed delete runs its post-commit phase (§13.2), then its
    // groups'.
    /// <summary>One delete of one stack: its validation rounds, its persist and its post-commit phase.</summary>
    internal sealed partial class DeleteRun<TEntity, TKey>
    {
        /// <summary>Runs round trip 1 and the validators' rounds.</summary>
        /// <param name="frame">The delete's confirmation frame.</param>
        /// <returns>A task that completes when every validator returned.</returns>
        private async Task ValidateAsync(ConfirmationFrame frame)
        {
            // --- The scheduler, the loader, the frame and the context the validators see; the ids are
            // known, so the frame composes from the start ---
            ValidationScheduler scheduler = new(_stack.Limits.MaxValidationRounds, OperationName, DispatchAsync, Services.Metrics, _cancellationToken);
            _loader = new ContextLoader(Services.Metadata, _map, scheduler, ReadDecision);
            _frame = _site is null
                ? new PipelineFrame(Services, _stack, OperationName, frame, OpenWriteFrame.SourceOf(Services.Accessor.Current))
                : throw new InvalidOperationException($"An enlisted delete of {Resource} composes in its host's frame and opens no frame of its own.");
            using IDisposable flow = FrameFlow.Enter(_frame);
            _frame.BeginComposing(scheduler, _loader, _map, _errors, _cancellationToken);
            _context = new DeleteContext<TEntity, TKey>
            {
                Ids = _ids,
                Context = Services.Accessor.Current,
                Loader = _loader,
                Errors = _errors,
                Host = _frame,
                CancellationToken = _cancellationToken,
            };

            // --- Round trip 1 carries the targets; the validators join it with their first loads ---
            _loader.Enqueue(this, new PipelineLoad(batch => ComposeTargets<TEntity, TKey>(
                batch, Composer, _map, _root, _ids, AccessActions.Delete, [], (rows, granted) => _loaded = (rows, granted))));
            scheduler.RequestRound();
            StartValidators(scheduler, _context, _frame);
            _ = await scheduler.RunAsync().ConfigureAwait(false);
        }

        /// <summary>
        ///     Dispatches one round; after the first, settles the targets before any validator resumes;
        ///     then the checks of every group enlisted with the delete that the round served.
        /// </summary>
        /// <param name="round">The round's 1-based number.</param>
        /// <param name="cancellationToken">Cancels the round.</param>
        /// <returns>A task that completes when the round's refs are filled.</returns>
        private async Task DispatchAsync(int round, CancellationToken cancellationToken)
        {
            await DispatchRoundAsync(_loader!, cancellationToken).ConfigureAwait(false);
            if (round == 1)
            {
                // An absent or hidden id is not found, before a row outside the grant is forbidden.
                _targets = SettleTargets(_ids, _loaded.Rows, _loaded.Granted, AccessActions.Delete);
                _context!.Entities = _targets;
            }

            if (Enlistments is EnlistmentRounds enlistments)
            {
                await enlistments.AfterRoundAsync(round).ConfigureAwait(false);
            }
        }

        /// <summary>Starts the service's delete hook, then every validator's, as tasks of the rounds.</summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="context">The context the hooks see.</param>
        /// <param name="frame">The frame the validators run in; null for a re-run, which enlists nothing of its own.</param>
        private void StartValidators(ValidationScheduler scheduler, DeleteContext<TEntity, TKey> context, EnlistmentFrame? frame)
        {
            // Spec 0015 §5.1: each task at the same slot on both runs, so a re-run's enlistments are
            // matched to the first run's by the task that made them.
            Run(scheduler, frame, _behavior.GetType().Name, () => _behavior.ValidateDeleteAsync(context), slot: 0);
            int slot = 1;
            foreach (IEntityValidator<TEntity> validator in Services.Services.GetServices<IEntityValidator<TEntity>>())
            {
                Run(scheduler, frame, validator.GetType().Name, () => validator.ValidateDeleteAsync(context), slot++);
            }
        }

        /// <summary>Runs a task of the rounds at its slot, inside a frame when one is given.</summary>
        /// <param name="scheduler">The rounds' scheduler.</param>
        /// <param name="frame">The frame; null to run the task as it is.</param>
        /// <param name="name">The task's name.</param>
        /// <param name="task">The task's body.</param>
        /// <param name="slot">The task's slot among the delete's own tasks.</param>
        private static void Run(ValidationScheduler scheduler, EnlistmentFrame? frame, string name, Func<Task> task, int slot)
        {
            scheduler.Run(name, frame is null ? task : () => FrameFlow.RunInAsync(frame, task), slot);
        }

        // Spec 0015 §5.1 step 1 of a re-entry, as a save's: the validators run again over the loaded
        // targets and the fresh user, the loader answering only from the rounds already run, and a new
        // error or a new unconfirmed warning refuses the persist. Round trip 1's verdicts stand: the
        // delete statement checks the ids again under the fresh grants inside the transaction.
        /// <summary>Re-runs the validators in memory for the fresh connected user.</summary>
        /// <param name="user">The fresh connected user.</param>
        /// <returns>A task that completes when every validator returned.</returns>
        /// <exception cref="StaleContextException">A tag the rounds were served under moved, or a validator asked for a load the rounds did not serve.</exception>
        /// <exception cref="ValidationException">A validator reported an error.</exception>
        /// <exception cref="ConfirmationRequiredException">A validator raised an unconfirmed warning.</exception>
        private async Task RevalidateAsync(ConnectedUser user)
        {
            EnsureServedTagsHold(user);
            Composer = user;
            _loader!.AnswerOnly();
            using ConfirmationFrame frame = Services.Confirmations.Frame(Resource, OperationName, audited: false);
            ValidationErrors<TEntity> errors = new();
            ValidationScheduler scheduler = new(
                _stack.Limits.MaxValidationRounds,
                OperationName,
                static (_, _) => throw new InvalidOperationException("A re-run of the validators answers from the rounds already run and dispatches none."),
                metrics: null,
                _cancellationToken);
            RerunHost host = new(_frame!, scheduler, frame, Services.Metadata);
            DeleteContext<TEntity, TKey> context = new()
            {
                Ids = _ids,
                Context = Services.Accessor.Current,
                Loader = _loader,
                Errors = errors,
                Host = host,
                CancellationToken = _cancellationToken,
                Entities = _targets,
            };

            // --- The validators, with every round trip refused as stale ---
            using (ComposeFence.Close(static () => new StaleContextException([])))
            {
                StartValidators(scheduler, context, frame: null);
                _ = await scheduler.RunAsync().ConfigureAwait(false);
            }

            // --- What they report, then every first-run enlistment of theirs matched by its task ---
            ValidationOutcome.Raise(staleStamp: null, errors, frame);
            host.EnsureMatched();
        }

        /// <summary>Persists the delete, commits it, and runs the post-commit phase.</summary>
        /// <returns>The rows deleted.</returns>
        /// <exception cref="TenantUnavailableException">The tenant's state admits no persist.</exception>
        private async Task<AffectedResult> PersistAsync()
        {
            // --- The tenant-state verdict: a delete is a mutation without AllowWhenReadOnly ---
            PersistVerdict.Ensure(Services.Accessor.Current, mutation: true, allowWhenReadOnly: false, Services.Frames.Open);

            // --- One Persist batch ---
            BatchOutcome outcome;
            try
            {
                outcome = await Services.Runner.RunAsync(BatchPurpose.Persist, ComposePersistAsync, static (_, outcome) => outcome, _cancellationToken).ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous) when (ambiguous.Verdict == CommitVerdict.Committed)
            {
                // A participant's inserted row proved the commit: the delete answers what it was asked to delete.
                return await AfterProvenCommitAsync().ConfigureAwait(false);
            }
            catch (DataAccessAmbiguousException ambiguous)
            {
                // A delete cannot be probed: the caller reloads to learn whether it committed.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, ambiguous);
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

            // --- The commit: the post-commit phase on the deleted ids, then the count ---
            Services.Accessor.Current.ApplyCultures();
            DeleteHandle handle = _handle ?? throw new InvalidOperationException("A committed persist composed the delete's statement.");
            List<TKey> deleted = Deleted(handle.Receipt.Value);
            await AfterCommitAsync(outcome, deleted).ConfigureAwait(false);
            return new AffectedResult(deleted.Count);
        }

        // Spec 0015 §10: the fixed prefix and suffix are the executor's; between them the delete
        // appends its own statements and the participants' recorded ones, in this order on every entry.
        /// <summary>Composes the persist batch: on the first entry calling the participants, on a re-entry replaying them.</summary>
        /// <param name="user">The connected user the batch is composed for.</param>
        /// <param name="batch">The batch.</param>
        /// <returns>A task that completes when the batch is composed.</returns>
        /// <exception cref="ForbiddenException">The connected user's set no longer grants a delete of the stack.</exception>
        /// <exception cref="NotFoundException">The connected user reads no row of the stack, so every id is hidden.</exception>
        private async ValueTask ComposePersistAsync(ConnectedUser user, IDataBatch batch)
        {
            // --- A re-entry, which the operation's frame counts: the validators re-run in memory under the fresh user ---
            bool first = ++_entries == 1;
            if (!first && _hooked)
            {
                await RevalidateAsync(user).ConfigureAwait(false);
            }

            // --- The Delete grant, decided for the connected user ---
            Composer = user;
            AccessDecision granted = Granted(user, AccessActions.Delete);
            batch.Limits = QueryexProfiles.PipelineLimits;
            IReadOnlySet<TableName> callers = batch.CallerAuthority;
            DependOnServed(batch);

            // --- The groups enlisted ahead of the delete, then the delete, under the stack's authority ---
            await ComposeEnlistedAsync(batch, EnlistOrder.BeforeHost, first).ConfigureAwait(false);
            Authorize(batch, StackTables);
            DeleteHandle handle = batch.Delete(Spec(batch, user, granted));
            _handle = handle;

            // --- The participants: called on the first entry, replayed on a re-entry ---
            if (first)
            {
                _firstHandle = handle;
                _participants = new RecordingBatch(batch, CallbackFailed);
                DeletePersistContext<TEntity> context = new()
                {
                    Entities = [],
                    Context = Services.Accessor.Current,
                    Batch = _participants,
                    AffectedIds = handle.DeletedIds,
                    Captures = handle.Captures,
                    Resource = Resource,
                    Notifier = Services.Services.GetService<INotifier>(),
                    AssignmentsService = Services.Services.GetService<IAssignments>(),
                };
                await ContributeAsync(_behavior, context).ConfigureAwait(false);
                _participants.Seal();
            }
            else
            {
                EnsureSameNames(handle);
                _participants!.Replay(batch);
            }

            // --- The groups enlisted after the delete ---
            await ComposeEnlistedAsync(batch, EnlistOrder.AfterHost, first).ConfigureAwait(false);

            // --- The access guards, after every contribution, when the batch writes a security table:
            // the stack's, a group's at any depth, or any statement's declared writes ---
            Authorize(batch, callers);
            IEnumerable<TableName> composed = Enlistments is EnlistmentRounds enlistments ? StackTables.Concat(enlistments.GroupTables) : StackTables;
            _ = PersistGuards.Contribute(Services.AccessGuards, Services.Accessor.Current, batch, composed);
        }

        // Spec 0015 §10.1 to §10.3: by ids and with descendants, the ids are counted under the caller's
        // Read filter (hidden equals missing) and the grant's filter inside the statement; by query,
        // the count of the rows the filter matches rides the same batch before the delete, which
        // throws past its cap or on another count than the caller confirmed.
        /// <summary>The delete's spec, decided for the connected user.</summary>
        /// <param name="batch">The batch, which a delete by query's count joins first.</param>
        /// <param name="user">The connected user.</param>
        /// <param name="granted">The Delete decision.</param>
        /// <returns>The spec.</returns>
        /// <exception cref="NotFoundException">The connected user reads no row of the stack.</exception>
        private DeleteSpec<TEntity> Spec(IDataBatch batch, ConnectedUser user, AccessDecision granted)
        {
            if (_shape == DeleteShape.ByQuery)
            {
                // --- The count of the rows matched, one past the cap, then the capped delete ---
                FilterTree filter = Filter(granted);
                int cap = _stack.Limits.MaxDeleteByQueryRows;
                EntityDescriptor root = _map.Descriptor(_root);
                BatchResult<int> counted = batch.Count(new QuerySpec { Root = root, Select = root.Key.Name, Filter = filter }, _arguments, cap);
                _counts = new QueryDeleteCounts(cap, _expectedCount, counted);
                return new DeleteSpec<TEntity>.ByQuery(filter, _arguments, cap, _expectedCount);
            }

            // --- By ids or with descendants: the caller's read filter, or nothing visible at all ---
            AccessDecision read = Decide(user, Resource, AccessActions.Read);
            if (read.Outcome == AccessOutcome.Denied)
            {
                throw new NotFoundException(Resource, [.. _ids.Select(static id => id.ToString(null, CultureInfo.InvariantCulture))]);
            }

            IReadOnlyList<object> ids = [.. _ids.Select(static id => (object)id)];
            return _shape == DeleteShape.WithDescendants
                ? new DeleteSpec<TEntity>.WithDescendants(ids, read.Filter, granted.Filter)
                : new DeleteSpec<TEntity>.ByIds(ids, read.Filter, granted.Filter, _stamps);
        }

        // Spec 0015 §8, as a save's: a delete whose response was lost around COMMIT committed when the
        // probe found a row a participant inserted. Nothing reads the deleted rows back: the delete
        // answers the ids it was asked to delete (or, by query, the count the caller confirmed, which
        // the statement checked), and the post-commit phase runs on them.
        /// <summary>Runs the post-commit phase of a delete whose commit the probe proved.</summary>
        /// <returns>The rows the delete was asked to delete.</returns>
        private async Task<AffectedResult> AfterProvenCommitAsync()
        {
            IReadOnlyList<TKey> deleted = _shape == DeleteShape.ByQuery ? [] : _ids;
            await AfterCommitAsync(committed: null, deleted).ConfigureAwait(false);
            return new AffectedResult(_shape == DeleteShape.ByQuery ? _expectedCount : _ids.Length);
        }

        /// <summary>Appends the groups of one order enlisted with the delete, each with its own groups around it.</summary>
        /// <param name="batch">The persist batch.</param>
        /// <param name="order">The groups' order.</param>
        /// <param name="first">Whether this is the batch's first composition.</param>
        /// <returns>A task that completes when the groups are composed.</returns>
        private ValueTask ComposeEnlistedAsync(IDataBatch batch, EnlistOrder order, bool first)
        {
            return Enlistments is null ? ValueTask.CompletedTask : _frame!.ComposeGroupsAsync(batch, order, first);
        }

        /// <summary>Runs the post-commit phase of a committed delete, then its groups'.</summary>
        /// <param name="committed">The outcome of the batch that committed; null when none answered.</param>
        /// <param name="deleted">The deleted ids.</param>
        /// <returns>A task that completes when every participant returned.</returns>
        private async Task AfterCommitAsync(BatchOutcome? committed, IReadOnlyList<TKey> deleted)
        {
            DeletePersistOutcome<TEntity, TKey> outcome = new()
            {
                Entities = [],
                Stamp = Services.Time.GetUtcNow(),
                Context = Services.Accessor.Current,
                VersionTags = TagsAfter(committed),
                UserVersionTags = committed?.UserVersionTags,
                DeletedIds = deleted,
            };
            await AfterCommitAsync(_behavior, outcome).ConfigureAwait(false);

            // --- The groups' post-commit phase, in the order the batch composed them ---
            if (Enlistments is not null)
            {
                await _frame!.AfterGroupsCommitAsync(committed).ConfigureAwait(false);
            }
        }

        /// <summary>The deleted ids: those the request named, in its order, then any other, ascending.</summary>
        /// <param name="receipt">The delete's receipt.</param>
        /// <returns>The ids.</returns>
        private List<TKey> Deleted(DeleteReceipt receipt)
        {
            HashSet<TKey> all = [.. receipt.DeletedIds.Select(KeyOf<TKey>)];
            List<TKey> ordered = [.. _ids.Where(all.Contains)];
            all.ExceptWith(ordered);
            ordered.AddRange(all.Order());
            return ordered;
        }

        /// <summary>Refuses a recomposed delete whose batch-local names differ from those the participants hold.</summary>
        /// <param name="handle">The recomposed delete's handle.</param>
        /// <exception cref="InvalidOperationException">A name differs.</exception>
        private void EnsureSameNames(DeleteHandle handle)
        {
            DeleteHandle first = _firstHandle ?? throw new InvalidOperationException("A re-entered persist follows a first entry that composed the delete.");
            if (first.DeletedIds != handle.DeletedIds || !first.Captures.SequenceEqual(handle.Captures))
            {
                throw new InvalidOperationException(
                    $"The recomposed persist batch named the deleted ids '{handle.DeletedIds.Name}' where its first entry named '{first.DeletedIds.Name}', so the participants' recorded statements would address the wrong tables. A persist's compose appends the same statements in the same order on every entry.");
            }
        }

        // Spec 0015 §10.1: a row still referenced is Fk.InUse at Ids[i], naming the referencing entity
        // and property. SQL Server's 547 names the constraint and not the row, so the id is known only
        // when the delete names one; a delete of several reports at the ids, and a delete by query,
        // which names none, at the root.
        /// <summary>What the delete knows about its persist, for the translation of its failures.</summary>
        /// <returns>The context.</returns>
        private PersistFailureContext FailureContext()
        {
            ValidationPath at = _shape == DeleteShape.ByQuery ? ValidationPath.Root
                : _ids.Length == 1 ? ValidationPath.Root.Property(IdsProperty).Index(0)
                : ValidationPath.Root.Property(IdsProperty);
            return new PersistFailureContext(Resource, AccessActions.Delete, _root, [], Services.Accessor.Current.ContentLanguageIndex, CarriesQueryText: _shape == DeleteShape.ByQuery)
            {
                Model = Services.Metadata.All,
                Drops = [.. Aggregate(_root).Select(entity => new DroppedRows(entity, at))],
                DeleteCounts = _counts,
            };
        }

        /// <summary>An entity and every entity its collections hold, at every depth, each once.</summary>
        /// <param name="root">The root entity.</param>
        /// <returns>The entities, the root first.</returns>
        private static List<EntityMetadata> Aggregate(EntityMetadata root)
        {
            List<EntityMetadata> entities = [];
            Queue<EntityMetadata> pending = new([root]);
            while (pending.TryDequeue(out EntityMetadata? entity))
            {
                if (entities.Contains(entity))
                {
                    continue;
                }

                entities.Add(entity);
                foreach (ChildCollectionMetadata collection in entity.Children)
                {
                    pending.Enqueue(collection.Child);
                }
            }

            return entities;
        }
    }
}
