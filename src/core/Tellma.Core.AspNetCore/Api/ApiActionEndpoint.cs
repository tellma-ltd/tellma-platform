// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Api
{
    // Spec 0016 §4.2 and §4.4: an API action reads its request type, or nothing when its method takes
    // no body, and hands it to the action invoker, which evaluates the securable (or requires a
    // connected member for a member-only action), opens the write frame and invokes the method. The
    // web layer never calls an action method itself.

    /// <summary>The web endpoint of one API action, on a stack, a child query surface or a routed service.</summary>
    /// <param name="action">The action, as the registry described it.</param>
    internal sealed class ApiActionEndpoint(ApiActionDescriptor action)
    {
        /// <summary>The action, as the registry described it.</summary>
        public ApiActionDescriptor Action { get; } = action ?? throw new ArgumentNullException(nameof(action));

        /// <summary>An action whose method takes a body.</summary>
        /// <typeparam name="TRequest">The body's type.</typeparam>
        /// <param name="request">The body.</param>
        /// <param name="invoker">The action invoker.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> InvokeAsync<TRequest>(
            [FromBody] TRequest request,
            [FromServices] IApiActionInvoker invoker,
            [FromServices] IRequestContextAccessor context,
            CancellationToken cancellationToken)
        {
            return Invoke(request, invoker, context, cancellationToken);
        }

        /// <summary>An action whose method takes no body.</summary>
        /// <param name="invoker">The action invoker.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        public Task<IResult> InvokeWithoutBodyAsync(
            [FromServices] IApiActionInvoker invoker,
            [FromServices] IRequestContextAccessor context,
            CancellationToken cancellationToken)
        {
            return Invoke(body: null, invoker, context, cancellationToken);
        }

        /// <summary>Invokes the action and answers its result.</summary>
        /// <param name="body">The body; null for a method that takes none.</param>
        /// <param name="invoker">The action invoker.</param>
        /// <param name="context">The request's context.</param>
        /// <param name="cancellationToken">The request's cancellation.</param>
        /// <returns>The action's result, answered under its declared type.</returns>
        private async Task<IResult> Invoke(object? body, IApiActionInvoker invoker, IRequestContextAccessor context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(invoker);
            object? result = await WireBinding.SettledAsync(context, () => invoker.InvokeAsync(Action, body, cancellationToken)).ConfigureAwait(false);
            return ActionAnswers.Of(result, Action.ResultType);
        }
    }
}
