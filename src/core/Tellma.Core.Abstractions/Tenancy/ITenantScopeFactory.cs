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
    ///     <para>
    ///         It sets no current culture either, and could not: a culture set inside this call does
    ///         not flow back to the caller. The caller that runs work in the scope first calls
    ///         <see cref="RequestContext.ApplyCultures" /> on the scope's context, in the method that
    ///         runs the work, so server text is formatted and localized for the run-as user.
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
        /// <exception cref="TenantNotFoundException">The run-as user is not an active member of the tenant. Thrown for nothing else.</exception>
        /// <exception cref="TenantUnavailableException">
        ///     The tenant does not serve the work. A state that refuses it comes with the state in
        ///     <see cref="TenantUnavailableException.State" /> and the code of the state:
        ///     <c>tenant-suspended</c>, <c>tenant-frozen</c>, or <c>tenant-not-found</c> for a
        ///     provisioning or retired tenant. A tenant the catalog no longer holds comes with
        ///     <c>tenant-not-found</c> and no state. Also thrown with <c>catalog-unavailable</c> when
        ///     the tenant snapshot cannot be served.
        /// </exception>
        Task<TenantScope> CreateScopeAsync(
            RequestContextSnapshot snapshot, bool allowNonActive = false, CancellationToken cancellationToken = default);
    }
}
