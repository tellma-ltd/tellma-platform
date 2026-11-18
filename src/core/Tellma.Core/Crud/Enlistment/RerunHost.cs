// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Crud.Validation;
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §5.1 step 1 of a re-entry: the host's validators run again in memory, and the writes
    // and enlistments they make are ignored; the groups their first run enlisted are recomposed and
    // replayed at their original positions. A re-run validator that wires a reference to a row it
    // enlists reads the row's id from what its enlistment answers, so each re-run enlistment is
    // answered from the group the same task of the operation enlisted at the same step of its first
    // run: the same target and kind, the same rows' count, and the first run's ids on its rows. The
    // tasks' interleaving never decides it, since the re-run's loads are answered at once and its
    // tasks run in another order. A step with no such group, one the first run took while another
    // flow of its task was parked, or a first-run step the re-run never takes, means what the
    // validators read moved or the match cannot be told: stale, never another group's ids. The groups
    // an action's method enlisted are replayed as they are, since a re-run never invokes the method.
    /// <summary>The host a re-run of a pipeline's validators sees: it answers each enlistment from the group the same task enlisted at the same step of the first run.</summary>
    /// <param name="first">The frame of the first run, whose groups answer the re-run's enlistments.</param>
    /// <param name="scheduler">The re-run's scheduler, whose tasks carry the first run's slots.</param>
    /// <param name="confirmations">The re-run's confirmation frame.</param>
    /// <param name="metadata">The tenant model's entity metadata, which sets a re-run row's id.</param>
    internal sealed class RerunHost(PipelineFrame first, ValidationScheduler scheduler, IConfirmations confirmations, IEntityMetadataProvider metadata) : IWriteHost, IEnlistmentHost
    {
        /// <summary>Guards the steps, the matches and the refusal.</summary>
        private readonly Lock _gate = new();

        /// <summary>The first run's groups the operation's own tasks enlisted, in enlistment order; empty when it enlisted none.</summary>
        private readonly IReadOnlyList<EnlistmentRecord> _recorded = first.HasRounds ? [.. first.Records.Where(static record => record.Slot is not null)] : [];

        /// <summary>The enlistments each of the re-run's tasks made so far, by its slot.</summary>
        private readonly Dictionary<int, int> _steps = [];

        /// <summary>The first run's groups a re-run enlistment was answered from.</summary>
        private readonly HashSet<EnlistmentRecord> _matched = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether a re-run enlistment was refused as stale, whether or not its writer let the refusal through.</summary>
        private bool _refused;

        /// <inheritdoc />
        public WriteHostKind Kind => first.Kind;

        /// <inheritdoc />
        public string Operation => first.Operation;

        /// <inheritdoc />
        public IConfirmations Confirmations { get; } = confirmations;

        /// <inheritdoc />
        /// <exception cref="StaleContextException">The re-run enlisted another write than the same task did at the same step of the first run.</exception>
        public Task<EnlistedSave<TTarget>> EnlistSaveAsync<TTarget>(IEnlists<TTarget> writer, IReadOnlyList<TTarget> rows, EnlistSaveOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(rows);
            ArgumentNullException.ThrowIfNull(options);
            IEnlistedGroup recorded = Next(typeof(TTarget), CrudOperationNames.Save, rows.Count);

            // --- The first run's ids on the re-run's rows: a new row takes its id, an update keeps its own ---
            PropertyMetadata key = metadata.Get(recorded.Target.EntityType).Key;
            for (int index = 0; index < rows.Count; index++)
            {
                object row = rows[index] ?? throw Refuse();
                long id = recorded.Ids[index];
                long own = PayloadRows.KeyOf(metadata.Get(recorded.Target.EntityType), row);
                if (own <= 0)
                {
                    PayloadRows.SetId(key, row, id);
                }
                else if (own != id)
                {
                    throw Refuse();
                }
            }

            return Task.FromResult(new EnlistedSave<TTarget>(rows, recorded.Target.Resource));
        }

        /// <inheritdoc />
        /// <exception cref="StaleContextException">The re-run enlisted another write than the same task did at the same step of the first run.</exception>
        public EnlistedDelete<TKey> EnlistDelete<TTarget, TKey>(IEnlists<TTarget> writer, IReadOnlyList<TKey> ids, EnlistDeleteOptions options)
            where TTarget : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            IEnlistedGroup recorded = Next(typeof(TTarget), CrudOperationNames.Delete, ids.Count);
            return ids.Select(static id => long.CreateChecked(id)).SequenceEqual(recorded.Ids)
                ? new EnlistedDelete<TKey>(ids, recorded.Target.Resource)
                : throw Refuse();
        }

        /// <summary>
        ///     Refuses as stale a re-run that left a step of the first run's untaken, or had one of
        ///     its enlistments refused: what its validators read moved.
        /// </summary>
        /// <exception cref="StaleContextException">The re-run's enlistments differ from the first run's.</exception>
        public void EnsureMatched()
        {
            lock (_gate)
            {
                if (_refused || _matched.Count != _recorded.Count)
                {
                    throw Stale();
                }
            }
        }

        /// <summary>The first run's group the same task enlisted at the re-run's next step of it, refused as stale when it differs.</summary>
        /// <param name="target">The entity type the re-run enlists.</param>
        /// <param name="kind">The enlistment's kind.</param>
        /// <param name="count">The rows or ids the re-run enlists.</param>
        /// <returns>The first run's group.</returns>
        /// <exception cref="StaleContextException">
        ///     The enlistment comes from no task of the operation's own; the first run's task enlisted
        ///     nothing at that step, another write, or one while another of its flows was parked.
        /// </exception>
        private IEnlistedGroup Next(Type target, string kind, int count)
        {
            int? slot = scheduler.CurrentSlot;
            lock (_gate)
            {
                // --- The task's next step, and the first run's group at the same step of the same task ---
                EnlistmentRecord? recorded = null;
                if (slot is int task)
                {
                    int step = _steps.GetValueOrDefault(task);
                    _steps[task] = step + 1;
                    recorded = _recorded.FirstOrDefault(record => record.Slot == task && record.Step == step);
                }

                // --- The same write, told apart from every other: else stale, the refusal kept ---
                if (recorded is null
                    || recorded.Raced
                    || !target.IsAssignableFrom(recorded.Group.Target.EntityType)
                    || recorded.Group.Kind != kind
                    || recorded.Group.Ids.Count != count
                    || !_matched.Add(recorded))
                {
                    _refused = true;
                    throw Stale();
                }

                return recorded.Group;
            }
        }

        /// <summary>Refuses a re-run enlistment whose rows differ from the first run's group, keeping the refusal.</summary>
        /// <returns>The exception.</returns>
        private StaleContextException Refuse()
        {
            lock (_gate)
            {
                _refused = true;
            }

            return Stale();
        }

        /// <summary>The refusal of a re-run whose enlistments differ from the first run's: what its validators read moved.</summary>
        /// <returns>The exception.</returns>
        private static StaleContextException Stale()
        {
            return new StaleContextException([]);
        }
    }
}
