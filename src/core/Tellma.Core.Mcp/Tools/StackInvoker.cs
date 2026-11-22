// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.Mcp.Tools
{
    /// <summary>The records a get read, in the wire's own JSON shape.</summary>
    /// <param name="Ids">The ids of the records read, in request order.</param>
    /// <param name="Result">The entities result as the web surface writes it: <c>ids</c>, <c>entities</c>, <c>related</c>, <c>extras</c>.</param>
    internal sealed record StackRecords(IReadOnlyList<long> Ids, JsonObject Result);

    /// <summary>
    ///     Calls a stack's service, closed over the stack's entity and key types, from a tool that
    ///     knows the stack only by its descriptor. Every call reaches the same service operation the
    ///     web surface calls, so its permissions, filters and limits are the service's.
    /// </summary>
    internal abstract class StackInvoker
    {
        /// <summary>The invokers built, by entity type.</summary>
        private static readonly ConcurrentDictionary<Type, StackInvoker> Invokers = new();

        /// <summary>The invoker of a stack.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>The invoker, built once per entity type.</returns>
        public static StackInvoker For(StackDescriptor stack)
        {
            ArgumentNullException.ThrowIfNull(stack);
            return Invokers.GetOrAdd(stack.EntityType, static (_, descriptor) => Create(descriptor), stack);
        }

        /// <summary>Runs a query on the stack.</summary>
        /// <param name="services">The request's services.</param>
        /// <param name="request">The query.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The page of rows.</returns>
        public abstract Task<RowPage> QueryAsync(IServiceProvider services, QueryRequest request, CancellationToken cancellationToken);

        /// <summary>Reads records of the stack by id, in details shape.</summary>
        /// <param name="services">The request's services.</param>
        /// <param name="ids">The ids, each refused when outside the key's range.</param>
        /// <param name="details">The extras to include.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The records read; an absent or invisible id is omitted.</returns>
        public abstract Task<StackRecords> GetAsync(IServiceProvider services, IReadOnlyList<long> ids, DetailsRequest details, CancellationToken cancellationToken);

        /// <summary>Builds the invoker closed over a stack's types.</summary>
        /// <param name="stack">The stack.</param>
        /// <returns>The invoker.</returns>
        private static StackInvoker Create(StackDescriptor stack)
        {
            Type closed = typeof(StackInvoker<,>).MakeGenericType(stack.EntityType, stack.KeyType);
            return (StackInvoker)Activator.CreateInstance(closed)!;
        }
    }

    /// <summary>The invoker of a stack over <typeparamref name="TEntity" /> keyed by <typeparamref name="TKey" />.</summary>
    /// <typeparam name="TEntity">The stack's leaf entity.</typeparam>
    /// <typeparam name="TKey">The key type.</typeparam>
    internal sealed class StackInvoker<TEntity, TKey> : StackInvoker
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The path a refused id is named under.</summary>
        private const string IdsPath = "ids";

        /// <inheritdoc />
        public override Task<RowPage> QueryAsync(IServiceProvider services, QueryRequest request, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            return services.GetRequiredService<EntityService<TEntity, TKey>>().QueryAsync(request, cancellationToken);
        }

        /// <inheritdoc />
        public override async Task<StackRecords> GetAsync(IServiceProvider services, IReadOnlyList<long> ids, DetailsRequest details, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(services);
            IReadOnlyList<TKey> keys = WireIds.ToKeys<TKey>(ids, IdsPath);
            EntitiesResult<TEntity> result = await services.GetRequiredService<EntityService<TEntity, TKey>>()
                .GetByIdsAsync(keys, details, cancellationToken)
                .ConfigureAwait(false);

            // The wire's own JSON, so an agent reads exactly what a details page reads.
            JsonObject json = JsonSerializer.SerializeToNode(result, TellmaJson.Options)?.AsObject()
                ?? throw new InvalidOperationException("An entities result serialized to nothing.");
            return new StackRecords(result.Ids, json);
        }
    }
}
