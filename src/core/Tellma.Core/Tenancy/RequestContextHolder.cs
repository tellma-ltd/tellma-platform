// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The scope's request context: one instance per service scope, read by every service of the
    ///     scope and written by the platform's two writers.
    /// </summary>
    /// <param name="defaults">Builds the tenantless context a scope starts with.</param>
    internal sealed class RequestContextHolder(RequestContextDefaults defaults) : IRequestContextHolder
    {
        /// <summary>The context set or first read; null until either happens.</summary>
        private RequestContext? _current;

        /// <inheritdoc />
        public RequestContext Current
        {
            get
            {
                RequestContext? current = Volatile.Read(ref _current);
                if (current is not null)
                {
                    return current;
                }

                // Nothing bound a context: the scope is tenantless. The context is stamped on the first
                // read and kept, so every reader of the scope sees one instant; a racing writer wins.
                RequestContext tenantless = defaults.Tenantless();
                return Interlocked.CompareExchange(ref _current, tenantless, null) ?? tenantless;
            }
        }

        /// <inheritdoc />
        public void Set(RequestContext context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // A compare-and-swap loop rather than a lock: the tenant rule is checked against exactly
            // the context the swap replaces.
            while (true)
            {
                RequestContext? current = Volatile.Read(ref _current);
                if (current?.Tenant is { } bound && context.Tenant?.Id != bound.Id)
                {
                    throw new InvalidOperationException(string.Create(
                        CultureInfo.InvariantCulture,
                        $"The scope is bound to tenant {bound.Id} and cannot be rebound to {Describe(context)}: a scope serves one tenant for its whole lifetime."));
                }

                if (ReferenceEquals(Interlocked.CompareExchange(ref _current, context, current), current))
                {
                    return;
                }
            }
        }

        /// <summary>Names the tenant a context is bound to, for the refusal's text.</summary>
        /// <param name="context">The context.</param>
        /// <returns>The description.</returns>
        private static string Describe(RequestContext context)
        {
            return context.Tenant is { } tenant
                ? string.Create(CultureInfo.InvariantCulture, $"tenant {tenant.Id}")
                : "no tenant";
        }
    }
}
