// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Trees;
using Tellma.Core.Queryex;

namespace Tellma.Core.Crud.Save
{
    // Spec 0015 §13.3, an enlisted save: the target's pre-persist stages run as tasks of its host's
    // rounds, over the host's loader and scheduler. The shape and ceilings, the preprocessing (the
    // writer's server-owned members re-applied after the resets), then the ids: at once from a warm
    // buffer, else riding the host's next round, the writer parked until they do. The before images
    // of the update roots load unfiltered in the same round, and the checks of §6.6 follow it: a
    // missing before image is not found at the mapped path, never a 404. The authority checks are
    // the host's, so the type-level grant, the read-filtered images, the save grant's count, the
    // reference pre-load, the early stamp comparison and the post-check never run. The group then
    // sits in its host's persist batch: its emitter, its hook and its effects, its failures
    // translated against its own rows and re-homed onto the host's paths.
    /// <summary>One save of one stack: the stages of a save enlisted with another operation's frame.</summary>
    internal sealed partial class SaveRun<TEntity, TKey>
    {
        /// <summary>The audit columns and the flag an enlistment never supplies, which the platform alone writes.</summary>
        private static readonly string[] PlatformOwned =
        [
            nameof(TopLevelEntity.CreatedAt),
            nameof(TopLevelEntity.CreatedById),
            nameof(TopLevelEntity.ModifiedAt),
            nameof(TopLevelEntity.ModifiedById),
            nameof(IActivatable.IsActive),
        ];

        /// <summary>The server-owned values the writer supplied, by root, read before the resets.</summary>
        private (PropertyMetadata Property, object? Value)[][] _supplied = [];

        /// <summary>Whether the enlistment went no further than its shape, or failed: the group composes nothing.</summary>
        private bool _abandoned;

        /// <summary>The round after whose dispatch the checks run; null when none is due.</summary>
        private int? _settleRound;

        /// <summary>The ordinals of the statements the group composed last, the end exclusive; null before it composed.</summary>
        private (int Start, int End)? _span;

        /// <inheritdoc />
        public StackDescriptor Target => Enlisted.Site.Target;

        /// <inheritdoc />
        public string Kind => CrudOperationNames.Save;

        /// <inheritdoc />
        public EnlistOrder Order => Enlisted.Site.Order;

        /// <inheritdoc />
        public GroupFrame Frame => field ??= new GroupFrame(Enlisted.Site.Host, _errors);

        /// <inheritdoc />
        public IReadOnlySet<TableName> Tables => StackTables;

        /// <inheritdoc />
        public IReadOnlyList<long> Ids => _abandoned ? [] : [.. _entities.Select(static entity => long.CreateChecked(entity.Id))];

        /// <inheritdoc />
        public DateTimeOffset? Stamp { get; private set; }

        /// <summary>The frame the save is enlisted with and how.</summary>
        /// <exception cref="InvalidOperationException">The save runs on its own.</exception>
        private EnlistedSaveSite Enlisted => _enlisted ?? throw new InvalidOperationException("A save of its own is no group of another operation's frame.");

        /// <inheritdoc />
        public async Task EnlistAsync()
        {
            EnlistedSaveSite enlisted = Enlisted;
            try
            {
                // --- Step 1: the shape and the ceilings; a payload that fails them goes no further,
                // its errors the host's ---
                SaveShape.Check(Payload, _root, _stack.Limits, _operation.Walker, _errors);
                if (_errors.HasErrors)
                {
                    _abandoned = true;
                    return;
                }

                // --- The host's rounds, loader and schema; the group's own frame and context ---
                OpenEnlisted(enlisted);
                using IDisposable flow = FrameFlow.Enter(Frame);

                // --- Step 3: preprocessing, the pipeline's then the hook's, the writer's server-owned
                // members re-applied after the resets ---
                Walk();
                _supplied = Supplied(enlisted.Options.ServerOwned);
                foreach (SaveRow row in _rows)
                {
                    Normalize(row);
                }

                Reapply(updatesOnly: false);
                await _behavior.PreprocessAsync(_context).ConfigureAwait(false);
                Walk();
                CheckFilled();
                CheckTemporaryReferences();

                // --- One write per root id and target in the frame; then the loads of the checks ---
                HostRounds(enlisted).Claim(_stack, _roots.Where(static row => !row.IsNew).Select(static row => row.OriginalId));
                RequestUniqueness();
                RequestTree();

                // --- Step 4: the ids; from here the group's participants validate, and enlist ---
                ReserveIds();
                Frame.BeginComposing();
                bool updates = _roots.Exists(static row => !row.IsNew);
                if (_deferred || updates)
                {
                    _loader.Enqueue(this, new PipelineLoad(ComposeEnlistedRound));
                }

                if (_deferred || updates || _probes.Count > 0 || _tree is not null)
                {
                    // The checks run once the round that serves the loads was dispatched, the round a
                    // load requested now rides.
                    _settleRound = _scheduler.Round;
                    _scheduler.RequestRound();
                }
                else
                {
                    Settle();
                }

                // --- The validators now; with deferred ids once the round assigned them, the writer
                // parked until it did ---
                if (!_deferred)
                {
                    StartValidators();
                    return;
                }

                await _loader.LoadAsync().ConfigureAwait(false);
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
            if (_abandoned)
            {
                return Task.CompletedTask;
            }

            // --- The checks, after the round that served the group's loads ---
            if (_settleRound is int due && round >= due)
            {
                Settle();
            }

            // --- The round the group's validators resume in, counted from the round they started in ---
            if (_validatorsStarted is int started)
            {
                _context.Round = round - started + 1;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public void CompleteRounds()
        {
            if (!_abandoned && _settled)
            {
                CheckValueSets();
            }
        }

        /// <inheritdoc />
        public async ValueTask ComposeAsync(IDataBatch batch, bool first)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (_abandoned)
            {
                return;
            }

            // --- The emitter, under the target's authority, then its hook and its effects: called on
            // the first entry, replayed on a re-entry; the ordinals they take mark the group's
            // statements ---
            int start = batch is IBatchOrdinals ordinals ? ordinals.NextOrdinal() : -1;
            Authorize(batch, StackTables);
            SaveHandle handle = batch.Save(_entities, _options.Concurrency);
            _handle = handle;
            if (first)
            {
                _firstHandle = handle;
                await ContributeAsync(batch, handle).ConfigureAwait(false);
            }
            else
            {
                EnsureSameNames(handle);
                _participants!.Replay(batch);
            }

            _span = batch is IBatchOrdinals marked ? (start, marked.NextOrdinal()) : null;
        }

        // Spec 0015 §13.3 and §14.2: a failure the executor attributes to one of the group's
        // statements is translated against the group's rows and resource, and each error re-homed
        // onto the host's paths; a missing row is not found at the mapped path, never a 404.
        /// <inheritdoc />
        public Exception? Translate(DataAccessException failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            if (_span is not (int start, int end) || failure.StatementOrdinal is not int ordinal || ordinal < start || ordinal >= end)
            {
                return null;
            }

            Exception translated = PersistFailures.Translate(failure, FailureContext(), _operation.Metrics) ?? failure;
            return translated switch
            {
                ValidationException validation => new ValidationException([.. validation.Errors.Select(_errors.Rehome)]),
                NotFoundException missing => new ValidationException(
                    [_errors.Rehome(new ValidationError(ValidationPath.Root, ValidationCodes.EntityNotFound, Arguments(("ids", missing.Ids))))]),
                _ => translated,
            };
        }

        // Spec 0015 §13.3: the rows the writer holds carry the batch stamp once the host committed;
        // then the target's post-commit phase runs, each failure contained.
        /// <inheritdoc />
        async Task IEnlistedGroup.AfterCommitAsync(BatchOutcome? committed)
        {
            if (_abandoned || _handle is not SaveHandle handle)
            {
                return;
            }

            // --- The stamp, when the batch's results were read: a proven commit's were never read ---
            DateTimeOffset stamp = default;
            if (handle.Receipt.IsCompleted)
            {
                stamp = handle.Receipt.Value.Stamp;
                Stamp = stamp;
                StampRows(stamp);
            }

            SavePersistOutcome<TEntity> outcome = new()
            {
                Entities = _entities,
                Stamp = stamp,
                Context = _operation.Accessor.Current,
                VersionTags = TagsAfter(committed),
                UserVersionTags = committed?.UserVersionTags,
                Options = _options,
                BeforeImages = _context.BeforeImages,
            };
            await AfterCommitAsync(_behavior, outcome).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public void Release()
        {
            _reservation?.Dispose();
        }

        /// <summary>The rounds of the host the save is enlisted with.</summary>
        /// <param name="enlisted">The enlistment.</param>
        /// <returns>The rounds.</returns>
        private static EnlistmentRounds HostRounds(EnlistedSaveSite enlisted)
        {
            return enlisted.Site.Host.Rounds;
        }

        /// <summary>Opens the save over its host's rounds: their schema map, scheduler and loader, and the context the target's hooks see.</summary>
        /// <param name="enlisted">The enlistment.</param>
        private void OpenEnlisted(EnlistedSaveSite enlisted)
        {
            EnlistmentRounds rounds = HostRounds(enlisted);
            _request = _operation.Accessor.Current;
            _map = rounds.Map;
            _scheduler = rounds.Scheduler;
            _loader = rounds.Loader;
            _context = NewContext(Frame);
        }

        // Spec 0015 §13.3: EnlistSaveOptions.ServerOwned names server-owned members the writer
        // supplies, such as a job entity's job id; the platform's own audit columns and the active
        // flag are never among them.
        /// <summary>Reads the server-owned values the writer supplied on each root, before the resets.</summary>
        /// <param name="names">The members the writer supplies.</param>
        /// <returns>Each root's values, in root order.</returns>
        /// <exception cref="InvalidOperationException">A name is an audit column, the active flag, or no server-owned member of the root.</exception>
        private (PropertyMetadata Property, object? Value)[][] Supplied(IReadOnlySet<string> names)
        {
            if (names.Count == 0)
            {
                return [];
            }

            var rules = RowRules.Of(_root);
            PropertyMetadata[] supplied = [.. names.Select(name => PlatformOwned.Contains(name, StringComparer.Ordinal)
                ? throw new InvalidOperationException(
                    $"The enlisted save of {_stack.Resource} names {name} among the server-owned members its writer supplies. The platform alone writes the audit columns and the active flag.")
                : rules.ServerOwned.FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.Ordinal))
                    ?? throw new InvalidOperationException(
                        $"The enlisted save of {_stack.Resource} names {name} among the server-owned members its writer supplies, but {_root.Name} has no such server-owned member. Name a server-owned property of the root entity."))];
            return [.. _roots.Select(root => supplied.Select(property => (property, property.Getter(root.Row))).ToArray())];
        }

        /// <summary>Writes the supplied server-owned values back onto the roots.</summary>
        /// <param name="updatesOnly">Whether only the update roots take them, after their before images restored the stored values.</param>
        private void Reapply(bool updatesOnly)
        {
            for (int index = 0; index < _supplied.Length; index++)
            {
                SaveRow root = _roots[index];
                if (updatesOnly && root.IsNew)
                {
                    continue;
                }

                foreach ((PropertyMetadata property, object? value) in _supplied[index])
                {
                    property.Setter(root.Row, value);
                }
            }
        }

        // Spec 0015 §6.6 for an enlisted save, after the round that served its loads: the deferred
        // ids, existence (a missing row an error, never a 404), the stamps under Check (a default
        // one an error; one that differs the persist's to refuse, its comparison running against
        // the writer's stamp), the before images, ownership, child identity, uniqueness and the
        // tree. The references are not pre-loaded: their constraints check them at persist.
        /// <summary>Runs the checks that follow the round that served the group's loads, once.</summary>
        private void Settle()
        {
            _settled = true;
            _settleRound = null;

            // --- The ids of a reservation that rode the round ---
            if (_deferred)
            {
                IdReservation reservation = _reservation
                    ?? throw new InvalidOperationException("A deferred reservation rides the host's round, which composed none.");
                reservation.Assign(Payload);
                PrimeNewRows();
            }

            // --- Existence, unfiltered: a missing row is not found at the root's key ---
            var stored = _stored.ToDictionary(static entity => long.CreateChecked(entity.Id));
            foreach (SaveRow root in _roots)
            {
                if (root.IsNew)
                {
                    continue;
                }

                if (stored.TryGetValue(root.OriginalId, out TEntity? before))
                {
                    root.Before = before;
                    Match(root, before);
                }
                else
                {
                    _errors.Add(root.At(_root.Key.Name), ValidationCodes.EntityNotFound, Arguments(("ids", new[] { root.OriginalId })));
                }
            }

            // --- Under Check, a stamp the writer never loaded ---
            if (_options.Concurrency == ConcurrencyMode.Check)
            {
                foreach (SaveRow root in _roots)
                {
                    if (root.IsNew || root.Before is null || root.Rules.Stamp is not PropertyMetadata stamp)
                    {
                        continue;
                    }

                    if (_stamps[root.RootIndex] is not DateTimeOffset echoed || echoed == default)
                    {
                        _errors.Add(root.At(stamp.Name), ValidationCodes.ConcurrencyStampRequired);
                    }
                }
            }

            // --- The before images become the validators', and answer their loads by id ---
            var images = new TEntity?[_entities.Count];
            foreach (SaveRow root in _roots)
            {
                if (root.Before is TEntity before)
                {
                    images[root.RootIndex] = before;
                    _loader.Prime<TEntity, TKey>(before);
                }
            }

            _context.BeforeImages = images;

            // --- Ownership, then the writer's own server-owned values and stamps over the stored ones ---
            CheckOwnership();
            Reapply(updatesOnly: true);
            RestoreStamps();
            CheckChildIdentity();
            CheckUniqueness();
            if (_tree is TreeCycleValidator<TEntity>.TreeCheck tree)
            {
                TreeCycleValidator<TEntity>.Report(tree, _context);
            }

            // --- Deferred ids: the validators start now, with them ---
            if (_deferred)
            {
                StartValidators();
            }
        }

        /// <summary>Under Check, puts the stamp the writer carries back on each update root, so the emitter compares it with the stored one.</summary>
        private void RestoreStamps()
        {
            if (_options.Concurrency != ConcurrencyMode.Check)
            {
                return;
            }

            foreach (SaveRow root in _roots)
            {
                if (!root.IsNew && root.Rules.Stamp is PropertyMetadata stamp && _stamps[root.RootIndex] is DateTimeOffset echoed && echoed != default)
                {
                    stamp.Setter(root.Row, echoed);
                }
            }
        }

        /// <summary>Composes the group's own loads of its host's round: a deferred reservation, and the update roots as stored, unfiltered.</summary>
        /// <param name="batch">The round's batch.</param>
        /// <returns>Reads the loads' results into the save once the batch ran.</returns>
        private Action ComposeEnlistedRound(IDataBatch batch)
        {
            // --- The reservation statement, when the buffer fell short; a recomposition reserves afresh ---
            if (_deferred)
            {
                _reservation?.Dispose();
                _reservation = null;
                _reservation = _operation.Allocator.Reserve(batch, typeof(TEntity), Payload);
            }

            // --- The update roots as stored, every mapped property and every collection the payload carries ---
            TKey[] updates = [.. _roots.Where(static row => !row.IsNew).Select(static row => ((TEntity)row.Row).Id)];
            if (updates.Length == 0)
            {
                return static () => { };
            }

            EntityDescriptor descriptor = _map.Descriptor(_root);
            string key = descriptor.Key.Name;
            BatchResult<EntityQueryResult<TEntity>> rows = batch.Query(new EntityQuery<TEntity>
            {
                Select = string.Join(", ", _map.Declared(_root).Select(static property => property.Name)),
                AllProperties = true,
                Restrictions = [new KeySetRestriction(key, KeyTables.Bind(batch, updates).Name)],
                Children = PresentCollections(),
                OrderBy = key,
            });
            return () => _stored = rows.Value.Entities;
        }

        /// <summary>Stamps the roots the writer holds with the batch stamp: every root's modification, and a new root's creation.</summary>
        /// <param name="stamp">The batch stamp.</param>
        private void StampRows(DateTimeOffset stamp)
        {
            PropertyMetadata? created = _root.Properties.FirstOrDefault(static property => property.Name == nameof(TopLevelEntity.CreatedAt));
            foreach (SaveRow root in _roots)
            {
                root.Rules.Stamp?.Setter(root.Row, stamp);
                if (root.IsNew)
                {
                    created?.Setter(root.Row, stamp);
                }
            }
        }
    }
}
