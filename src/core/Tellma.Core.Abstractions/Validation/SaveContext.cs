// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     What a save's preprocessing and validation hooks see: the payload after preprocessing with
    ///     its ids assigned, the before image of each update row, and the means to load context and
    ///     record errors.
    /// </summary>
    /// <remarks>
    ///     A hook takes this one object, so a later platform version adds members here and never
    ///     parameters to a hook. The pipeline creates it and sets which rows are inserts and, once the
    ///     first round ran, their before images.
    /// </remarks>
    /// <typeparam name="TEntity">The payload's entity type.</typeparam>
    public sealed class SaveContext<TEntity>
    {
        /// <summary>The payload, in payload order: preprocessed, with ids assigned.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TEntity> Entities
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Entities));
        }

        /// <summary>The save's options, as the caller passed them; an enlisted save's carry its concurrency and source.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SaveOptions Options
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Options));
        }

        /// <summary>Who is saving, in which tenant and culture.</summary>
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

        /// <summary>The errors of the save; for an enlisted save, a view that re-homes each error onto the host's payload.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required ValidationErrors<TEntity> Errors
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Errors));
        }

        /// <summary>The frame this save runs in; a validator enlists writes into other stacks through it.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IWriteHost Host
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Host));
        }

        /// <summary>The declared pair this payload was enlisted under; null for a payload that came through the front door.</summary>
        public EnlistmentDescriptor? Enlistment { get; init; }

        /// <summary>The validation round now running, 1-based; bounded by the stack's validation-round ceiling.</summary>
        public int Round { get; internal set; } = 1;

        /// <summary>Cancels the save's reads and validation rounds when the caller goes away; a persist already sent completes regardless.</summary>
        public CancellationToken CancellationToken { get; init; }

        /// <summary>Which payload rows are inserts, parallel to <see cref="Entities" />; set by the pipeline before the hooks run.</summary>
        internal IReadOnlyList<bool>? NewRows { get; init; }

        /// <summary>The before images, parallel to <see cref="Entities" /> and null for an insert; set by the pipeline once the first round ran.</summary>
        internal IReadOnlyList<TEntity?>? BeforeImages { get; set; }

        /// <summary>
        ///     The before image of an update row, loaded under the caller's read filter, with every child
        ///     collection the payload carries populated (grandchildren nested) and a collection the
        ///     payload omits null. Null for an insert.
        /// </summary>
        /// <remarks>The images load with the first validation round, so a validator reads one after its first <see cref="IContextLoader.LoadAsync" />.</remarks>
        /// <param name="index">The payload index, zero-based.</param>
        /// <returns>The before image, or null for an insert.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the payload.</exception>
        /// <exception cref="InvalidOperationException">The row is an update and the first round has not run.</exception>
        public TEntity? Before(int index)
        {
            return ContextRows.Before(BeforeImages, IsNew(index), index);
        }

        /// <summary>Whether a payload row is an insert.</summary>
        /// <param name="index">The payload index, zero-based.</param>
        /// <returns>True for an insert.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the payload.</exception>
        /// <exception cref="InvalidOperationException">The context was not prepared by the save pipeline.</exception>
        public bool IsNew(int index)
        {
            ContextRows.CheckIndex(index, Entities.Count);
            return ContextRows.IsNew(NewRows, index);
        }

        /// <summary>Whether an update row changes a value: the value read off the row differs from the value read off its before image. False for an insert.</summary>
        /// <typeparam name="TValue">The value's type, compared by its default equality.</typeparam>
        /// <param name="index">The payload index, zero-based.</param>
        /// <param name="property">Reads the value off a row: <c>x =&gt; x.ParentId</c>.</param>
        /// <returns>True when an update row's value changed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="property" /> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the payload.</exception>
        /// <exception cref="InvalidOperationException">The row is an update and the first round has not run.</exception>
        public bool Changed<TValue>(int index, Func<TEntity, TValue> property)
        {
            ArgumentNullException.ThrowIfNull(property);
            if (IsNew(index))
            {
                return false;
            }

            // An update row always has a before image once the round ran: a missing one fails the
            // save as not found before any validator resumes.
            TEntity? before = Before(index);
            return before is null || !EqualityComparer<TValue>.Default.Equals(property(Entities[index]), property(before));
        }
    }
}
