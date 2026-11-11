// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Tenancy
{
    /// <summary>
    ///     The single decision whether the principal bound to a request may proceed against the
    ///     tenant bound to it. Every front door of a tenant request calls it once, after the tenant
    ///     and the principal are bound and before the handler runs. Scoped.
    /// </summary>
    /// <remarks>
    ///     <para>It decides in three steps, and the first refusal ends the request:</para>
    ///     <list type="number">
    ///         <item>
    ///             <description>
    ///                 The tenant's state: a suspended tenant refuses every request, a frozen one
    ///                 every mutation, and a read-only one every mutation the endpoint does not mark
    ///                 as allowed when read-only.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 Every <see cref="IRequestContextInitializer" />, in order: the user connection,
    ///                 which refuses a caller who is not an active member of the tenant, then the
    ///                 locale negotiation. The scope's context is replaced by each step's result.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 The assurance bar, when the endpoint sets one: a service account is refused,
    ///                 and a session below the bar is asked to step up.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     <para>
    ///         When the call returns, the scope's context names the caller's user in the tenant:
    ///         <see cref="RequestContext.UserId" /> is set, or the request was refused. The guard
    ///         opens no tenant database itself.
    ///     </para>
    /// </remarks>
    public interface ITenantAccessGuard
    {
        /// <summary>Decides whether the bound principal may proceed against the bound tenant, and completes the scope's context.</summary>
        /// <param name="requirement">What the endpoint asks of the tenant and of the session.</param>
        /// <param name="inputs">What the request asked for about its locale and client, handed to every initializer.</param>
        /// <param name="cancellationToken">Cancels the decision.</param>
        /// <returns>A task that completes when the caller may proceed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="requirement" /> or <paramref name="inputs" /> is null.</exception>
        /// <exception cref="TenantNotFoundException">The caller is anonymous or not an active member of the tenant, or the tenant is in a state a request sees as absent.</exception>
        /// <exception cref="TenantUnavailableException">The tenant's state refuses the request: <c>tenant-suspended</c>, <c>tenant-frozen</c> or <c>tenant-read-only</c>.</exception>
        /// <exception cref="IndividualRequiredException">The endpoint sets an assurance bar and the caller is a service account.</exception>
        /// <exception cref="StepUpRequiredException">The caller's session is below the endpoint's assurance bar.</exception>
        /// <exception cref="InvalidOperationException">
        ///     The front door bound no tenant before calling or bound the platform's own principal,
        ///     or no initializer bound the caller's user: a defect of the host's wiring, never a
        ///     caller's outcome.
        /// </exception>
        Task EnsureAccessAsync(
            TenantAccessRequirement requirement, RequestContextInputs inputs, CancellationToken cancellationToken = default);
    }
}
