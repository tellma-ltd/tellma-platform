// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Numerics;
using System.Reflection;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Entities;
using Tellma.Core.Crud.Save;
using Tellma.Core.Data;

namespace Tellma.Core.Crud.Actions
{
    // The actions are spec 0015 §11: the built-in activate and deactivate (§11.1) and the entity
    // actions a service declares (§11.2), each a unit of work the operation scope names and meters
    // (§16.1). The overload an action is reached through is the one its method's result selects
    // (§3.1): a method returning no value answers the target ids and the read-back, one returning a
    // value answers that value; either mismatch is an authoring bug.
    /// <summary>
    ///     The actions of a stack: resolves the action, refuses the overload or the target count it
    ///     does not take, and runs it on the one-round-trip path or the general one. Scoped.
    /// </summary>
    /// <param name="writes">The scope's write services.</param>
    /// <param name="operations">The operation scope.</param>
    /// <param name="registry">The stack registry, which holds each declared action's method.</param>
    internal sealed class ActionOperation(SaveOperation writes, CrudOperations operations, StackRegistry registry)
    {
        /// <summary>Runs an action whose method returns no value: it answers the target ids and, when asked, the rows read back.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks, and the instance a declared action's method runs on.</param>
        /// <param name="name">The action's name.</param>
        /// <param name="ids">The target ids.</param>
        /// <param name="arguments">The arguments: the action's arguments record, or its JSON; null for none.</param>
        /// <param name="options">What the action answers.</param>
        /// <param name="cancellationToken">Cancels the validation rounds; a persist already sent completes regardless.</param>
        /// <returns>The target ids in request order and, when asked, the rows read back.</returns>
        /// <exception cref="InvalidOperationException">The stack declares no such action, or the action answers a value of its own.</exception>
        public Task<EntitiesResult<TEntity>> ExecuteAsync<TEntity, TKey>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, string name, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);
            (EntityActionDescriptor descriptor, MethodInfo? method) = Resolve(stack, name);
            return ResultOf(method) is Type result
                ? throw new InvalidOperationException(
                    $"The action '{name}' of {stack.Resource} answers a {result.Name} of its own, so its service reaches it through ExecuteActionAsync<{result.Name}>.")
                : Run(
                stack,
                ids.Count,
                async () => (EntitiesResult<TEntity>)(await new ActionRun<TEntity, TKey>(writes, stack, behavior, descriptor, method, ids, arguments, options, cancellationToken).RunAsync().ConfigureAwait(false))!,
                cancellationToken);
        }

        /// <summary>Runs an action whose method returns a value of its own, which it answers.</summary>
        /// <typeparam name="TEntity">The stack's entity type.</typeparam>
        /// <typeparam name="TKey">The stack's key type.</typeparam>
        /// <typeparam name="TResult">The action's result type.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="behavior">The service's hooks, and the instance the action's method runs on.</param>
        /// <param name="name">The action's name.</param>
        /// <param name="ids">The target ids.</param>
        /// <param name="arguments">The arguments: the action's arguments record, or its JSON; null for none.</param>
        /// <param name="options">What the action answers; a read-back is never composed.</param>
        /// <param name="cancellationToken">Cancels the validation rounds; a persist already sent completes regardless.</param>
        /// <returns>What the action's method returned, once the action committed.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The stack declares no such action, the action answers no value of its own or one that is no
        ///     <typeparamref name="TResult" />, or a single-target action names other than one id.
        /// </exception>
        public async Task<TResult> ExecuteAsync<TEntity, TKey, TResult>(
            StackDescriptor stack, IEntityBehavior<TEntity, TKey> behavior, string name, IReadOnlyList<TKey> ids, object? arguments, ActionOptions options, CancellationToken cancellationToken)
            where TEntity : Entity<TKey>
            where TKey : struct, IBinaryInteger<TKey>
        {
            ArgumentNullException.ThrowIfNull(stack);
            ArgumentNullException.ThrowIfNull(behavior);
            ArgumentNullException.ThrowIfNull(ids);
            ArgumentNullException.ThrowIfNull(options);

            // --- The overload the action's result selects, and the one target a single-target action takes ---
            (EntityActionDescriptor descriptor, MethodInfo? method) = Resolve(stack, name);
            EnsureAnswers<TResult>(stack, descriptor, method, ids.Count);
            return await Run(
                stack,
                ids.Count,
                async () => (TResult)(await new ActionRun<TEntity, TKey>(writes, stack, behavior, descriptor, method, ids, arguments, options, cancellationToken).RunAsync().ConfigureAwait(false))!,
                cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Refuses the typed overload for an action that answers no value, or another one, and a single-target action called with other than one id.</summary>
        /// <typeparam name="TResult">The result type the caller asks for.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="descriptor">The action.</param>
        /// <param name="method">The action's method; null for a built-in action.</param>
        /// <param name="targets">The target ids' count.</param>
        /// <exception cref="InvalidOperationException">The overload or the target count does not fit the action.</exception>
        private static void EnsureAnswers<TResult>(StackDescriptor stack, EntityActionDescriptor descriptor, MethodInfo? method, int targets)
        {
            Type result = ResultOf(method) ?? throw new InvalidOperationException(
                $"The action '{descriptor.Name}' of {stack.Resource} answers no value of its own, so its service reaches it through ExecuteActionAsync, which answers the target ids.");
            if (!typeof(TResult).IsAssignableFrom(result))
            {
                throw new InvalidOperationException(
                    $"The action '{descriptor.Name}' of {stack.Resource} answers a {result.Name}, which is no {typeof(TResult).Name}.");
            }

            if (descriptor.SingleTarget && targets != 1)
            {
                throw new InvalidOperationException(
                    $"The action '{descriptor.Name}' of {stack.Resource} targets a single row, so it is called with one id, not {targets}.");
            }
        }

        /// <summary>The entity action a stack declares by a name, and its method; null for a built-in action.</summary>
        /// <param name="stack">The stack.</param>
        /// <param name="name">The action's name.</param>
        /// <returns>The descriptor and the method.</returns>
        /// <exception cref="InvalidOperationException">The stack declares no entity action by that name.</exception>
        private (EntityActionDescriptor Descriptor, MethodInfo? Method) Resolve(StackDescriptor stack, string name)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            EntityActionDescriptor descriptor = stack.Actions.OfType<EntityActionDescriptor>().FirstOrDefault(action => string.Equals(action.Name, name, StringComparison.Ordinal))
                ?? throw new InvalidOperationException(
                    $"The stack {stack.Resource} declares no entity action '{name}'. An API action is reached through the action invoker, and an entity action through the name its attribute declares.");
            MethodInfo? method = descriptor.IsBuiltIn ? null : registry.MethodOf(descriptor)?.Method
                ?? throw new InvalidOperationException($"The registry holds no method for the action '{name}' of {stack.Resource}, whose descriptor it built.");
            return (descriptor, method);
        }

        /// <summary>The value an action's method answers of its own.</summary>
        /// <param name="method">The method; null for a built-in action.</param>
        /// <returns>The result type; null when the action answers the target ids.</returns>
        private static Type? ResultOf(MethodInfo? method)
        {
            return method is not null && ActionMethods.TryResult(method.ReturnType, out Type? result) ? result : null;
        }

        /// <summary>Runs an action as a unit of work of the operation scope, metered in the lane its target count picks.</summary>
        /// <typeparam name="TResult">What the action answers.</typeparam>
        /// <param name="stack">The stack.</param>
        /// <param name="targets">The target ids' count.</param>
        /// <param name="steps">The action's steps.</param>
        /// <param name="cancellationToken">The caller's token, which tells an abort from a failure.</param>
        /// <returns>What the action answers.</returns>
        private Task<TResult> Run<TResult>(StackDescriptor stack, int targets, Func<Task<TResult>> steps, CancellationToken cancellationToken)
        {
            string lane = CrudMetrics.Lane(targets, writes.LargeBatchThreshold);
            return operations.RunAsync(stack.Resource, CrudOperationNames.Action, source: null, lane, () => DataAccessTranslation.RunAsync(steps), cancellationToken);
        }
    }
}
