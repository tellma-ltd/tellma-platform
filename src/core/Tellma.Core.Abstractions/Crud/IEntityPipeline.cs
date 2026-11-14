// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.ComponentModel;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Excel;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The platform's sealed pipeline behind one entity service: every operation's steps, bound to
    ///     the service's hooks. Public only because the service base lives in the contracts while the
    ///     pipeline lives in the runtime; not for distributions, which call the service's operations.
    /// </summary>
    /// <remarks>
    ///     A startup check refuses any registration of this interface whose implementation is not the
    ///     platform's.
    /// </remarks>
    /// <typeparam name="TEntity">The stack's entity type.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public interface IEntityPipeline<TEntity, TKey>
    {
        /// <summary>The stack's descriptor.</summary>
        StackDescriptor Descriptor { get; }

        /// <summary>Runs a query.</summary>
        /// <param name="request">The query.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page of rows.</returns>
        Task<RowPage> QueryAsync(QueryRequest request, CancellationToken cancellationToken);

        /// <summary>Reads one row in details shape.</summary>
        /// <param name="id">The row's id.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row.</returns>
        Task<EntitiesResult<TEntity>> GetByIdAsync(TKey id, DetailsRequest request, CancellationToken cancellationToken);

        /// <summary>Reads rows in details shape, in request order.</summary>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The visible rows.</returns>
        Task<EntitiesResult<TEntity>> GetByIdsAsync(IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken);

        /// <summary>Reads every row of a cacheable stack.</summary>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        Task<EntitiesResult<TEntity>> GetAllCachedAsync(DetailsRequest request, CancellationToken cancellationToken);

        /// <summary>Saves entities.</summary>
        /// <param name="entities">The entities.</param>
        /// <param name="options">How the save runs.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The saved ids and, when asked, the rows.</returns>
        Task<EntitiesResult<TEntity>> SaveAsync(IReadOnlyList<TEntity> entities, SaveOptions options, CancellationToken cancellationToken);

        /// <summary>Deletes rows by id, all or nothing.</summary>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="expectedStamps">The stamps the caller expects, parallel to the ids; null for none.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The deleted count.</returns>
        Task<AffectedResult> DeleteByIdsAsync(IReadOnlyList<TKey> ids, IReadOnlyList<DateTimeOffset?>? expectedStamps, CancellationToken cancellationToken);

        /// <summary>Deletes the rows a filter selects, when their count is the one expected.</summary>
        /// <param name="request">The filter and the expected count.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The deleted count.</returns>
        Task<AffectedResult> DeleteByQueryAsync(DeleteByQueryRequest request, CancellationToken cancellationToken);

        /// <summary>Runs an action without a result of its own.</summary>
        /// <param name="action">The action's name.</param>
        /// <param name="ids">The target ids.</param>
        /// <param name="arguments">The action's arguments; null for none.</param>
        /// <param name="options">What the action answers.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The target ids and, when asked, the rows.</returns>
        Task<EntitiesResult<TEntity>> ExecuteActionAsync(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken);

        /// <summary>Runs an action with a result of its own.</summary>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="action">The action's name.</param>
        /// <param name="ids">The target ids.</param>
        /// <param name="arguments">The action's arguments; null for none.</param>
        /// <param name="options">Ignored: an action with its own result runs no read-back.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The method's result.</returns>
        Task<TResult> ExecuteActionAsync<TResult>(
            string action, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken);

        /// <summary>Reads the children of tree rows, or the roots.</summary>
        /// <param name="request">The parents and the clauses.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The rows.</returns>
        Task<QueryRowSet> GetByParentIdsAsync(ParentIdsRequest request, CancellationToken cancellationToken);

        /// <summary>Deletes tree rows with their descendants.</summary>
        /// <param name="ids">The rows' ids.</param>
        /// <param name="cancellationToken">Cancels the reads and validation rounds.</param>
        /// <returns>The deleted count, descendants included.</returns>
        Task<AffectedResult> DeleteWithDescendantsAsync(IReadOnlyList<TKey> ids, CancellationToken cancellationToken);

        /// <summary>Creates the stack's row source over one source; it runs nothing until read.</summary>
        /// <param name="source">The rows to read.</param>
        /// <returns>The row source.</returns>
        IExcelRowSource CreateExcelRowSource(ExportSource source);
    }
}
