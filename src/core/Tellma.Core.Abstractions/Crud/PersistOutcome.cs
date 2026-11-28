// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     What a post-commit participant sees: an operation on the stack that committed, with the
    ///     batch's stamp and the version tags after its bumps. Post-commit work runs outside any
    ///     transaction and is best-effort: a failure is logged and counted, never thrown or retried.
    /// </summary>
    /// <remarks>
    ///     The kind matches the operation, paired with its persist context: a
    ///     <see cref="SavePersistOutcome{TEntity}" />, a <see cref="DeletePersistOutcome{TEntity}" /> or
    ///     an <see cref="ActionPersistOutcome{TEntity}" />. A participant that reacts to one kind
    ///     type-tests the outcome; the service, knowing its key, tests the keyed kind for the ids.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class PersistOutcome<TEntity>
    {
        /// <summary>Initialises the outcome; private protected so the three kinds stay the only ones.</summary>
        private protected PersistOutcome()
        {
        }

        /// <summary>
        ///     The rows of the operation: for a save, the payload with final ids; for an action, the
        ///     target rows, empty on the built-in actions' fast path; for a delete, empty.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TEntity> Entities
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Entities));
        }

        /// <summary>The persist batch's stamp, which every row it wrote carries.</summary>
        public required DateTimeOffset Stamp { get; init; }

        /// <summary>Who ran the operation, in which tenant and culture.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required RequestContext Context
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Context));
        }

        /// <summary>The tenant's version tags after the batch's bumps.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required VersionTagSnapshot VersionTags
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(VersionTags));
        }

        /// <summary>The caller's user-level version tags after the batch's bumps; null when the batch carried no connected user.</summary>
        public UserVersionTagSnapshot? UserVersionTags { get; init; }
    }

    /// <summary>The outcome of a save: the payload with final ids, its before images and its options.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class SavePersistOutcome<TEntity> : PersistOutcome<TEntity>
    {
        /// <summary>The save's options.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SaveOptions Options
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Options));
        }

        /// <summary>The before images, parallel to <see cref="PersistOutcome{TEntity}.Entities" /> and null for an insert; set by the pipeline.</summary>
        internal IReadOnlyList<TEntity?>? BeforeImages { get; init; }

        /// <summary>The before image of a payload row; null for an insert.</summary>
        /// <param name="index">The payload index, zero-based.</param>
        /// <returns>The before image, or null for an insert.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the payload.</exception>
        /// <exception cref="InvalidOperationException">The outcome carries no before images: it was not prepared by the save pipeline.</exception>
        public TEntity? Before(int index)
        {
            ContextRows.CheckIndex(index, Entities.Count);
            return ContextRows.Loaded(BeforeImages)[index];
        }
    }

    /// <summary>The outcome of a delete by ids, by query or with descendants, without the key type; the service sees <see cref="DeletePersistOutcome{TEntity, TKey}" />.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class DeletePersistOutcome<TEntity> : PersistOutcome<TEntity>
    {
        /// <summary>Initialises the view; private protected so the keyed outcome is its one kind.</summary>
        private protected DeletePersistOutcome()
        {
        }
    }

    /// <summary>The outcome of a delete, with the deleted ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public sealed class DeletePersistOutcome<TEntity, TKey> : DeletePersistOutcome<TEntity>
    {
        /// <summary>The deleted ids; for a delete with descendants, the descendants too.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TKey> DeletedIds
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(DeletedIds));
        }
    }

    /// <summary>The outcome of an action, without the key type; the service sees <see cref="ActionPersistOutcome{TEntity, TKey}" />.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class ActionPersistOutcome<TEntity> : PersistOutcome<TEntity>
    {
        /// <summary>Initialises the view; private protected so the keyed outcome is its one kind.</summary>
        private protected ActionPersistOutcome()
        {
        }

        /// <summary>The action's name, as its descriptor names it (<c>activate</c>).</summary>
        /// <exception cref="ArgumentException">Set to null, empty or white space.</exception>
        public required string Action
        {
            get;
            init
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                field = value;
            }
        }

        /// <summary>The action's arguments, as its arguments record; null for an action that takes none.</summary>
        public object? Arguments { get; init; }
    }

    /// <summary>The outcome of an action, with the target ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public sealed class ActionPersistOutcome<TEntity, TKey> : ActionPersistOutcome<TEntity>
    {
        /// <summary>The ids the action targeted, in request order.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TKey> Ids
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Ids));
        }
    }
}
