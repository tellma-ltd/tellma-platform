// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Assignments;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Validation
{
    /// <summary>
    ///     What an action's validation hook and its method see, without the key type: the target rows,
    ///     the arguments, and the means to validate and to compose the action's writes. A component
    ///     generic over the entity alone sees this view; the service sees
    ///     <see cref="ActionContext{TEntity, TKey}" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The hook and the method run as a validator and a mutator over one context: errors
    ///         collect in <see cref="Errors" />; <see cref="Save" /> and <see cref="Update" /> compose
    ///         the writes to the stack's own rows; <see cref="Batch" /> takes statements with declared
    ///         writes; <see cref="Notify" />, <see cref="Withdraw" /> and <see cref="Assignments" />
    ///         append to the same batch; <see cref="Host" /> takes enlisted writes into other stacks.
    ///         Any error fails the action and nothing composed is written.
    ///     </para>
    ///     <para>An action is never enlisted, so the context carries no enlistment.</para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    public abstract class ActionContext<TEntity>
    {
        /// <summary>Guards the composed writes: the hook and the method may run concurrently.</summary>
        private readonly Lock _gate = new();

        /// <summary>The rows marked for the emitter, in marking order.</summary>
        private readonly List<TEntity> _saved = [];

        /// <summary>The update assignments composed, in order.</summary>
        private readonly List<IReadOnlyDictionary<string, object?>> _updates = [];

        /// <summary>Whether composing has closed: the persist batch is being assembled.</summary>
        private bool _closed;

        /// <summary>Initialises the view; private protected so the keyed context is its one kind.</summary>
        private protected ActionContext()
        {
        }

        /// <summary>
        ///     The visible target rows, loaded under the caller's read filter, with every child collection
        ///     when the action loads children. They load with the first validation round, so the hook
        ///     and the method read them after their first <see cref="IContextLoader.LoadAsync" />.
        /// </summary>
        /// <exception cref="InvalidOperationException">Read before the first round ran.</exception>
        public IReadOnlyList<TEntity> Entities
        {
            get => field ?? throw new InvalidOperationException(
                "An action's target rows load with the first validation round. Await the context loader's LoadAsync before reading them.");
            internal set => field = value ?? throw new ArgumentNullException(nameof(Entities));
        }

        /// <summary>The action's arguments, as its arguments record; null for an action that takes none.</summary>
        public object? Arguments { get; init; }

        /// <summary>Who runs the action, in which tenant and culture.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required RequestContext Context
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Context));
        }

        /// <summary>The loader the hook and the method read context through.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IContextLoader Loader
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Loader));
        }

        /// <summary>The errors of the action.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required ValidationErrors<TEntity> Errors
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Errors));
        }

        /// <summary>
        ///     The action's persist batch: statements appended here carry declared writes and commit with
        ///     the action. A statement writing a table another stack owns is refused when appended; such
        ///     rows are enlisted through <see cref="Host" />.
        /// </summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IDataBatch Batch
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Batch));
        }

        /// <summary>The pipeline frame the action runs in; the method enlists writes into other stacks through it.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IWriteHost Host
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Host));
        }

        /// <summary>Cancels the action's reads and validation rounds when the caller goes away; a persist already sent completes regardless.</summary>
        public CancellationToken CancellationToken { get; init; }

        /// <summary>
        ///     Completes once the action's persist committed, and faults with the exception that ended the
        ///     action: the persist's, or the validation failure that kept it from running. A method that
        ///     must read a batch result it appended awaits this before returning.
        /// </summary>
        public Task Persisted => PersistedSource.Task;

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

        /// <summary>The source the pipeline completes or faults <see cref="Persisted" /> through: once the persist committed, or with what ended the action.</summary>
        internal TaskCompletionSource PersistedSource { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The rows <see cref="Save" /> marked, in marking order.</summary>
        internal IReadOnlyList<TEntity> SavedRows
        {
            get
            {
                lock (_gate)
                {
                    return [.. _saved];
                }
            }
        }

        /// <summary>The assignments <see cref="Update" /> composed, in order.</summary>
        internal IReadOnlyList<IReadOnlyDictionary<string, object?>> Updates
        {
            get
            {
                lock (_gate)
                {
                    return [.. _updates];
                }
            }
        }

        /// <summary>
        ///     Marks rows for the stack's emitter, which writes them with stamping and without a stamp
        ///     comparison: an action is a command over rows it just loaded. A row deleted meanwhile still
        ///     fails as missing.
        /// </summary>
        /// <param name="entities">The rows; none null.</param>
        /// <exception cref="ArgumentNullException"><paramref name="entities" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="entities" /> holds a null row.</exception>
        /// <exception cref="InvalidOperationException">Composing has closed.</exception>
        public void Save(IReadOnlyList<TEntity> entities)
        {
            ArgumentNullException.ThrowIfNull(entities);
            TEntity[] rows = [.. entities];
            if (Array.Exists(rows, static row => row is null))
            {
                throw new ArgumentException("A row marked for saving is never null.", nameof(entities));
            }

            lock (_gate)
            {
                ThrowIfClosed();
                _saved.AddRange(rows);
            }
        }

        /// <summary>
        ///     Composes one update of the target rows, under the caller's read filter and the action
        ///     grant's filter, all or nothing, with stamping.
        /// </summary>
        /// <param name="assignments">The new values, by property name; at least one.</param>
        /// <exception cref="ArgumentNullException"><paramref name="assignments" /> is null.</exception>
        /// <exception cref="ArgumentException"><paramref name="assignments" /> is empty.</exception>
        /// <exception cref="InvalidOperationException">Composing has closed.</exception>
        public void Update(IReadOnlyDictionary<string, object?> assignments)
        {
            ArgumentNullException.ThrowIfNull(assignments);
            if (assignments.Count == 0)
            {
                throw new ArgumentException("An update assigns at least one property.", nameof(assignments));
            }

            IReadOnlyDictionary<string, object?> copy = ReadOnlyCopies.Of(assignments);
            lock (_gate)
            {
                ThrowIfClosed();
                _updates.Add(copy);
            }
        }

        /// <summary>Appends a notification to <see cref="Batch" />; it is raised when the action commits.</summary>
        /// <param name="request">The notification.</param>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The request breaks a rule of the notifier.</exception>
        /// <exception cref="NotSupportedException">The host registers no notifier.</exception>
        public void Notify(NotificationRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContextNotifier.Require(Notifier).Notify(Batch, [request]);
        }

        /// <summary>Appends a withdrawal of notifications to <see cref="Batch" />; it applies when the action commits.</summary>
        /// <param name="request">The withdrawal.</param>
        /// <exception cref="ArgumentNullException"><paramref name="request" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The request breaks a rule of the notifier.</exception>
        /// <exception cref="NotSupportedException">The host registers no notifier.</exception>
        public void Withdraw(WithdrawRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContextNotifier.Require(Notifier).Withdraw(Batch, [request]);
        }

        /// <summary>Closes composing: the pipeline is assembling the persist batch, so a later <see cref="Save" /> or <see cref="Update" /> is refused.</summary>
        internal void Close()
        {
            lock (_gate)
            {
                _closed = true;
            }
        }

        /// <summary>Refuses a write composed after the persist batch was assembled.</summary>
        /// <exception cref="InvalidOperationException">Composing has closed.</exception>
        private void ThrowIfClosed()
        {
            if (_closed)
            {
                throw new InvalidOperationException(
                    "The action's persist batch is already assembled. Compose every save and update before the action persists.");
            }
        }
    }

    /// <summary>What an action's validation hook and its method see: the key-erased view and the target ids.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public sealed class ActionContext<TEntity, TKey> : ActionContext<TEntity>
    {
        /// <summary>The ids the action targets, in request order.</summary>
        /// <exception cref="ArgumentNullException">Set to null.</exception>
        public required IReadOnlyList<TKey> Ids
        {
            get;
            init => field = value ?? throw new ArgumentNullException(nameof(Ids));
        }
    }
}
