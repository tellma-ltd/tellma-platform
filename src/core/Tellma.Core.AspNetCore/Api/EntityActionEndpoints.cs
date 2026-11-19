// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Numerics;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §4.2: an entity action's body follows its result and target. One answering the
    // stack's entities reads EntityActionRequest(<TArguments>) and calls ExecuteActionAsync; one
    // targeting a single row reads IdRequest(<TArguments>); any other reads IdsRequest(<TArguments>);
    // both of the last call ExecuteActionAsync<TResult>. The built-in activate and deactivate are
    // entity actions answering the entities, so they take the first path, which is the one the
    // service's activation operations forward to. The service evaluates the action's securable.

    /// <summary>The web endpoints of one entity action of a stack.</summary>
    /// <typeparam name="TEntity">The stack's entity leaf.</typeparam>
    /// <typeparam name="TKey">The stack's key type.</typeparam>
    /// <param name="action">The action, as the registry described it.</param>
    /// <param name="preSerialized">Whether the action is a platform action whose result is a pre-serialized JSON document, written as its bytes.</param>
    internal sealed class EntityActionEndpoints<TEntity, TKey>(EntityActionDescriptor action, bool preSerialized)
        where TEntity : Entity<TKey>
        where TKey : struct, IBinaryInteger<TKey>
    {
        /// <summary>The path a single id binds as.</summary>
        private const string IdPath = "id";

        /// <summary>The path a list of ids binds as.</summary>
        private const string IdsPath = "ids";

        /// <summary>The action, as the registry described it.</summary>
        public EntityActionDescriptor Action { get; } = action ?? throw new ArgumentNullException(nameof(action));

        /// <summary>Whether the action's result is a pre-serialized JSON document, written as its bytes.</summary>
        public bool PreSerialized { get; } = preSerialized;

        /// <summary>An action answering the stack's entities that takes no arguments.</summary>
        /// <param name="request">The target ids and what to answer.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The target ids in request order and, when asked, the rows read back.</returns>
        public Task<EntitiesResult<TEntity>> EntitiesAsync(
            [FromBody] EntityActionRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            return Entities(request, arguments: null, service, cancellationToken);
        }

        /// <summary>An action answering the stack's entities that takes arguments.</summary>
        /// <typeparam name="TArguments">The action's arguments record.</typeparam>
        /// <param name="request">The target ids, the arguments and what to answer.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The target ids in request order and, when asked, the rows read back.</returns>
        public Task<EntitiesResult<TEntity>> EntitiesWithArgumentsAsync<TArguments>(
            [FromBody] EntityActionRequest<TArguments> request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Entities(request, request.Arguments, service, cancellationToken);
        }

        /// <summary>An action over many rows answering its own result, taking no arguments.</summary>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="request">The target ids.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> IdsAsync<TResult>(
            [FromBody] IdsRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Own<TResult>(WireIds.ToKeys<TKey>(request.Ids, IdsPath), arguments: null, service, cancellationToken);
        }

        /// <summary>An action over many rows answering its own result, taking arguments.</summary>
        /// <typeparam name="TArguments">The action's arguments record.</typeparam>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="request">The target ids and the arguments.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> IdsWithArgumentsAsync<TArguments, TResult>(
            [FromBody] IdsRequest<TArguments> request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Own<TResult>(WireIds.ToKeys<TKey>(request.Ids, IdsPath), request.Arguments, service, cancellationToken);
        }

        /// <summary>A single-target action answering its own result, taking no arguments.</summary>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="request">The target id.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> IdAsync<TResult>(
            [FromBody] IdRequest request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Own<TResult>([WireIds.ToKey<TKey>(request.Id, IdPath)], arguments: null, service, cancellationToken);
        }

        /// <summary>A single-target action answering its own result, taking arguments.</summary>
        /// <typeparam name="TArguments">The action's arguments record.</typeparam>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="request">The target id and the arguments.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> IdWithArgumentsAsync<TArguments, TResult>(
            [FromBody] IdRequest<TArguments> request,
            [FromServices] EntityService<TEntity, TKey> service,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            return Own<TResult>([WireIds.ToKey<TKey>(request.Id, IdPath)], request.Arguments, service, cancellationToken);
        }

        /// <summary>Runs the action as one answering the stack's entities.</summary>
        /// <param name="request">The target ids and what to answer.</param>
        /// <param name="arguments">The typed arguments; null for none.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The entities' envelope.</returns>
        private Task<EntitiesResult<TEntity>> Entities(
            EntityActionRequest request, object? arguments, EntityService<TEntity, TKey> service, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(service);
            IReadOnlyList<TKey> ids = WireIds.ToKeys<TKey>(request.Ids, IdsPath);
            ActionOptions options = new(request.ReturnEntities, WireBinding.Details(request.Include));
            return service.ExecuteActionAsync(Action.Name, ids, arguments, options, cancellationToken);
        }

        /// <summary>Runs the action as one answering its own result, with no read-back.</summary>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="ids">The target ids, narrowed to the key.</param>
        /// <param name="arguments">The typed arguments; null for none.</param>
        /// <param name="service">The stack's service.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        private async Task<IResult> Own<TResult>(
            IReadOnlyList<TKey> ids, object? arguments, EntityService<TEntity, TKey> service, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(service);
            TResult result = await service.ExecuteActionAsync<TResult>(Action.Name, ids, arguments, new ActionOptions(), cancellationToken).ConfigureAwait(false);
            return ActionAnswers.Of(result, Action.ResultType, PreSerialized);
        }
    }
}
