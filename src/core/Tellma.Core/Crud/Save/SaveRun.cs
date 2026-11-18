// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Frozen;
using System.Numerics;
using System.Runtime.CompilerServices;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Ids;
using Tellma.Core.Data.Queries;
using Tellma.Core.Data.Trees;

namespace Tellma.Core.Crud.Save
{
    // One save's state through spec 0015 §6.1 steps 1 to 6. The steps run in the spec's order:
    // the shape (§6.1 step 1), the type-level grant (§9.1), preprocessing (§6.2, §6.3), the ids
    // (§6.4), round trip 1 with the pipeline's own loads (§6.1 step 5), the ordered checks of §6.6
    // and the validators' rounds (§7.2), the value sets, then the raise order of §7.5. This part
    // holds the steps up to round trip 1; the round's composition and the checks that follow it
    // are the other parts.
    /// <summary>One save of one stack, from its payload's shape to the outcome of its validation.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="operation">The operation, which holds the scope's services.</param>
    /// <param name="stack">The stack.</param>
    /// <param name="behavior">The service's hooks.</param>
    /// <param name="entities">The payload's roots.</param>
    /// <param name="options">The save's options.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    internal sealed partial class SaveRun<TEntity, TKey>(
        SaveOperation operation, StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The code of every refusal the save raises for a missing grant.</summary>
        private const string ForbiddenCode = "forbidden";

        /// <summary>The value sets' spellings, by value ignoring case, built once per set.</summary>
        private static readonly ConditionalWeakTable<ValueSetDescriptor, FrozenDictionary<string, string>> Spellings = [];

        /// <summary>The operation, which holds the scope's services.</summary>
        private readonly SaveOperation _operation = operation;

        /// <summary>The stack.</summary>
        private readonly StackDescriptor _stack = stack;

        /// <summary>The service's hooks.</summary>
        private readonly IEntityBehavior<TEntity, TKey> _behavior = behavior;

        /// <summary>The payload's roots, in payload order.</summary>
        private readonly IReadOnlyList<TEntity> _entities = entities;

        /// <summary>The save's options.</summary>
        private readonly SaveOptions _options = options;

        /// <summary>Cancels the save.</summary>
        private readonly CancellationToken _cancellationToken = cancellationToken;

        /// <summary>The roots' entity.</summary>
        private readonly EntityMetadata _root = operation.Metadata.Get(typeof(TEntity));

        /// <summary>The save's errors, every step's.</summary>
        private readonly ValidationErrors<TEntity> _errors = new();

        /// <summary>Every payload row at every depth, each before its children.</summary>
        private readonly List<SaveRow> _rows = [];

        /// <summary>The payload's roots, in payload order.</summary>
        private readonly List<SaveRow> _roots = [];

        /// <summary>The concurrency stamp each root echoed, read before the server-owned columns were reset; null for an entity with none.</summary>
        private readonly DateTimeOffset?[] _stamps = new DateTimeOffset?[entities.Count];

        /// <summary>The references the save validates: each row's foreign key values above zero.</summary>
        private readonly List<(SaveRow Row, ReferenceMetadata Reference, long Value)> _references = [];

        /// <summary>The referenced ids to load, by target entity.</summary>
        private readonly Dictionary<EntityMetadata, HashSet<long>> _targets = new(ReferenceEqualityComparer.Instance);

        /// <summary>The uniqueness checks, one per entity and unique property the payload holds values of.</summary>
        private readonly List<UniqueProbe> _probes = [];

        /// <summary>The request context the save started under.</summary>
        private RequestContext _request = null!;

        /// <summary>The tenant's schema map.</summary>
        private QueryexSchemaMap _map = null!;

        /// <summary>The context the hooks and validators see.</summary>
        private SaveContext<TEntity> _context = null!;

        /// <summary>The operation's loader.</summary>
        private ContextLoader _loader = null!;

        /// <summary>The scheduler of the validators' rounds.</summary>
        private ValidationScheduler _scheduler = null!;

        /// <summary>The ids' reservation; null while none is held.</summary>
        private IdReservation? _reservation;

        /// <summary>Whether the reservation rides round trip 1, its ids assigned after it.</summary>
        private bool _deferred;

        /// <summary>The tree check, its load requested; null when the stack is no tree or no row names a parent.</summary>
        private TreeCycleValidator<TEntity>.TreeCheck? _tree;

        /// <summary>The payload's roots, as the allocator and the walkers take them.</summary>
        private IReadOnlyList<object> Payload => _entities;

        /// <summary>Runs the save.</summary>
        /// <returns>The saved rows' ids, and the rows when asked for.</returns>
        public async Task<EntitiesResult<TEntity>> RunAsync()
        {
            // --- Step 1: the shape and the ceilings, before any round trip ---
            SaveShape.Check(Payload, _root, _stack.Limits, _operation.Walker, _errors);
            if (_errors.HasErrors)
            {
                throw new ValidationException(_errors.Items);
            }

            // --- Step 2: the type-level grant ---
            _ = await _operation.Evaluator.RequireAsync(_stack.Resource, AccessActions.Save, _cancellationToken).ConfigureAwait(false);

            // --- The frame, the context and the loader the hooks and validators see ---
            using ConfirmationFrame frame = _operation.Confirmations.Frame(_stack.Resource, CrudOperationNames.Save);
            try
            {
                Open(frame);

                // --- Step 3: preprocessing, the pipeline's then the hook's; the rows are walked
                // again after the hook, which may add or drop children ---
                Walk();
                foreach (SaveRow row in _rows)
                {
                    Normalize(row);
                }

                await _behavior.PreprocessAsync(_context).ConfigureAwait(false);
                Walk();
                CheckFilled();
                CheckTemporaryReferences();

                // --- The loads round trip 1 carries for the pipeline's own checks ---
                CollectReferences();
                RequestUniqueness();
                RequestTree();

                // --- Step 4: the ids, at once from a warm buffer, else riding round trip 1 ---
                ReserveIds();

                // --- Steps 5 and 6: round trip 1, the ordered checks, the validators and their rounds ---
                if (NeedsFirstRound())
                {
                    _loader.Enqueue(this, new FirstRound(this));
                }

                if (_loader.HasPending)
                {
                    _scheduler.RequestRound();
                }

                if (_deferred)
                {
                    // Round trip 1 then carries no validator's load: the validators keep their rounds.
                    _scheduler.AddPipelineRound();
                }
                else
                {
                    StartValidators();
                }

                _ = await _scheduler.RunAsync().ConfigureAwait(false);
                if (!_settled)
                {
                    // Nothing needed round trip 1: the one-round-trip create runs the checks without it.
                    await SettleAsync().ConfigureAwait(false);
                }

                // --- The value sets, then the outcome in the order a caller can act on ---
                CheckValueSets();
                ValidationOutcome.Raise(staleStamp: null, _errors, frame);

                // --- The persist seam ---
                return Persist();
            }
            finally
            {
                // A save that did not persist returns its ids: nothing outside the process saw them.
                _reservation?.Dispose();
            }
        }

        /// <summary>The persist step, which follows a save's validation.</summary>
        /// <returns>The save's result.</returns>
        /// <exception cref="NotSupportedException">Always: the persist step is not built yet.</exception>
        private EntitiesResult<TEntity> Persist()
        {
            throw new NotSupportedException($"The pipeline of {_stack.Resource} validates a save but does not persist it yet.");
        }

        /// <summary>Opens the save's context: the schema map, the scheduler, the loader and the context the hooks see.</summary>
        /// <param name="frame">The save's confirmation frame.</param>
        private void Open(ConfirmationFrame frame)
        {
            _request = _operation.Accessor.Current;
            MultilingualShape shape = _request.TenantSettings?.Shape
                ?? throw new InvalidOperationException("The tenant's settings are not loaded in this scope, so the save has no schema to validate under.");
            _map = _operation.Schemas.Map(shape);

            // The dispatch closes over this run; the loader parks on the scheduler, and decides
            // visibility for the user each round is composed for.
            _scheduler = new ValidationScheduler(_stack.Limits.MaxValidationRounds, CrudOperationNames.Save, DispatchAsync, _operation.Metrics, _cancellationToken);
            _loader = new ContextLoader(_operation.Metadata, _map, _scheduler, ReadDecision);
            _context = new SaveContext<TEntity>
            {
                Entities = _entities,
                Options = _options,
                Context = _request,
                Loader = _loader,
                Errors = _errors,
                Host = new PipelineWriteHost(_stack.Resource, CrudOperationNames.Save, frame),
                CancellationToken = _cancellationToken,
                NewRows = [.. _entities.Select(static entity => entity.Id <= TKey.Zero)],
            };
        }

        /// <summary>Walks the payload into its rows at every depth, each with its path and its owner.</summary>
        private void Walk()
        {
            _rows.Clear();
            _roots.Clear();
            var rules = RowRules.Of(_root);
            for (int index = 0; index < _entities.Count; index++)
            {
                SaveRow root = new(rules, _entities[index], ValidationPath.Root.Index(index), index, owner: null, collection: null);
                _roots.Add(root);
                _rows.Add(root);
                Descend(root);
            }
        }

        /// <summary>Adds a row's children, and theirs, in collection order.</summary>
        /// <param name="owner">The row.</param>
        private void Descend(SaveRow owner)
        {
            foreach (ChildCollectionMetadata collection in owner.Entity.Children)
            {
                if (collection.Getter(owner.Row) is not IReadOnlyList<object> children)
                {
                    continue;
                }

                var rules = RowRules.Of(collection.Child);
                ValidationPath path = owner.Path.Property(collection.Property);
                for (int index = 0; index < children.Count; index++)
                {
                    SaveRow child = new(rules, children[index], path.Index(index), owner.RootIndex, owner, collection);
                    owner.Children.Add(child);
                    _rows.Add(child);
                    Descend(child);
                }
            }
        }

        // Spec 0015 §6.2 and §6.3, before the hook: the expected stamp is read before the server-owned
        // columns are reset; derived and server-owned columns are reset on every row (an update's
        // server-owned values are restored from its before image after round trip 1), and a new
        // row of an activatable entity is active (§12.2); strings are trimmed; nested children take
        // their parent key from the nesting; unconfigured twins are dropped; value-set values take
        // the set's spelling.
        /// <summary>Prepares one row for the hook.</summary>
        /// <param name="row">The row.</param>
        private void Normalize(SaveRow row)
        {
            RowRules rules = row.Rules;
            object entity = row.Row;

            // --- The stamp the client echoed, then the resets ---
            if (row.Owner is null && rules.Stamp is PropertyMetadata stamp)
            {
                _stamps[row.RootIndex] = (DateTimeOffset?)stamp.Getter(entity);
            }

            row.Entity.ResetDerived(entity);
            row.Entity.ResetServerOwned(entity);

            // Every row is created active, whatever its class initialises the flag to: the insert
            // writes the row's value, so the reset to a fresh instance's default is not enough.
            if (row.IsNew && entity is IActivatable activatable)
            {
                activatable.IsActive = true;
            }

            // --- Trimming: an empty nullable string is null ---
            foreach (PropertyMetadata property in rules.Trimmed)
            {
                if (property.Getter(entity) is string text)
                {
                    string trimmed = text.Trim();
                    if (trimmed.Length == 0 && property.IsNullable)
                    {
                        property.Setter(entity, null);
                    }
                    else if (trimmed.Length != text.Length)
                    {
                        property.Setter(entity, trimmed);
                    }
                }
            }

            // --- The nesting wins over any parent key the payload carried ---
            if (row is { Collection: ChildCollectionMetadata collection, Owner: SaveRow owner })
            {
                PayloadRows.SetId(collection.ParentKey, entity, owner.Id);
            }

            // --- Twins the tenant has not configured: dropped, or refused under an import ---
            int configured = (int)_map.Shape;
            foreach ((PropertyMetadata twin, int position) in rules.Twins)
            {
                if (position <= configured)
                {
                    continue;
                }

                if (_options.Source == SaveSource.Import && twin.Getter(entity) is object value && !(value is string text && text.Length == 0))
                {
                    _errors.Add(row.At(twin.Name), ValidationCodes.ImportLanguageNotConfigured);
                }

                twin.Setter(entity, null);
            }

            // --- Value-set values in the set's spelling ---
            foreach (PropertyMetadata property in rules.ValueSets)
            {
                if (property.Getter(entity) is string value && Spelling(property.ValueSet!, value) is string spelled && !string.Equals(spelled, value, StringComparison.Ordinal))
                {
                    property.Setter(entity, spelled);
                }
            }
        }

        /// <summary>Refuses a defaulted property the hook left empty: it is required after all.</summary>
        private void CheckFilled()
        {
            foreach (SaveRow row in _rows)
            {
                foreach (PropertyMetadata property in row.Rules.Defaulted)
                {
                    if (property.Getter(row.Row) is null or "")
                    {
                        _errors.Add(row.At(property.Name), ValidationCodes.Required, Arguments(("property", property.Name)));
                    }
                }
            }
        }

        /// <summary>Refuses a temporary id that no row of the payload defines, at the reference that holds it.</summary>
        private void CheckTemporaryReferences()
        {
            HashSet<(Type Entity, long Id)> defined = [.. _rows.Where(static row => row.OriginalId < 0).Select(static row => (row.Entity.ClrType, row.OriginalId))];
            foreach (SaveRow row in _rows)
            {
                foreach (ReferenceMetadata reference in Checked(row))
                {
                    if (PayloadRows.ValueOf(reference.ForeignKey, row.Row) is long value && value < 0 && !defined.Contains((reference.Target.ClrType, value)))
                    {
                        _errors.Add(row.At(reference.ForeignKey.Name), ValidationCodes.EntityNotFound);
                    }
                }
            }
        }

        // Spec 0015 §7.4: the referenced rows of every foreign key load in round trip 1 under the
        // target's read decision. Whether a value changed is known only once the before images
        // loaded, so every value above zero loads and the check reports the new and changed ones.
        // A temporary id refers to a row of this payload, which needs no load.
        /// <summary>Collects every reference value above zero, by target.</summary>
        private void CollectReferences()
        {
            foreach (SaveRow row in _rows)
            {
                foreach (ReferenceMetadata reference in Checked(row))
                {
                    if (PayloadRows.ValueOf(reference.ForeignKey, row.Row) is long value && value > 0)
                    {
                        _references.Add((row, reference, value));
                        if (!_targets.TryGetValue(reference.Target, out HashSet<long>? ids))
                        {
                            _targets[reference.Target] = ids = [];
                        }

                        _ = ids.Add(value);
                    }
                }
            }
        }

        /// <summary>The references of a row the save validates: every client-written one but the parent key its nesting sets.</summary>
        /// <param name="row">The row.</param>
        /// <returns>The references.</returns>
        private static IEnumerable<ReferenceMetadata> Checked(SaveRow row)
        {
            PropertyMetadata? parentKey = row.Collection?.ParentKey;
            return row.Rules.References.Where(reference => !ReferenceEquals(reference.ForeignKey, parentKey));
        }

        /// <summary>Requests the stored rows holding every unique value of the payload, one grouped load per entity and property.</summary>
        private void RequestUniqueness()
        {
            foreach (SaveRow row in _rows)
            {
                foreach (PropertyMetadata property in row.Rules.Unique)
                {
                    if (property.Getter(row.Row) is not object value)
                    {
                        continue;
                    }

                    UniqueProbe? probe = _probes.Find(candidate => ReferenceEquals(candidate.Rules, row.Rules) && ReferenceEquals(candidate.Property, property));
                    if (probe is null)
                    {
                        // A child property unique only within its parent is scoped by the nesting's parent key.
                        PropertyMetadata? scope = row.Rules.IsGloballyUnique(property) ? null : row.Collection?.ParentKey;
                        probe = UniqueProbe.Create(row.Rules, property, scope);
                        _probes.Add(probe);
                    }

                    probe.Holders.Add((row, value));
                }
            }

            foreach (UniqueProbe probe in _probes)
            {
                probe.Request(_loader);
            }
        }

        /// <summary>Requests the tree check's load, for a tree whose payload names a parent.</summary>
        private void RequestTree()
        {
            if (_root.Tree is null)
            {
                return;
            }

            _tree = new TreeCycleValidator<TEntity>(_operation.Metadata, _operation.Schemas).Request(_context);
        }

        // Spec 0015 §6.4: a warm buffer covers the payload's new rows at once, and the validators
        // start before round trip 1 with the final ids; otherwise the reservation statement rides
        // round trip 1 and the ids are assigned after it.
        /// <summary>Reserves the ids of the payload's new rows, assigning them at once when the buffer covers them.</summary>
        private void ReserveIds()
        {
            if (_rows.TrueForAll(static row => !row.IsNew))
            {
                return;
            }

            int tenantId = _request.RequireTenant().Id;
            _reservation = _operation.Buffer.ReserveBuffered(tenantId, typeof(TEntity), Payload);
            if (_reservation is null)
            {
                _deferred = true;
                return;
            }

            _reservation.Assign(Payload);
        }

        /// <summary>Whether the pipeline's own loads need round trip 1: a deferred reservation, update rows, or references.</summary>
        /// <returns>True when they do.</returns>
        private bool NeedsFirstRound()
        {
            return _deferred || _roots.Exists(static row => !row.IsNew) || _targets.Count > 0;
        }

        /// <summary>Starts the service's validation hook, then every validator of the entity, as tasks of the rounds.</summary>
        private void StartValidators()
        {
            _scheduler.Run(_behavior.GetType().Name, () => _behavior.ValidateAsync(_context));
            foreach (IEntityValidator<TEntity> validator in _operation.Services.GetServices<IEntityValidator<TEntity>>())
            {
                _scheduler.Run(validator.GetType().Name, () => validator.ValidateAsync(_context));
            }
        }

        // Spec 0015 §6.6 step 8: a new or changed value outside its set, unless a validator already
        // recorded an error at its path; an unchanged stored value passes. A hook or a validator may
        // have written a value in another case, which takes the set's spelling here as well.
        /// <summary>Refuses the payload's new and changed values that lie outside their value sets.</summary>
        private void CheckValueSets()
        {
            HashSet<ValidationPath> erred = [.. _errors.Items.Select(static error => error.Path)];
            foreach (SaveRow row in _rows)
            {
                foreach (PropertyMetadata property in row.Rules.ValueSets)
                {
                    if (property.Getter(row.Row) is not string value)
                    {
                        continue;
                    }

                    if (row.Before is object before && string.Equals(property.Getter(before) as string, value, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (Spelling(property.ValueSet!, value) is string spelled)
                    {
                        if (!string.Equals(spelled, value, StringComparison.Ordinal))
                        {
                            property.Setter(row.Row, spelled);
                        }

                        continue;
                    }

                    ValidationPath path = row.At(property.Name);
                    if (!erred.Contains(path))
                    {
                        _errors.Add(path, ValidationCodes.ValueNotInSet, Arguments(("set", property.ValueSet), ("value", value)));
                    }
                }
            }
        }

        /// <summary>The set's spelling of a value that matches one of its values ignoring case.</summary>
        /// <param name="set">The set's name.</param>
        /// <param name="value">The value.</param>
        /// <returns>The set's spelling, or null when the value is outside the set.</returns>
        /// <exception cref="InvalidOperationException">No set has the name.</exception>
        private string? Spelling(string set, string value)
        {
            ValueSetDescriptor descriptor = _operation.ValueSets.Find(set)
                ?? throw new InvalidOperationException($"No value set is named '{set}', though a property draws from it. Contribute the set with the feature that declares the property.");
            FrozenDictionary<string, string> spellings = Spellings.GetValue(
                descriptor,
                static values => values.Values.ToFrozenDictionary(static entry => entry.Value, static entry => entry.Value, StringComparer.OrdinalIgnoreCase));
            return spellings.GetValueOrDefault(value);
        }

        /// <summary>An error's arguments.</summary>
        /// <param name="arguments">Each argument's name and value.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(params (string Name, object? Value)[] arguments)
        {
            return arguments.ToDictionary(static argument => argument.Name, static argument => argument.Value, StringComparer.Ordinal);
        }
    }
}
