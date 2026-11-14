// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     The one entry to every API action method: the web and agent projections, jobs and tests all
    ///     call through it, so no caller reaches a method whose securable nobody evaluated.
    /// </summary>
    /// <remarks>
    ///     The invoker resolves the descriptor's handler from the request's scope, evaluates its
    ///     securable (or, for a member-only action, requires a connected active member), opens the
    ///     action's write frame, validates the body's annotations, and invokes the method through every
    ///     <see cref="IApiActionInterceptor" />.
    /// </remarks>
    public interface IApiActionInvoker
    {
        /// <summary>Invokes an API action with its body.</summary>
        /// <param name="action">The action.</param>
        /// <param name="body">The body, of the action's request type; null for an action that takes none.</param>
        /// <param name="cancellationToken">Cancels the action's reads and validation; a persist already sent completes regardless.</param>
        /// <returns>The method's result; null for a method that returns none.</returns>
        Task<object?> InvokeAsync(ApiActionDescriptor action, object? body, CancellationToken cancellationToken = default);
    }
}
