// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.AspNetCore.Api
{
    /// <summary>What every projected endpoint does around its service call.</summary>
    internal static class WireBinding
    {
        /// <summary>The details a request's <c>include</c> names.</summary>
        /// <param name="include">The extras by name; null for none.</param>
        /// <returns>The details request.</returns>
        public static DetailsRequest Details(IReadOnlyList<string>? include)
        {
            return include is { Count: > 0 } ? new DetailsRequest { Include = include } : DetailsRequest.None;
        }

        /// <summary>
        ///     Runs a service call, then re-applies the request's cultures, whether the call answered
        ///     or threw: a call that re-composed the request context cannot change its caller's
        ///     cultures from inside, and the answer, a problem included, is written in them.
        /// </summary>
        /// <typeparam name="TResult">The call's result type.</typeparam>
        /// <param name="context">The request's context.</param>
        /// <param name="call">The service call.</param>
        /// <returns>The call's result.</returns>
        public static async Task<TResult> SettledAsync<TResult>(IRequestContextAccessor context, Func<Task<TResult>> call)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(call);
            try
            {
                return await call().ConfigureAwait(false);
            }
            finally
            {
                context.Current.ApplyCultures();
            }
        }
    }
}
