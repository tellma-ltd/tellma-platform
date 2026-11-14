// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     What a delete's validation hooks see, without the key type: the rows the delete targets and
    ///     the means to load context and record errors. A component generic over the entity alone
    ///     sees this view; the service's own hook sees <see cref="DeleteContext{TEntity, TKey}" />.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class DeleteContext<TEntity>
    {
        /// <summary>Initialises the view; private protected so the keyed context is its one kind.</summary>
        private protected DeleteContext()
        {
        }

        /// <summary>
        ///     The rows the delete targets, loaded under the caller's read filter (unfiltered for an
        ///     enlisted delete). They load with the first validation round, so a validator reads them
        ///     after its first <see cref="IContextLoader.LoadAsync" />.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before the first round ran.</exception>
        public IReadOnlyList<TEntity> Entities
        {
            get => field ?? throw new InvalidOperationException(
                "The rows a delete targets load with the first validation round. Await the context loader's LoadAsync before reading them.");
            internal set => field = value ?? throw new ArgumentNullException(nameof(Entities));
        }

        /// <summary>Who is deleting, in which tenant and culture.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required RequestContext Context
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Context));
        }

        /// <summary>The loader a validator reads context through.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IContextLoader Loader
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Loader));
        }

        /// <summary>The errors of the delete; for an enlisted delete, a view that re-homes each error onto the host's payload.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required ValidationErrors<TEntity> Errors
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Errors));
        }

        /// <summary>The frame this delete runs in; a validator enlists writes into other stacks through it.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IWriteHost Host
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Host));
        }

        /// <summary>The declared pair this delete was enlisted under; null for a delete that came through the front door.</summary>
        public EnlistmentDescriptor? Enlistment { get; init; }

        /// <summary>Cancels the delete's reads and validation rounds when the caller goes away; a persist already sent completes regardless.</summary>
        public CancellationToken CancellationToken { get; init; }
    }

    /// <summary>What a delete's validation hooks see: the key-erased view and the target ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public sealed class DeleteContext<TEntity, TKey> : DeleteContext<TEntity>
    {
        /// <summary>The ids the delete targets, in request order.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TKey> Ids
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Ids));
        }
    }
}
