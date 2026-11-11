// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Abstractions.Tenancy
{
    /// <summary>
    ///     Opens a service scope bound to a tenant for work no request carries: a background job, a
    ///     provisioning step. It is the only way background work enters a tenant, so everything
    ///     resolved from the scope sees the tenant, the run-as user and that user's locale exactly as
    ///     a request's services do.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The factory looks the tenant up afresh, since its state may have changed since the
    ///         work was enqueued, and serves only an <see cref="TenantState.Active" /> or a
    ///         <see cref="TenantState.ReadOnly" /> tenant. It binds the scope's request context from
    ///         the snapshot and runs every <see cref="IRequestContextInitializer" /> with
    ///         <see cref="RequestContextInputs.Empty" /> before it returns the scope.
    ///     </para>
    ///     <para>
    ///         It starts no activity: the caller owns the activity the scope runs under, so the
    ///         scope's spans nest under the caller's.
    ///     </para>
    ///     <para>A singleton, safe to call from any thread.</para>
    /// </remarks>
    public interface ITenantScopeFactory
    {
        /// <summary>Opens a scope bound to the snapshot's tenant and principal.</summary>
        /// <param name="snapshot">The tenant and the principal the work runs for.</param>
        /// <param name="allowNonActive">
        ///     Also serve a <see cref="TenantState.Provisioning" /> or a
        ///     <see cref="TenantState.Frozen" /> tenant: the door of the platform's own maintenance
        ///     work, open only to a snapshot whose kind is <see cref="PrincipalKind.System" />. Never
        ///     set for a job.
        /// </param>
        /// <param name="cancellationToken">Cancels the scope's creation.</param>
        /// <returns>The scope; the caller disposes it.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="snapshot" /> is null.</exception>
        /// <exception cref="ArgumentException">The snapshot's kind is <see cref="PrincipalKind.Anonymous" />: background work always runs for a principal.</exception>
        /// <exception cref="InvalidOperationException"><paramref name="allowNonActive" /> is set and the snapshot's kind is not <see cref="PrincipalKind.System" />.</exception>
        /// <exception cref="TenantNotFoundException">No tenant has the snapshot's id, or the run-as user is not an active member of it.</exception>
        /// <exception cref="TenantUnavailableException">
        ///     The tenant's state does not serve the work, with the state in
        ///     <see cref="TenantUnavailableException.State" /> and the code of the state:
        ///     <c>tenant-suspended</c>, <c>tenant-frozen</c>, or <c>tenant-not-found</c> for a
        ///     provisioning or retired tenant. Also thrown with <c>catalog-unavailable</c> when the
        ///     tenant snapshot cannot be served.
        /// </exception>
        Task<TenantScope> CreateScopeAsync(
            RequestContextSnapshot snapshot, bool allowNonActive = false, CancellationToken cancellationToken = default);
    }
}
