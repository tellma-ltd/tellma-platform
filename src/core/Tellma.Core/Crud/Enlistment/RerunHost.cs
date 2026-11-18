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
using Tellma.Core.Data.Ids;

namespace Tellma.Core.Crud.Enlistment
{
    // Spec 0015 §5.1 step 1 of a re-entry: the host's validators run again in memory, and the writes
    // and enlistments they make are ignored; the groups their first run enlisted are recomposed and
    // replayed at their original positions. A re-run validator that wires a reference to a row it
    // enlists reads the row's id from what its enlistment answers, so the re-run's n-th enlistment is
    // answered from the first run's n-th: the same target and kind, the same rows' count, and the
    // first run's ids on its rows. One that differs means what the validator read moved, which is
    // stale.
    /// <summary>The host a re-run of a pipeline's validators sees: it answers each enlistment from the first run's.</summary>
    /// <param name="first">The frame of the first run, whose groups answer the re-run's enlistments.</param>
    /// <param name="confirmations">The re-run's confirmation frame.</param>
    /// <param name="metadata">The tenant model's entity metadata, which sets a re-run row's id.</param>
    internal sealed class RerunHost(PipelineFrame first, IConfirmations confirmations, IEntityMetadataProvider metadata) : IWriteHost, IEnlistmentHost
    {
        /// <summary>The first run's groups, in enlistment order; empty when it enlisted none.</summary>
        private readonly IReadOnlyList<IEnlistedGroup> _recorded = first.HasRounds ? first.Groups : [];

        /// <summary>The next of the first run's groups to answer an enlistment.</summary>
        private int _next;

        /// <inheritdoc />
        public WriteHostKind Kind => first.Kind;

        /// <inheritdoc />
        public string Operation => first.Operation;

        /// <inheritdoc />
        public IConfirmations Confirmations { get; } = confirmations;

        /// <inheritdoc />
        /// <exception cref="StaleContextException">The re-run enlisted another write than the first run did at this position.</exception>
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
                object row = rows[index] ?? throw Stale();
                long id = recorded.Ids[index];
                long own = PayloadRows.KeyOf(metadata.Get(recorded.Target.EntityType), row);
                if (own <= 0)
                {
                    PayloadRows.SetId(key, row, id);
                }
                else if (own != id)
                {
                    throw Stale();
                }
            }

            return Task.FromResult(new EnlistedSave<TTarget>(rows, recorded.Target.Resource));
        }

        /// <inheritdoc />
        /// <exception cref="StaleContextException">The re-run enlisted another write than the first run did at this position.</exception>
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
                : throw Stale();
        }

        /// <summary>The first run's group at the re-run's next position, refused as stale when it differs.</summary>
        /// <param name="target">The entity type the re-run enlists.</param>
        /// <param name="kind">The enlistment's kind.</param>
        /// <param name="count">The rows or ids the re-run enlists.</param>
        /// <returns>The first run's group.</returns>
        /// <exception cref="StaleContextException">The first run enlisted no write at the position, or another one.</exception>
        private IEnlistedGroup Next(Type target, string kind, int count)
        {
            int position = Interlocked.Increment(ref _next) - 1;
            if (position >= _recorded.Count)
            {
                throw Stale();
            }

            IEnlistedGroup recorded = _recorded[position];
            return target.IsAssignableFrom(recorded.Target.EntityType) && recorded.Kind == kind && recorded.Ids.Count == count
                ? recorded
                : throw Stale();
        }

        /// <summary>The refusal of a re-run whose enlistments differ from the first run's: what its validators read moved.</summary>
        /// <returns>The exception.</returns>
        private static StaleContextException Stale()
        {
            return new StaleContextException([]);
        }
    }
}
