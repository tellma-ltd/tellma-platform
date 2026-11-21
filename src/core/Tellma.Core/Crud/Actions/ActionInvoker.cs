// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.Concurrent;
using System.Reflection;

namespace Tellma.Core.Crud.Actions
{
    // Spec 0015 §3.1: an [EntityAction] method returns Task, ValueTask, Task<TResult> or
    // ValueTask<TResult>, which the startup checks enforce. The method is invoked reflectively, its
    // own exceptions reaching the pipeline unwrapped, and its task is awaited as one task answering
    // the method's result, or null for a method without one.
    /// <summary>Invokes an action method and awaits what it returns as one task answering its result.</summary>
    internal static class ActionInvoker
    {
        /// <summary>The reader of each generic task type's result, by the task type.</summary>
        private static readonly ConcurrentDictionary<Type, Func<object, object?>> Results = new();

        /// <summary>The converter of each generic value task type to its task, by the value task type.</summary>
        private static readonly ConcurrentDictionary<Type, Func<object, Task>> AsTasks = new();

        /// <summary>Invokes a method and awaits its task.</summary>
        /// <param name="method">The method: it returns a task or a value task, with or without a result.</param>
        /// <param name="target">The instance the method is invoked on.</param>
        /// <param name="arguments">The method's arguments.</param>
        /// <returns>The method's result; null for a method without one.</returns>
        /// <exception cref="InvalidOperationException">The method returned null, or something other than a task.</exception>
        public static async Task<object?> InvokeAsync(MethodInfo method, object target, object?[] arguments)
        {
            ArgumentNullException.ThrowIfNull(method);
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(arguments);

            // --- The call: the method's own exception reaches the caller as thrown ---
            object? returned = method.Invoke(target, BindingFlags.DoNotWrapExceptions, binder: null, arguments, culture: null);

            // --- The task it returned, awaited, and its result read ---
            switch (returned)
            {
                case Task task:
                    await task.ConfigureAwait(false);
                    return method.ReturnType.IsGenericType ? ResultOf(method.ReturnType, task) : null;
                case ValueTask valueTask:
                    await valueTask.ConfigureAwait(false);
                    return null;
                case object boxed when boxed.GetType().IsGenericType && boxed.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>):
                    Task converted = AsTasks.GetOrAdd(boxed.GetType(), static type =>
                    {
                        MethodInfo asTask = type.GetMethod(nameof(ValueTask<>.AsTask), Type.EmptyTypes)!;
                        return value => (Task)asTask.Invoke(value, BindingFlags.DoNotWrapExceptions, binder: null, parameters: null, culture: null)!;
                    })(boxed);
                    await converted.ConfigureAwait(false);
                    return ResultOf(typeof(Task<>).MakeGenericType(boxed.GetType().GetGenericArguments()[0]), converted);
                default:
                    throw new InvalidOperationException(
                        $"The action method {method.DeclaringType?.Name}.{method.Name} returned {(returned is null ? "null" : returned.GetType().Name)} where it declares a task.");
            }
        }

        /// <summary>The result of a completed generic task.</summary>
        /// <param name="taskType">The task's declared type: <see cref="Task{TResult}" />.</param>
        /// <param name="task">The completed task.</param>
        /// <returns>The result.</returns>
        private static object? ResultOf(Type taskType, Task task)
        {
            return Results.GetOrAdd(taskType, static type =>
            {
                PropertyInfo result = type.GetProperty(nameof(Task<>.Result))!;
                return completed => result.GetValue(completed);
            })(task);
        }
    }
}
