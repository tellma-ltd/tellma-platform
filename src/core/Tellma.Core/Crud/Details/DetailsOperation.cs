// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Options;
using System.Globalization;
using System.Numerics;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Caching;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Data;
using Tellma.Core.Data.Queries;

namespace Tellma.Core.Crud.Details
{
    // The operations are spec 0015 §5.3 (get, get by ids) and §5.5 (all cached); the translation of
    // an exhausted retry is §14.2, and an arithmetic failure in a details read passes through as a
    // fault, since no caller's Queryex text is involved.
    /// <summary>
    ///     The details reads of a stack: one row by id, many by ids, and every row of a cacheable stack
    ///     from the cache. Each runs the type-level read check and the ceilings, then the details
    ///     plan in one guarded round trip, or none when the cache answers. Scoped.
    /// </summary>
    /// <param name="evaluator">The caller's access decisions.</param>
    /// <param name="runner">The guarded batch runner.</param>
    /// <param name="planner">The details planner.</param>
    /// <param name="cacheable">The cacheable entities' lists.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="metadata">The tenant model's entity metadata.</param>
    /// <param name="schemas">The tenant schemas and their maps.</param>
    /// <param name="accessor">The scope's request context, whose cultures the steps after a round trip run in.</param>
    /// <param name="data">The data layer's options, whose large-batch threshold picks a read's lane.</param>
    internal sealed class DetailsOperation(
        AccessEvaluator evaluator,
        IGuardedBatchRunner runner,
        DetailsPlanner planner,
        ICacheableEntities cacheable,
        CrudOperations operations,
        IEntityMetadataProvider metadata,
        QueryexSchemaProvider schemas,
        IRequestContextAccessor accessor,
        IOptions<DataOptions> data)
    {
        /// <summary>The dependency a data-access failure names.</summary>
        private const string DatabaseDependency = "database";

        /// <summary>The wait a caller is told before retrying a read the database could not serve.</summary>
        private static readonly TimeSpan DatabaseRetryAfter = TimeSpan.FromSeconds(1);

        /// <summary>Reads one row by id: missing and invisible alike are not found.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="id">The id.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row, what it points at, and the extras.</returns>
        public Task<EntitiesResult<TEntity>> GetByIdAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, TKey id, DetailsRequest request, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(request);
            return operations.RunAsync(
                stack.Resource,
                CrudOperationNames.Get,
                source: null,
                CrudMetrics.SmallLane,
                () => TranslatedAsync(async () =>
                {
                    EntitiesResult<TEntity> result = await ReadAsync(stack, behavior, request, [id], cancellationToken).ConfigureAwait(false);

                    // The plan's other result sets were read and are discarded with the result.
                    return result.Entities.Count > 0
                        ? result
                        : throw new NotFoundException(stack.Resource, [id.ToString(null, CultureInfo.InvariantCulture)]);
                }),
                cancellationToken);
        }

        /// <summary>Reads rows by ids: what is visible, in request order.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The visible rows, what they point at, and the extras.</returns>
        public Task<EntitiesResult<TEntity>> GetByIdsAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, IReadOnlyList<TKey> ids, DetailsRequest request, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(request);
            return operations.RunAsync(
                stack.Resource,
                CrudOperationNames.GetByIds,
                source: null,
                CrudMetrics.Lane(ids.Count, data.Value.LargeBatchThreshold),
                () => TranslatedAsync(() => ReadAsync(stack, behavior, request, ids, cancellationToken)),
                cancellationToken);
        }

        /// <summary>
        ///     Reads every row of a cacheable stack from the cache, with no round trip on a warm one; a
        ///     table beyond the cache's row limit is read whole, and requested extras ride one details
        ///     round trip over the rows.
        /// </summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Every row.</returns>
        public Task<EntitiesResult<TEntity>> GetAllCachedAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, DetailsRequest request, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(request);
            return operations.RunAsync(
                stack.Resource,
                CrudOperationNames.All,
                source: null,
                CrudMetrics.SmallLane,
                () => TranslatedAsync(() => AllAsync(stack, behavior, request, cancellationToken)),
                cancellationToken);
        }

        /// <summary>Runs a read's steps with an exhausted retry translated into what the caller can act on.</summary>
        /// <typeparam name="TResult">What the read returns.</typeparam>
        /// <param name="steps">The read's steps.</param>
        /// <returns>What the read returned.</returns>
        private static async Task<TResult> TranslatedAsync<TResult>(Func<Task<TResult>> steps)
        {
            try
            {
                return await steps().ConfigureAwait(false);
            }
            catch (DataAccessRetryExhaustedException failure)
            {
                // Deadlocks and transient failures outlasted the executor's retries: worth a retry soon.
                throw new DependencyUnavailableException(DatabaseDependency, DatabaseRetryAfter, failure);
            }
        }

        /// <summary>The steps of a read by ids: the check, the ceilings, then the plan's one round trip.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="ids">The ids.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The result.</returns>
        private async Task<EntitiesResult<TEntity>> ReadAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, DetailsRequest request, IReadOnlyList<TKey> ids, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            // --- The type-level grant, then the ceilings: both refuse before any round trip but the connect ---
            _ = await evaluator.RequireAsync(stack.Resource, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            if (ids.Count > stack.Limits.MaxIds)
            {
                throw new LimitExceededException(LimitNames.MaxIds, ids.Count, stack.Limits.MaxIds);
            }

            DetailsPlanner.Admit(stack, request);

            // A repeated id is read and returned once, where it first appears.
            TKey[] distinct = [.. ids.Distinct()];
            return await RunPlanAsync(stack, behavior, request, distinct, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>The steps of a read of every cached row.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the reads.</param>
        /// <returns>Every row.</returns>
        private async Task<EntitiesResult<TEntity>> AllAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, DetailsRequest request, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            // --- The type-level grant, which carries no row filter on a cacheable stack, then the ceilings ---
            _ = await evaluator.RequireAsync(stack.Resource, AccessActions.Read, cancellationToken).ConfigureAwait(false);
            DetailsPlanner.Admit(stack, request);

            // --- The cache: warm, no round trip; cold, the capped probe's one ---
            CacheResult<CachedEntitySet<TEntity>> cached = await cacheable.GetAsync<TEntity>().ConfigureAwait(false);
            if (cached.Value is CachedEntitySet<TEntity> list)
            {
                if (request.Include.Count == 0)
                {
                    return new EntitiesResult<TEntity>
                    {
                        Ids = [.. list.Items.Select(static entity => long.CreateChecked(entity.Id))],
                        Entities = list.Items,
                    };
                }

                // The extras ride one details round trip over the rows the cache returned.
                return await RunPlanAsync(stack, behavior, request, [.. list.Items.Select(static entity => entity.Id)], cancellationToken).ConfigureAwait(false);
            }

            // --- A table beyond the cache's row limit: read whole, on every call, with the extras when requested ---
            return request.Include.Count == 0
                ? await ReadWholeAsync<TEntity, TKey>(cancellationToken).ConfigureAwait(false)
                : await RunPlanAsync(stack, behavior, request, ids: null, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Runs the details plan in one guarded round trip, then assembles its result.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks.</param>
        /// <param name="request">The extras to include, admitted.</param>
        /// <param name="ids">The distinct ids in request order; null to read every row.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The result.</returns>
        private async Task<EntitiesResult<TEntity>> RunPlanAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, DetailsRequest request, IReadOnlyList<TKey>? ids, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            DetailsComposition<TEntity, TKey>? composition = null;
            _ = await runner.RunAsync(
                BatchPurpose.Read,
                (user, batch) =>
                {
                    // A recomposition composes the plan afresh, its producers asked again.
                    composition = planner.Compose(stack, behavior, request, ids, idsSource: null, user, batch);
                    return ValueTask.CompletedTask;
                },
                static (_, _) => true,
                cancellationToken).ConfigureAwait(false);

            // The runner may have refreshed the context in its own frame: the rules and the computed
            // extras run in the cultures of the context the batch was composed under.
            accessor.Current.ApplyCultures();
            return composition!.Complete();
        }

        /// <summary>Reads every row of a table the cache does not hold, in one guarded round trip.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>Every row, by key.</returns>
        private async Task<EntitiesResult<TEntity>> ReadWholeAsync<TEntity, TKey>(CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            EntityMetadata entity = metadata.Get(typeof(TEntity));
            BatchResult<EntityQueryResult<TEntity>>? rows = null;
            IReadOnlyList<TEntity> entities = await runner.RunAsync(
                BatchPurpose.Read,
                (_, batch) =>
                {
                    // Every property the tenant's schema declares, as a details read selects them.
                    batch.Limits = QueryexProfiles.PipelineLimits;
                    QueryexSchemaMap map = schemas.Map(accessor.Current.TenantSettings?.Shape ?? throw new InvalidOperationException(
                        "The tenant's settings are not loaded in this scope yet, so the whole table has no schema to be read under."));
                    rows = batch.Query(new EntityQuery<TEntity>
                    {
                        Select = string.Join(", ", map.Declared(entity).Select(static property => property.Name)),
                        AllProperties = true,
                        OrderBy = entity.Key.Name,
                    });
                    return ValueTask.CompletedTask;
                },
                (_, _) => rows!.Value.Entities,
                cancellationToken).ConfigureAwait(false);
            accessor.Current.ApplyCultures();
            return new EntitiesResult<TEntity>
            {
                Ids = [.. entities.Select(static row => long.CreateChecked(row.Id))],
                Entities = entities,
            };
        }
    }
}
