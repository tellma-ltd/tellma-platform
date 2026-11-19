// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.AspNetCore.Hosting;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §4.2: one static method per standard operation, closed over each stack's entity and
    // key types, so the request-delegate factory sees real parameter types and OpenAPI real schemas.
    // Each reads its body under the HTTP JSON options, narrows the wire's ids to the key type and
    // calls the stack's service, which evaluates the securable; none evaluates one itself.

    /// <summary>The web endpoints of a stack's standard operations.</summary>
    /// <typeparam name="TEntity">The stack's entity leaf.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    internal static class StandardEndpoints<TEntity, TKey>
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The path a single id binds as.</summary>
        private const string IdPath = "id";

        /// <summary>The path a list of ids binds as.</summary>
        private const string IdsPath = "ids";

        /// <summary>The stack's <c>query</c>: one page of rows.</summary>
        /// <param name="request">The query.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The page.</returns>
        public static Task<RowPage> QueryAsync(
            [FromBody] QueryRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            return service.QueryAsync(request, cancellationToken);
        }

        /// <summary>The stack's <c>get</c>: one entity by id, 404 when absent or invisible.</summary>
        /// <param name="request">The id and the extras.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The entity's envelope.</returns>
        public static Task<EntitiesResult<TEntity>> GetAsync(
            [FromBody] GetRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            TKey id = WireIds.ToKey<TKey>(request.Id, IdPath);
            return service.GetByIdAsync(id, WireBinding.Details(request.Include), cancellationToken);
        }

        /// <summary>The stack's <c>get-by-ids</c>: the entities found, in the requested order.</summary>
        /// <param name="request">The ids and the extras.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The entities' envelope.</returns>
        public static Task<EntitiesResult<TEntity>> GetByIdsAsync(
            [FromBody] GetByIdsRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            IReadOnlyList<TKey> ids = WireIds.ToKeys<TKey>(request.Ids, IdsPath);
            return service.GetByIdsAsync(ids, WireBinding.Details(request.Include), cancellationToken);
        }

        /// <summary>The stack's <c>all</c>: every entity of a cacheable stack, from the cache.</summary>
        /// <param name="request">The extras.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The entities' envelope.</returns>
        public static Task<EntitiesResult<TEntity>> AllAsync(
            [FromBody] AllRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            return service.GetAllCachedAsync(WireBinding.Details(request.Include), cancellationToken);
        }

        /// <summary>
        ///     The stack's <c>save</c>: the entities written as a web save. More entities than the web
        ///     surface's ceiling or the stack's, whichever is smaller, are refused before the service
        ///     is called.
        /// </summary>
        /// <param name="request">The entities and the save's options.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="api">The web API's options.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The saved entities' envelope.</returns>
        /// <exception cref="LimitExceededException">The request carries more entities than one web save takes.</exception>
        public static Task<EntitiesResult<TEntity>> SaveAsync(
            [FromBody] SaveRequest<TEntity> request,
            [FromServices] EntityService<TEntity, TKey> service,
            [FromServices] IOptions<ApiOptions> api,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            ArgumentNullException.ThrowIfNull(api);

            // --- The web surface lowers the stack's ceiling; the smaller wins, before anything runs ---
            int maximum = Math.Min(api.Value.MaxEntitiesPerSave, service.Descriptor.Limits.MaxSaveCount);
            if (request.Entities.Count > maximum)
            {
                throw new LimitExceededException(LimitNames.Entities, request.Entities.Count, maximum);
            }

            // --- The save, as the web surface's: what it answers comes from the request ---
            SaveOptions options = new()
            {
                ReturnEntities = request.ReturnEntities,
                Details = WireBinding.Details(request.Include),
                Concurrency = request.Concurrency,
                Source = SaveSource.Web,
            };
            return service.SaveAsync(request.Entities, options, cancellationToken);
        }

        /// <summary>The stack's <c>delete</c>: the entities with the ids, deleted.</summary>
        /// <param name="request">The ids.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>How many rows were deleted.</returns>
        public static Task<AffectedResult> DeleteAsync(
            [FromBody] IdsRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);

            // The web surface carries no stamps on a delete: it is a command over ids.
            IReadOnlyList<TKey> ids = WireIds.ToKeys<TKey>(request.Ids, IdsPath);
            return service.DeleteByIdsAsync(ids, expectedStamps: null, cancellationToken);
        }

        /// <summary>The stack's <c>delete-by-query</c>: the rows a filter matches, deleted when they are as many as expected.</summary>
        /// <param name="request">The filter and the expected count.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>How many rows were deleted.</returns>
        public static Task<AffectedResult> DeleteByQueryAsync(
            [FromBody] DeleteByQueryRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            return service.DeleteByQueryAsync(request, cancellationToken);
        }
    }
}
