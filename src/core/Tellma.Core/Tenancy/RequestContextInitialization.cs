// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     Runs the request-context initializers over a scope's context, the same way for the access
    ///     guard of a request and for the scope factory of background work.
    /// </summary>
    internal static class RequestContextInitialization
    {
        /// <summary>
        ///     Runs every initializer in ascending order, initializers of equal order in registration
        ///     order, and sets the scope's context to each one's result before the next runs.
        /// </summary>
        /// <param name="holder">The scope's context, already bound to <paramref name="context" />.</param>
        /// <param name="initializers">The scope's initializers, in registration order.</param>
        /// <param name="context">The context the platform bound.</param>
        /// <param name="inputs">What the request asked for; empty for background work.</param>
        /// <param name="cancellationToken">Cancels the run between and inside initializers.</param>
        /// <returns>The completed context, which the scope's context now is.</returns>
        /// <exception cref="InvalidOperationException">
        ///     An initializer returned no context, or one bound to another tenant or another
        ///     principal than the one it was given.
        /// </exception>
        public static async Task<RequestContext> RunAsync(
            IRequestContextHolder holder,
            IEnumerable<IRequestContextInitializer> initializers,
            RequestContext context,
            RequestContextInputs inputs,
            CancellationToken cancellationToken)
        {
            // OrderBy is a stable sort, so initializers of equal order keep their registration order.
            foreach (IRequestContextInitializer initializer in initializers.OrderBy(static initializer => initializer.Order))
            {
                cancellationToken.ThrowIfCancellationRequested();

                RequestContext next = await initializer.InitializeAsync(context, inputs, cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException(
                        $"The request-context initializer {initializer.GetType().Name} returned no context.");

                // An initializer completes the context; it never rebinds it. A result naming another
                // tenant or another principal would run the rest of the work as someone it is not.
                if (next.Tenant?.Id != context.Tenant?.Id
                    || next.Kind != context.Kind
                    || !string.Equals(next.Subject, context.Subject, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"The request-context initializer {initializer.GetType().Name} changed the tenant or the principal the context is bound to.");
                }

                // Set after every step, so the services the next initializer uses already read what
                // this one resolved (the user, before the locale negotiation reads their preferences).
                holder.Set(next);
                context = next;
            }

            return context;
        }
    }
}
