// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;
using Tellma.Core.Abstractions.Composition;

namespace Tellma.Core.Abstractions.Crud
{
    /// <summary>
    ///     Wraps the invocation of API action methods, each interceptor wrapping the next, in
    ///     registration order. Registered only by the platform itself, never by a distribution.
    /// </summary>
    [CollectionService]
    [SuppressMessage(
        "Naming",
        "CA1716:Identifiers should not match keywords",
        Justification = "next is the conventional name of a middleware continuation; the platform's interceptors are C#, where it is not a keyword.")]
    public interface IApiActionInterceptor
    {
        /// <summary>Runs around one invocation; an interceptor that does not apply calls <paramref name="next" /> at once.</summary>
        /// <param name="action">The action being invoked.</param>
        /// <param name="body">The body, of the action's request type; null for an action that takes none.</param>
        /// <param name="next">Invokes the next interceptor, and the method after the last.</param>
        /// <param name="cancellationToken">The invocation's cancellation.</param>
        /// <returns>The result to answer, ordinarily what <paramref name="next" /> returned.</returns>
        Task<object?> InvokeAsync(ApiActionDescriptor action, object? body, Func<Task<object?>> next, CancellationToken cancellationToken);
    }
}
