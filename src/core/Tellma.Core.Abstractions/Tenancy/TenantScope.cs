// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     A service scope bound to one tenant, opened by <see cref="ITenantScopeFactory" />. Resolve
    ///     the work's services from <see cref="Services" /> and dispose the scope when the work is
    ///     done.
    /// </summary>
    /// <remarks>
    ///     A handle to a resource, so two scopes are equal only when they are the same scope.
    ///     Disposing more than once is harmless.
    /// </remarks>
    public sealed class TenantScope : IAsyncDisposable
    {
        /// <summary>What disposing this scope disposes; null once disposed.</summary>
        private IAsyncDisposable? _owner;

        /// <summary>Wraps a service scope that is already bound to a tenant.</summary>
        /// <param name="services">The scope's services.</param>
        /// <param name="owner">The underlying scope, disposed with this one.</param>
        /// <exception cref="ArgumentNullException"><paramref name="services" /> or <paramref name="owner" /> is null.</exception>
        public TenantScope(IServiceProvider services, IAsyncDisposable owner)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(owner);

            Services = services;
            _owner = owner;
        }

        /// <summary>The scope's services, every one of them bound to the scope's tenant.</summary>
        public IServiceProvider Services { get; }

        /// <summary>Disposes the underlying scope and every scoped service resolved from it.</summary>
        /// <returns>A task that completes when the scope is disposed.</returns>
        public ValueTask DisposeAsync()
        {
            // Exchanged so two racing disposals dispose the underlying scope once.
            IAsyncDisposable? owner = Interlocked.Exchange(ref _owner, null);
            return owner is null ? ValueTask.CompletedTask : owner.DisposeAsync();
        }
    }
}
