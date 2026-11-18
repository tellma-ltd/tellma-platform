// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Access;
using Tellma.Core.Crud.Enlistment;
using Tellma.Core.Crud.Save;

namespace Tellma.Core.Crud.Actions
{
    // Spec 0015 §11.3: the one entry to every [ApiAction] method. The descriptor is the registry's
    // own, matched by reference, so no copy reaches a method whose securable nobody evaluated. The
    // invoker resolves the handler from the request's scope, evaluates the securable (or, for a
    // member-only action, connects the caller as an active member: the connect cache, else one
    // prologue-only round trip), checks the body's annotations, then runs the method in the frame
    // of §13.3, the scope's IOpenWriteHost, through every interceptor in registration order.
    /// <summary>Invokes API action methods: the authority, the body's annotations, the frame and the interceptors. Scoped.</summary>
    /// <param name="writes">The scope's write services.</param>
    /// <param name="operations">The operation scope, whose unit of work the action names.</param>
    /// <param name="connection">The scope's user connection, which a member-only action requires.</param>
    /// <param name="interceptors">The interceptors, in registration order: the first wraps the rest.</param>
    internal sealed class ApiActionInvoker(
        SaveOperation writes,
        CrudOperations operations,
        IScopeConnection connection,
        IEnumerable<IApiActionInterceptor> interceptors) : IApiActionInvoker
    {
        /// <summary>The interceptors, in registration order.</summary>
        private readonly IApiActionInterceptor[] _interceptors = [.. interceptors ?? throw new ArgumentNullException(nameof(interceptors))];

        /// <inheritdoc />
        /// <exception cref="InvalidOperationException">
        ///     The descriptor is not one the stack registry holds, or the body is not of the action's
        ///     request type.
        /// </exception>
        /// <exception cref="BadRequestException">The action takes a body, and none was sent.</exception>
        /// <exception cref="ValidationException">An annotation of the body failed.</exception>
        public async Task<object?> InvokeAsync(ApiActionDescriptor action, object? body, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(action);

            // --- The registry's descriptor itself: an equal copy names no method ---
            ActionMethod method = writes.Registry.MethodOf(action) ?? throw new InvalidOperationException(
                $"The API action '{action.Name}' is not a descriptor the stack registry holds. Pass the descriptor the registry lists, never a copy.");
            object? request = BodyOf(action, method, body);

            // --- The handler, from the request's scope; then the authority ---
            object handler = writes.Services.GetRequiredService(method.Handler);
            await AuthorizeAsync(action, cancellationToken).ConfigureAwait(false);

            // --- The body's annotations: every failure at once, each at a member of the body ---
            if (request is not null)
            {
                ValidationErrors errors = new();
                writes.Walker.CheckBody(request, ValidationPath.Root, errors);
                if (errors.HasErrors)
                {
                    throw new ValidationException(errors.Items);
                }
            }

            // --- The frame, the scope's open frame and its unit of work, around the interceptors and the method ---
            operations.Name(method.Owner, action.Name);
            object?[] arguments = ArgumentsOf(method, request, cancellationToken);
            var frame = OpenWriteFrame.ForApiAction(writes, action, method, cancellationToken);
            return await frame.RunAsync(
                () => Intercepted(action, request, () => ActionInvoker.InvokeAsync(method.Method, handler, arguments), cancellationToken)).ConfigureAwait(false);
        }

        /// <summary>The body the method takes: none for an action without a request type, else one of that type.</summary>
        /// <param name="action">The action.</param>
        /// <param name="method">The action's method.</param>
        /// <param name="body">The body as passed.</param>
        /// <returns>The body; null for an action that takes none.</returns>
        /// <exception cref="InvalidOperationException">The caller passed a body of another type, or one to an action that takes none.</exception>
        /// <exception cref="BadRequestException">The action takes a body, and none was sent.</exception>
        private static object? BodyOf(ApiActionDescriptor action, ActionMethod method, object? body)
        {
            // --- An action without a request type takes none ---
            Type? type = action.RequestType;
            if (type is null)
            {
                return body is null
                    ? null
                    : throw new InvalidOperationException($"The API action '{action.Name}' of {method.Owner} takes no body, yet its caller passed a {body.GetType().Name}.");
            }

            // --- One with a request type takes a body of that type ---
            return body switch
            {
                null => throw new BadRequestException($"The action '{action.Name}' takes a body, and none was sent."),
                _ when type.IsInstanceOfType(body) => body,
                _ => throw new InvalidOperationException($"The API action '{action.Name}' of {method.Owner} takes a {type.Name}, yet its caller passed a {body.GetType().Name}."),
            };
        }

        /// <summary>The method's arguments: the body when it takes one, then the invocation's token when it takes one.</summary>
        /// <param name="method">The action's method.</param>
        /// <param name="request">The body; null for an action that takes none.</param>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>The arguments, in the method's parameter order.</returns>
        private static object?[] ArgumentsOf(ActionMethod method, object? request, CancellationToken cancellationToken)
        {
            List<object?> arguments = new(2);
            if (request is not null)
            {
                arguments.Add(request);
            }

            if (method.TakesCancellationToken)
            {
                arguments.Add(cancellationToken);
            }

            return [.. arguments];
        }

        /// <summary>Evaluates the action's securable, or for a member-only action connects the caller as an active member.</summary>
        /// <param name="action">The action.</param>
        /// <param name="cancellationToken">Cancels the evaluation or the connection.</param>
        /// <returns>A task that completes when the caller is authorised.</returns>
        /// <exception cref="ForbiddenException">The caller holds no grant of the securable.</exception>
        /// <exception cref="Abstractions.Tenancy.TenantNotFoundException">A member-only action's caller is not an active member of the tenant.</exception>
        private async Task AuthorizeAsync(ApiActionDescriptor action, CancellationToken cancellationToken)
        {
            if (action.Securable is SecurableRef securable)
            {
                _ = await writes.Evaluator.RequireAsync(securable.Resource, securable.Action, cancellationToken).ConfigureAwait(false);
            }
            else if (!writes.Accessor.Current.IsSystem)
            {
                // The connect cache answers a caller already connected; otherwise one prologue-only round trip.
                _ = await connection.CurrentAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Runs the method through every interceptor, the first registered outermost.</summary>
        /// <param name="action">The action.</param>
        /// <param name="request">The body; null for an action that takes none.</param>
        /// <param name="invoke">Invokes the method.</param>
        /// <param name="cancellationToken">The invocation's token.</param>
        /// <returns>What the outermost interceptor answered, or the method when none is registered.</returns>
        private Task<object?> Intercepted(ApiActionDescriptor action, object? request, Func<Task<object?>> invoke, CancellationToken cancellationToken)
        {
            // Built from the innermost out, so the first registered interceptor wraps the rest.
            Func<Task<object?>> next = invoke;
            for (int index = _interceptors.Length - 1; index >= 0; index--)
            {
                IApiActionInterceptor interceptor = _interceptors[index];
                Func<Task<object?>> inner = next;
                next = () => interceptor.InvokeAsync(action, request, inner, cancellationToken);
            }

            return next();
        }
    }
}
