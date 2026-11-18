// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Delete
{
    // Spec 0015 §13.3, an enlisted delete: a delete by ids with no expected stamps. When the target
    // validates deletes, its rows load unfiltered in its host's next round and its hook and its
    // components validate them as tasks of the host's rounds; a missing row is not found at the
    // mapped path, never a 404. When it does not, nothing loads: the delete statement's own count
    // finds a missing row at persist. The authority checks are the host's, so the delete carries no
    // read filter and no grant filter. The group then sits in its host's persist batch: its delete,
    // its hook and its effects, its failures translated against its own ids and re-homed onto the
    // host's paths.
    /// <summary>One delete of one stack: the stages of a delete enlisted with another operation's frame.</summary>
    internal sealed partial class DeleteRun<TEntity, TKey>
    {
        /// <summary>The round after whose dispatch the targets settle; null when none is due.</summary>
        private int? _settleRound;

        /// <summary>Whether the enlistment failed: the group composes nothing.</summary>
        private bool _abandoned;

        /// <summary>The ordinals of the statements the group composed last, the end exclusive; null before it composed.</summary>
        private (int Start, int End)? _span;

        /// <inheritdoc />
        public StackDescriptor Target => Site.Target;

        /// <inheritdoc />
        public string Kind => CrudOperationNames.Delete;

        /// <inheritdoc />
        public EnlistOrder Order => Site.Order;

        /// <inheritdoc />
        public GroupFrame Frame => field ??= new GroupFrame(Site.Host, _errors);

        /// <inheritdoc />
        public IReadOnlySet<TableName> Tables => StackTables;

        /// <inheritdoc />
        public IReadOnlyList<long> Ids => [.. _ids.Select(static id => long.CreateChecked(id))];

        /// <inheritdoc />
        public DateTimeOffset? Stamp => null;

        /// <summary>The frame the delete is enlisted with and how.</summary>
        /// <exception cref="InvalidOperationException">The delete runs on its own.</exception>
        private EnlistmentSite Site => _site ?? throw new InvalidOperationException("A delete of its own is no group of another operation's frame.");

        /// <summary>Takes the ids of an enlisted delete, each once, in the order first named.</summary>
        /// <param name="ids">The ids.</param>
        /// <returns>The delete, as its host's group.</returns>
        public IEnlistedDelete Enlisted(IReadOnlyList<TKey> ids)
        {
            ArgumentNullException.ThrowIfNull(ids);
            _ = Site;
            _shape = DeleteShape.ByIds;
            _ids = DistinctIds(ids);
            return this;
        }

        /// <inheritdoc />
        public void Enlist()
        {
            EnlistmentSite site = Site;
            EnlistmentRounds rounds = site.Host.Rounds;
            try
            {
                // --- The ceiling, then one write per id and target in the frame ---
                CheckIds(_ids);
                rounds.Claim(_stack, Ids);
                _map = rounds.Map;

                // --- Only a target that validates deletes loads its rows and runs its validators ---
                _hooked = HookOverrides.ValidatesDeletes(_behavior, Services.Services.GetServices<IEntityValidator<TEntity>>());
                Frame.BeginComposing();
                if (!_hooked || _ids.Length == 0)
                {
                    return;
                }

                _loader = rounds.Loader;
                _context = new DeleteContext<TEntity, TKey>
                {
                    Ids = _ids,
                    Context = Services.Accessor.Current,
                    Loader = _loader,
                    Errors = _errors,
                    Host = Frame,
                    Enlistment = site.Pair,
                    CancellationToken = rounds.CancellationToken,
                };

                // --- The targets, unfiltered, in the round a load requested now rides; the validators now ---
                _loader.Enqueue(this, new PipelineLoad(ComposeEnlistedTargets));
                _settleRound = rounds.Scheduler.Round;
                rounds.Scheduler.RequestRound();
                StartValidators(rounds.Scheduler, _context, Frame);
            }
            catch
            {
                // A refused or failed enlistment composes nothing, whatever its writer makes of the failure.
                _abandoned = true;
                throw;
            }
        }

        /// <inheritdoc />
        public Task AfterRoundAsync(int round)
        {
            if (_abandoned || _context is null)
            {
                return Task.CompletedTask;
            }

            // --- The targets, after the round that loaded them: a missing one is not found at the mapped path ---
            if (_settleRound is int due && round >= due)
            {
                _settleRound = null;
                var byId = _loaded.Rows.ToDictionary(static row => row.Id);
                long[] missing = [.. _ids.Where(id => !byId.ContainsKey(id)).Select(static id => long.CreateChecked(id))];
                if (missing.Length > 0)
                {
                    _errors.Add(ValidationPath.Root, ValidationCodes.EntityNotFound, new Dictionary<string, object?>(StringComparer.Ordinal) { ["ids"] = missing });
                }

                _targets = [.. _ids.Where(byId.ContainsKey).Select(id => byId[id])];
                _context.Entities = _targets;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void CompleteRounds()
        {
            // A delete's checks all run in its rounds: nothing follows them.
        }

        /// <inheritdoc />
        public async ValueTask ComposeAsync(IDataBatch batch, bool first)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (_abandoned || _ids.Length == 0)
            {
                return;
            }

            // --- The delete, under the target's authority and with no filter of its own, then its hook
            // and its effects: called on the first entry, replayed on a re-entry; the ordinals they
            // take mark the group's statements ---
            int start = batch is IBatchOrdinals ordinals ? ordinals.NextOrdinal() : -1;
            Authorize(batch, StackTables);
            DeleteHandle handle = batch.Delete(new DeleteSpec<TEntity>.ByIds([.. _ids.Select(static id => (object)id)], ReadFilter: null, Filter: null, ExpectedStamps: null));
            _handle = handle;
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
                    Enlistment = Site.Pair,
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

            _span = batch is IBatchOrdinals marked ? (start, marked.NextOrdinal()) : null;
        }

        // Spec 0015 §13.3 and §14.2: a failure the executor attributes to one of the group's
        // statements is translated against the group's ids and resource, and re-homed onto the host's
        // paths; a missing row (50404) is not found at the mapped path with the ids, never a 404.
        /// <inheritdoc />
        public Exception? Translate(DataAccessException failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            if (_span is not (int start, int end) || failure.StatementOrdinal is not int ordinal || ordinal < start || ordinal >= end)
            {
                return null;
            }

            Exception translated = PersistFailures.Translate(failure, FailureContext(), Services.Metrics) ?? failure;
            return translated switch
            {
                ValidationException validation => new ValidationException([.. validation.Errors.Select(_errors.Rehome)]),
                NotFoundException missing => new ValidationException(
                    [_errors.Rehome(new ValidationError(ValidationPath.Root, ValidationCodes.EntityNotFound, new Dictionary<string, object?>(StringComparer.Ordinal) { ["ids"] = missing.Ids }))]),
                _ => translated,
            };
        }

        /// <inheritdoc />
        async Task IEnlistedGroup.AfterCommitAsync(BatchOutcome? committed)
        {
            if (_abandoned || _handle is not DeleteHandle handle)
            {
                return;
            }

            // --- The deleted ids, as the receipt read them; a proven commit's were never read ---
            IReadOnlyList<TKey> deleted = handle.Receipt.IsCompleted ? Deleted(handle.Receipt.Value) : _ids;
            // A delete stamps no row, so its outcome carries the clock, as the front door's delete does.
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
        }

        /// <inheritdoc />
        public void Release()
        {
            // A delete reserves no ids.
        }

        /// <summary>Composes the group's own load of its host's round: the target rows, unfiltered.</summary>
        /// <param name="batch">The round's batch.</param>
        /// <returns>Reads the rows into the delete once the batch ran.</returns>
        private Action ComposeEnlistedTargets(IDataBatch batch)
        {
            EntityDescriptor descriptor = _map.Descriptor(_root);
            string key = descriptor.Key.Name;
            BatchResult<EntityQueryResult<TEntity>> rows = batch.Query(new EntityQuery<TEntity>
            {
                Select = string.Join(", ", _map.Declared(_root).Select(static property => property.Name)),
                AllProperties = true,
                Restrictions = [new KeySetRestriction(key, KeyTables.Bind(batch, _ids).Name)],
                OrderBy = key,
            });
            return () => _loaded = (rows.Value.Entities, null);
        }
    }
}
