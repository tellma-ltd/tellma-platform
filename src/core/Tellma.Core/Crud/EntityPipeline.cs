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
using Tellma.Core.Crud.Query;

namespace Tellma.Core.Crud
{
    // The pipeline is spec 0015 §3.1; an operation the stack omits is §2.2's authoring bug.
    /// <summary>
    ///     The platform's sealed pipeline behind one entity service instance: each operation's
    ///     steps, run against the service's hooks under the stack's descriptor. An operation the
    ///     stack omits throws <see cref="InvalidOperationException" />, an authoring bug never a user
    ///     outcome.
    /// </summary>
    /// <remarks>
    ///     The query runs here. The details, the save, the deletes, the actions, the tree reads and
    ///     the Excel row source refuse with <see cref="NotSupportedException" /> until their steps
    ///     are built.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="descriptor">The stack's descriptor.</param>
    /// <param name="behavior">The service's hooks.</param>
    /// <param name="surfaces">The query surfaces.</param>
    /// <param name="queries">The scope's query operation.</param>
    internal sealed class EntityPipeline<TEntity, TKey>(StackDescriptor descriptor, IEntityBehavior<TEntity, TKey> behavior, QuerySurfaces surfaces, QueryOperation queries)
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
            QuerySurface surface = surfaces.Stack(Descriptor.Resource)
                ?? throw new InvalidOperationException($"The stack {Descriptor.Resource} has no query surface.");
            return queries.RunAsync(surface, request, behavior.SearchFilter, cancellationToken);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken)
        {
            Require(StackOperations.Details, CrudOperationNames.Get);
            throw NotBuilt(CrudOperationNames.Get);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken)
        {
            Require(StackOperations.Details, CrudOperationNames.GetByIds);
            throw NotBuilt(CrudOperationNames.GetByIds);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken)
        {
            Require(StackOperations.Details, CrudOperationNames.All, CacheableCapability);
            throw NotBuilt(CrudOperationNames.All);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken)
        {
            Require(StackOperations.Save, CrudOperationNames.Save);
            throw NotBuilt(CrudOperationNames.Save);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken)
        {
            Require(StackOperations.Delete, CrudOperationNames.Delete);
            throw NotBuilt(CrudOperationNames.Delete);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken)
        {
            Require(StackOperations.Delete, CrudOperationNames.DeleteByQuery);
            throw NotBuilt(CrudOperationNames.DeleteByQuery);
        }

        /// <inheritdoc />
        public Task<EntitiesResult<TEntity>> ExecuteActionAsync(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            throw NotBuilt(CrudOperationNames.Action);
        }

        /// <inheritdoc />
        public Task<TResult> ExecuteActionAsync<TResult>(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
        {
            throw NotBuilt(CrudOperationNames.Action);
        }

        /// <inheritdoc />
        public Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken)
        {
            Require(StackOperations.Query, CrudOperationNames.GetByParentIds, TreeCapability);
            throw NotBuilt(CrudOperationNames.GetByParentIds);
        }

        /// <inheritdoc />
        public Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
        {
            Require(StackOperations.Delete, CrudOperationNames.DeleteWithDescendants, TreeCapability);
            throw NotBuilt(CrudOperationNames.DeleteWithDescendants);
        }

        /// <inheritdoc />
        public IExcelRowSource CreateExcelRowSource(ExportSource source)
        {
            Require(StackOperations.Export, CrudOperationNames.ExcelRows);
            throw NotBuilt(CrudOperationNames.ExcelRows);
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

        /// <summary>The refusal of an operation whose steps are not built yet.</summary>
        /// <param name="name">The operation's name.</param>
        /// <returns>The exception.</returns>
        private NotSupportedException NotBuilt(string name)
        {
            return new NotSupportedException($"The pipeline of {Descriptor.Resource} does not run '{name}' yet.");
        }
    }
}
