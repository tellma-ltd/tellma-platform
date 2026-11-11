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

            // A compare-and-swap loop rather than a lock: the binding rule is checked against exactly
            // the context the swap replaces.
            while (true)
            {
                RequestContext? current = Volatile.Read(ref _current);
                if (current?.Tenant is { } bound)
                {
                    EnsureSameBinding(current, bound, context);
                }

                if (ReferenceEquals(Interlocked.CompareExchange(ref _current, context, current), current))
                {
                    return;
                }
            }
        }

        /// <summary>
        ///     Refuses a context that would rebind a scope already bound to a tenant: to another
        ///     tenant or none, to another principal, or to another user than the one connected.
        /// </summary>
        /// <param name="current">The scope's context.</param>
        /// <param name="bound">The tenant the scope is bound to.</param>
        /// <param name="context">The context offered.</param>
        /// <exception cref="InvalidOperationException">The context rebinds the scope.</exception>
        private static void EnsureSameBinding(RequestContext current, TenantDescriptor bound, RequestContext context)
        {
            if (context.Tenant?.Id != bound.Id)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The scope is bound to tenant {bound.Id} and cannot be rebound to {Describe(context)}: a scope serves one tenant for its whole lifetime."));
            }

            // The writers bind the tenant and the principal in one write, so whoever the scope was
            // bound for is whoever the access guard admitted. A later write naming someone else would
            // run the rest of the work as a principal nobody checked. Subjects are left out of the
            // text: they identify a person.
            if (context.Kind != current.Kind || !string.Equals(context.Subject, current.Subject, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The scope is bound to a principal of kind {current.Kind} in tenant {bound.Id} and cannot be rebound to another principal: a scope serves one principal for its whole lifetime."));
            }

            // The user is bound once, by the connection step; before that it is unknown, not someone else.
            if (current.UserId is { } user && context.UserId != user)
            {
                string offered = context.UserId is { } other
                    ? string.Create(CultureInfo.InvariantCulture, $"user {other}")
                    : "no user";
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The scope is bound to user {user} of tenant {bound.Id} and cannot be rebound to {offered}: a scope serves one user for its whole lifetime."));
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
