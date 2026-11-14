// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     What a transactional participant sees: the persist batch of one operation on the stack, after
    ///     the operation's own statements and before the batch is sent, with final ids. Everything a
    ///     participant appends commits or rolls back with the operation.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The kind matches the operation: a <see cref="SavePersistContext{TEntity}" /> for a save, a
    ///         <see cref="DeletePersistContext{TEntity}" /> for every delete, and an
    ///         <see cref="ActionPersistContext{TEntity}" /> for every action, the built-in ones included.
    ///         A participant that needs a save-only member type-tests the context.
    ///     </para>
    ///     <para>
    ///         A participant only appends: it performs no I/O of its own and the context exposes no
    ///         connection. Anything that must eventually happen outside the database is a job row
    ///         appended here.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class PersistContext<TEntity>
    {
        /// <summary>Initialises the context; private protected so the three kinds stay the only ones.</summary>
        private protected PersistContext()
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

        /// <summary>Who runs the operation, in which tenant and culture.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required RequestContext Context
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Context));
        }

        /// <summary>The persist batch, append-only for participants; a raw statement declares its writes.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IDataBatch Batch
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Batch));
        }

        /// <summary>
        ///     The ids the operation affects, as a table a statement joins: for a save, the saved roots;
        ///     for an action, the target ids; for a delete, the deleted ids. Opaque: interpolate it,
        ///     never spell its name.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SqlIdentifier AffectedIds
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(AffectedIds));
        }

        /// <summary>
        ///     The emitter's column captures of this persist, looked up by table and column, such as the
        ///     blob references a save replaced or a delete removed; empty on an action.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<ColumnCapture> Captures
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Captures));
        }

        /// <summary>The declared pair of the enlisted group this context belongs to; null for the host's own operation.</summary>
        public EnlistmentDescriptor? Enlistment { get; init; }

        /// <summary>
        ///     The assignments of rows of the stack, appended to <see cref="Batch" />.
        /// </summary>
        /// <exception cref="NotSupportedException">The host registers no assignments service.</exception>
        public BatchAssignments Assignments => BatchAssignments.For(AssignmentsService, Batch, Resource);

        /// <summary>The stack's entity name, which the assignments bind; set by the pipeline.</summary>
        internal string? Resource { get; init; }

        /// <summary>The scope's notifier, which <see cref="Notify" /> and <see cref="Withdraw" /> append through; set by the pipeline.</summary>
        internal INotifier? Notifier { get; init; }

        /// <summary>The scope's assignments service; null while the host registers none.</summary>
        internal IAssignments? AssignmentsService { get; init; }

        /// <summary>Appends a notification to <see cref="Batch" />; it is raised when the operation commits.</summary>
        /// <param name="request">The notification.</param>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The request breaks a rule of the notifier.</exception>
        /// <exception cref="NotSupportedException">The host registers no notifier.</exception>
        public void Notify(NotificationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContextNotifier.Require(Notifier).Notify(Batch, [request]);
        }

        /// <summary>Appends a withdrawal of notifications to <see cref="Batch" />; it applies when the operation commits.</summary>
        /// <param name="request">The withdrawal.</param>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The request breaks a rule of the notifier.</exception>
        /// <exception cref="NotSupportedException">The host registers no notifier.</exception>
        public void Withdraw(WithdrawRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContextNotifier.Require(Notifier).Withdraw(Batch, [request]);
        }
    }

    /// <summary>The persist context of a save: the payload with final ids, its before images and options, and the tables of new and touched ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class SavePersistContext<TEntity> : PersistContext<TEntity>
    {
        /// <summary>The save's options; an enlisted save's carry the enlistment's concurrency and source and the rest defaulted.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SaveOptions Options
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Options));
        }

        /// <summary>The ids of the inserted roots, as a table a statement joins.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SqlIdentifier NewIds
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(NewIds));
        }

        /// <summary>The ids of the parents whose child collections the save synchronised, as a table a statement joins.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required SqlIdentifier TouchedIds
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(TouchedIds));
        }

        /// <summary>The before images, parallel to <see cref="PersistContext{TEntity}.Entities" /> and null for an insert; set by the pipeline.</summary>
        internal IReadOnlyList<TEntity?>? BeforeImages { get; init; }

        /// <summary>The before image of a payload row; null for an insert.</summary>
        /// <param name="index">The payload index, zero-based.</param>
        /// <returns>The before image, or null for an insert.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index" /> is outside the payload.</exception>
        /// <exception cref="InvalidOperationException">The context carries no before images: it was not prepared by the save pipeline.</exception>
        public TEntity? Before(int index)
        {
            ContextRows.CheckIndex(index, Entities.Count);
            return ContextRows.Loaded(BeforeImages)[index];
        }
    }

    /// <summary>The persist context of an action, the built-in activate and deactivate included.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class ActionPersistContext<TEntity> : PersistContext<TEntity>
    {
        /// <summary>The action's name, as its descriptor names it.</summary>
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

    /// <summary>The persist context of a delete by ids, by query or with descendants; its rows are empty and the deleted ids are <see cref="PersistContext{TEntity}.AffectedIds" />.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public sealed class DeletePersistContext<TEntity> : PersistContext<TEntity>;
}
