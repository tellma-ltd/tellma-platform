// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Excel;
using Tellma.Core.Crud.Actions;
using Tellma.Core.Crud.Delete;
using Tellma.Core.Crud.Details;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.ExcelRows;
using Tellma.Core.Crud.Query;
using Tellma.Core.Crud.Save;

namespace Tellma.Core.Crud
{
    // The pipeline is spec 0015 §3.1; an operation the stack omits is §2.2's authoring bug. A write
    // called from inside a frame that is composing is refused (§13.3): it would run in a second
    // transaction beside the frame's, so it enlists instead.
    /// <summary>
    ///     The platform's sealed pipeline behind one entity service instance: each operation's
    ///     steps, run against the service's hooks under the stack's descriptor. An operation the
    ///     stack omits throws <see cref="InvalidOperationException" />, an authoring bug never a user
    ///     outcome.
    /// </summary>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="descriptor">The stack's descriptor.</param>
    /// <param name="behavior">The service's hooks.</param>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="queries">The scope's query operation.</param>
    /// <param name="excelRows">The scope's Excel row-source operation.</param>
    /// <param name="details">The scope's details reads.</param>
    /// <param name="save">The scope's save operation.</param>
    /// <param name="deletes">The scope's deletes.</param>
    /// <param name="actions">The scope's actions.</param>
    internal sealed class EntityPipeline<TEntity, TKey>(
        StackDescriptor descriptor,
        IEntityBehavior<TEntity, TKey> behavior,
        QuerySurfaces surfaces,
        QueryOperation queries,
        ExcelRowsOperation excelRows,
        DetailsOperation details,
        SaveOperation save,
        DeleteOperation deletes,
        ActionOperation actions)
        : IEntityPipeline<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The capability of a stack whose rows form a tree.</summary>
        private const string TreeCapability = "tree";

        /// <summary>The capability of a stack whose rows are cached whole.</summary>
        private const string CacheableCapability = "cacheable";

        /// <inheritdoc />
        public StackDescriptor Descriptor { get; } = descriptor;

        /// <inheritdoc />
        public Task<RowPage> QueryAsync(QueryRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.Query, CrudOperationNames.Query);
            return queries.RunAsync(Surface(), request, behavior.SearchFilter, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.Details, CrudOperationNames.Get);
            return details.GetByIdAsync(Descriptor, behavior, id, request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.Details, CrudOperationNames.GetByIds);
            return details.GetByIdsAsync(Descriptor, behavior, ids, request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.Details, CrudOperationNames.All, CacheableCapability);
            return details.GetAllCachedAsync(Descriptor, behavior, request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(entities);
            ArgumentNullException.ThrowIfNull(options);
            Require(StackOperations.Save, CrudOperationNames.Save);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.Save);
            return save.SaveAsync(Descriptor, behavior, entities, options, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);
            Require(StackOperations.Delete, CrudOperationNames.Delete);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.Delete);
            return deletes.DeleteByIdsAsync(Descriptor, behavior, ids, expectedStamps, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.DeleteByQuery, CrudOperationNames.DeleteByQuery);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.DeleteByQuery);
            return deletes.DeleteByQueryAsync(Descriptor, behavior, request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> ExecuteActionAsync(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.Action);
            return actions.ExecuteAsync(Descriptor, behavior, action, ids, arguments, options, cancellationToken);
        }

        /// <inheritdoc />
        public Task<TResult> ExecuteActionAsync<TResult>(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(action);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.Action);
            return actions.ExecuteAsync<TEntity, TKey, TResult>(Descriptor, behavior, action, ids, arguments, options, cancellationToken);
        }

        /// <inheritdoc />
        public Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            Require(StackOperations.Query, CrudOperationNames.GetByParentIds, TreeCapability);
            return queries.RunParentIdsAsync<TKey>(Surface(), Descriptor.EntityType, request, cancellationToken);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(ids);
            Require(StackOperations.Delete, CrudOperationNames.DeleteWithDescendants, TreeCapability);
            FrameFlow.EnsureFrontDoor(Descriptor.Resource, CrudOperationNames.DeleteWithDescendants);
            return deletes.DeleteWithDescendantsAsync(Descriptor, behavior, ids, cancellationToken);
        }

        /// <inheritdoc />
        public IExcelRowSource CreateExcelRowSource(ExportSource source)
        {
            ArgumentNullException.ThrowIfNull(source);
            Require(StackOperations.Export, CrudOperationNames.ExcelRows);
            return excelRows.Create(Surface(), source, behavior.SearchFilter);
        }

        /// <summary>The stack's query surface.</summary>
        /// <returns>The surface.</returns>
        /// <exception cref="InvalidOperationException">The stack has none.</exception>
        private QuerySurface Surface()
        {
            return surfaces.Stack(Descriptor.Resource)
                ?? throw new InvalidOperationException($"The stack {Descriptor.Resource} has no query surface.");
        }

        /// <summary>Refuses an operation the stack omits: an authoring bug.</summary>
        /// <param name="operation">The stack operation it needs.</param>
        /// <param name="name">The operation's name.</param>
        /// <param name="capability">The capability it also needs; null when none.</param>
        /// <exception cref="InvalidOperationException">The stack omits the operation or lacks the capability.</exception>
        private void Require(StackOperations operation, string name, string? capability = null)
        {
            if (!Descriptor.Operations.HasFlag(operation))
            {
                throw new InvalidOperationException($"The stack {Descriptor.Resource} omits the {operation} operation, so its service cannot run '{name}'.");
            }

            if (capability is not null && !Descriptor.Capabilities.Contains(capability))
            {
                throw new InvalidOperationException($"The stack {Descriptor.Resource} lacks the {capability} capability, so its service cannot run '{name}'.");
            }
        }
    }
}
