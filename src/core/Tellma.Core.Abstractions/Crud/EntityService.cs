// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The service of one stack: its standard operations, each forwarding to the platform's
    ///     pipeline, and the hooks a subclass overrides to add the stack's own rules.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The operations are not virtual: a subclass overrides hooks, never steps, so none can
    ///         forget a check. An operation the stack omits throws <see cref="InvalidOperationException" />,
    ///         an authoring bug. The hooks run in the pipeline's order; each takes one context object.
    ///     </para>
    ///     <para>
    ///         The constructor binds the pipeline once, so every operation of this instance runs on a
    ///         pipeline bound to this service's hooks. A subclass passes the factory it is given
    ///         through to this constructor.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    public abstract class EntityService<TEntity, TKey> : IEntityBehavior<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>Binds the service to a pipeline.</summary>
        /// <param name="factory">The pipeline factory the scope resolves.</param>
        /// <exception cref="ArgumentNullException"><paramref name="factory" /> is null.</exception>
        /// <exception cref="InvalidOperationException">The factory created no pipeline.</exception>
        protected EntityService(IEntityPipelineFactory factory)
        {
            ArgumentNullException.ThrowIfNull(factory);

            // The factory only binds the hooks here; it calls none of them before the constructor
            // of the most derived service has finished.
            Pipeline = factory.Create(this) ?? throw new InvalidOperationException(
                $"The pipeline factory created no pipeline for '{GetType().Name}'.");
        }

        /// <summary>The stack's descriptor.</summary>
        public StackDescriptor Descriptor => Pipeline.Descriptor;

        /// <summary>The pipeline bound to this service, which the extension operations forward to.</summary>
        internal IEntityPipeline<TEntity, TKey> Pipeline { get; }

        // --- Operations: each forwards to the pipeline ---

        /// <summary>Runs a query: filters, named filters, search, ordering, paging, the capped count and the ancestors, under the caller's read filter.</summary>
        /// <param name="request">The query.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page of rows.</returns>
        public Task<RowPage> QueryAsync(QueryRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Pipeline.QueryAsync(request, cancellationToken);
        }

        /// <summary>Reads one row in details shape, with its children, what it points at, and the extras requested.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row.</returns>
        /// <exception cref="Errors.NotFoundException">The row is absent or invisible to the caller; the two are indistinguishable.</exception>
        public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Pipeline.GetByIdAsync(id, request, cancellationToken);
        }

        /// <summary>Reads rows in details shape, in request order; an absent or invisible id is omitted, never an error.</summary>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The visible rows.</returns>
        public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(request);
            return Pipeline.GetByIdsAsync(ids, request, cancellationToken);
        }

        /// <summary>Reads every row of a cacheable stack from the tenant's cache.</summary>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Pipeline.GetAllCachedAsync(request, cancellationToken);
        }

        /// <summary>Saves entities, inserts and updates together, in one transaction.</summary>
        /// <param name="entities">The entities; the details page sends one.</param>
        /// <param name="options">How the save runs and what it answers.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The saved ids in input order and, when asked, the rows read back.</returns>
        public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);
            return Pipeline.SaveAsync(entities, options, cancellationToken);
        }

        /// <summary>Deletes rows by id, all or nothing.</summary>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="expectedStamps">The stamps the caller expects, parallel to <paramref name="ids" />; null compares none.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The deleted count.</returns>
        public Task<AffectedResult> DeleteByIdsAsync(
            IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps = null, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(ids);
            return Pipeline.DeleteByIdsAsync(ids, expectedStamps, cancellationToken);
        }

        /// <summary>Deletes the rows a filter selects, when their count is the one expected.</summary>
        /// <param name="request">The filter and the expected count.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The deleted count.</returns>
        public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Pipeline.DeleteByQueryAsync(request, cancellationToken);
        }

        /// <summary>Runs a built-in or declared action whose method returns no value of its own.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="ids">The target ids.</param>
        /// <param name="arguments">The action's arguments, as its arguments record or as JSON; null for none.</param>
        /// <param name="options">What the action answers.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The target ids in request order and, when asked, the rows read back.</returns>
        /// <exception cref="InvalidOperationException">No action has the name, or the action answers a result of its own.</exception>
        public Task<EntitiesResult<TEntity>> ExecuteActionAsync(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            return Pipeline.ExecuteActionAsync(action, ids, arguments, options, cancellationToken);
        }

        /// <summary>Runs a declared action whose method returns a result of its own; no read-back runs.</summary>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="action">The action's name.</param>
        /// <param name="ids">The target ids; exactly one for a single-target action.</param>
        /// <param name="arguments">The action's arguments, as its arguments record or as JSON; null for none.</param>
        /// <param name="options">Ignored: an action with its own result runs no read-back.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The method's result, once the action committed.</returns>
        /// <exception cref="InvalidOperationException">No action has the name, or the action answers no result of its own.</exception>
        public Task<TResult> ExecuteActionAsync<TResult>(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            return Pipeline.ExecuteActionAsync<TResult>(action, ids, arguments, options, cancellationToken);
        }

        /// <summary>The row source the Excel codec streams from, bound to one source; it runs nothing until read.</summary>
        /// <param name="source">The rows to read.</param>
        /// <returns>The row source.</returns>
        public IExcelRowSource ExcelRowSource(ExportSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            return Pipeline.CreateExcelRowSource(source);
        }

        // --- Hooks: empty by default; the pipeline sees them through IEntityBehavior ---

        /// <summary>
        ///     Preprocesses a save's payload, once, after the platform's normalisation and before ids are
        ///     assigned: defaults, derived values, normalisations. Does nothing by default.
        /// </summary>
        /// <param name="context">The save's context; its rows have no final ids yet.</param>
        /// <returns>A task that completes when the payload is preprocessed.</returns>
        protected virtual Task PreprocessAsync(SaveContext<TEntity> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Validates a save; runs before the validator components. Does nothing by default.</summary>
        /// <param name="context">The save's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected virtual Task ValidateAsync(SaveContext<TEntity> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Validates a delete; runs before the validator components. Does nothing by default, and a
        ///     delete whose service does not override it skips loading the target rows.
        /// </summary>
        /// <param name="context">The delete's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected virtual Task ValidateDeleteAsync(DeleteContext<TEntity, TKey> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Validates an action; runs before the validator components and beside the action's method. Does nothing by default.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="context">The action's context.</param>
        /// <returns>A task that completes when the rules ran.</returns>
        protected virtual Task ValidateActionAsync(string action, ActionContext<TEntity, TKey> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        ///     Appends the service's statements to every persist batch on the stack, with final ids;
        ///     runs before the effect components. Does nothing by default.
        /// </summary>
        /// <param name="context">The persist context, of the operation's kind.</param>
        /// <returns>A task that completes when the statements are appended.</returns>
        protected virtual Task ContributeAsync(PersistContext<TEntity> context)
        {
            return Task.CompletedTask;
        }

        /// <summary>Runs the service's best-effort work after a commit, outside any transaction; runs before the effect components. Does nothing by default.</summary>
        /// <param name="outcome">The committed operation's outcome, of its kind.</param>
        /// <returns>A task that completes when the work ran.</returns>
        protected virtual Task AfterCommitAsync(PersistOutcome<TEntity> outcome)
        {
            return Task.CompletedTask;
        }

        /// <summary>Adds the statements or computed extras producing the extras the service declares. Does nothing by default.</summary>
        /// <param name="plan">The details plan.</param>
        protected virtual void ContributeDetails(DetailsPlan<TEntity, TKey> plan)
        {
        }

        /// <summary>The service's search filter for a term, replacing the platform's match over the searchable properties; null, the default, keeps the platform's.</summary>
        /// <param name="search">The search term.</param>
        /// <returns>The filter, or null.</returns>
        protected virtual FilterTree? SearchFilter(string search)
        {
            return null;
        }

        /// <summary>
        ///     Why a row refuses an ability the caller's grant admits, for the <c>permissions</c> extra;
        ///     null, the default, refuses nothing. It answers the extra only: the operation's own
        ///     validation still enforces the rule.
        /// </summary>
        /// <param name="row">The row.</param>
        /// <param name="ability">The ability: <c>save</c>, <c>delete</c>, <c>activate</c> or <c>action:&lt;name&gt;</c>.</param>
        /// <returns>The reason's resource key, or null.</returns>
        protected virtual string? RefuseOnRecord(TEntity row, string ability)
        {
            return null;
        }

        // --- The hooks as the pipeline sees them ---

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.PreprocessAsync(SaveContext<TEntity> context)
        {
            return PreprocessAsync(context);
        }

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.ValidateAsync(SaveContext<TEntity> context)
        {
            return ValidateAsync(context);
        }

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.ValidateDeleteAsync(DeleteContext<TEntity, TKey> context)
        {
            return ValidateDeleteAsync(context);
        }

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.ValidateActionAsync(string action, ActionContext<TEntity, TKey> context)
        {
            return ValidateActionAsync(action, context);
        }

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.ContributeAsync(PersistContext<TEntity> context)
        {
            return ContributeAsync(context);
        }

        /// <inheritdoc />
        Task IEntityBehavior<TEntity, TKey>.AfterCommitAsync(PersistOutcome<TEntity> outcome)
        {
            return AfterCommitAsync(outcome);
        }

        /// <inheritdoc />
        void IEntityBehavior<TEntity, TKey>.ContributeDetails(DetailsPlan<TEntity, TKey> plan)
        {
            ContributeDetails(plan);
        }

        /// <inheritdoc />
        FilterTree? IEntityBehavior<TEntity, TKey>.SearchFilter(string search)
        {
            return SearchFilter(search);
        }

        /// <inheritdoc />
        string? IEntityBehavior<TEntity, TKey>.RefuseOnRecord(TEntity row, string ability)
        {
            return RefuseOnRecord(row, ability);
        }
    }

    /// <summary>The service of a stack keyed by <see cref="int" />, the common case.</summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <param name="factory">The pipeline factory the scope resolves.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory" /> is null.</exception>
    public abstract class EntityService<TEntity>(IEntityPipelineFactory factory) : EntityService<TEntity, int>(factory)
        where TEntity : Entity<int>;
}
