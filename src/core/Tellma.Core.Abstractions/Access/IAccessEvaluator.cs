// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Abstractions.Access
{
    // The evaluator is spec 0014 §5.1; the deny re-check is §6.4, the witness §4.6 layer 4, and what
    // the pipeline does with a decision §5.3.
    /// <summary>
    ///     Decides what the scope's caller may do with a securable, and why: from the caller's cached
    ///     permission set, every bespoke criterion registered for the securable's resource, and, for a
    ///     child securable, the decision on its owner. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A scope whose context names no caller (anonymous, tenantless, or a principal with
    ///         neither a subject nor a user id) is denied everything, criteria included. The platform's
    ///         own work holds everything. A pair the registry does not hold is denied, with one
    ///         problem of code <see cref="AccessProblemCode.UnknownResource" /> or
    ///         <see cref="AccessProblemCode.UnknownAction" /> that names no stored permission (its
    ///         permission and role ids are 0).
    ///     </para>
    ///     <para>
    ///         A denial from a set no prologue confirmed within the fast-deny window is re-verified
    ///         by one prologue-only round trip and decided again, at most once per scope; a second
    ///         stale denial answers from the set as it is.
    ///     </para>
    /// </remarks>
    public interface IAccessEvaluator
    {
        /// <summary>
        ///     Decides one securable for the caller, records it as evaluated during the scope, and
        ///     counts the decision.
        /// </summary>
        /// <param name="resource">The securable's resource, or a former name of it.</param>
        /// <param name="action">The securable's action.</param>
        /// <param name="cancellationToken">Cancels the deny re-check's round trip.</param>
        /// <returns>The decision.</returns>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<AccessDecision> EvaluateAsync(string resource, string action, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Decides one securable as <see cref="EvaluateAsync(string, string, CancellationToken)" />
        ///     does, and refuses a denial and, on a sensitive securable, a session below the
        ///     configured assurance. This is the check every operation opens with, whoever calls it.
        /// </summary>
        /// <param name="resource">The securable's resource, or a former name of it.</param>
        /// <param name="action">The securable's action.</param>
        /// <param name="cancellationToken">Cancels the deny re-check's round trip.</param>
        /// <returns>The decision, never a denial.</returns>
        /// <exception cref="ForbiddenException">The caller holds no grant and no criterion on the pair (<see cref="ForbiddenReason.NoGrant" />).</exception>
        /// <exception cref="StepUpRequiredException">The pair is sensitive and the session has not reached the configured assurance recently enough.</exception>
        /// <exception cref="IndividualRequiredException">The pair is sensitive and the caller is a service account, which cannot step up.</exception>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<AccessDecision> RequireAsync(string resource, string action, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Decides several securables at once from one set, records each as evaluated and counts
        ///     each decision. One deny re-check at most serves them all.
        /// </summary>
        /// <param name="securables">The securables.</param>
        /// <param name="cancellationToken">Cancels the deny re-check's round trip.</param>
        /// <returns>One decision per securable, in order.</returns>
        /// <exception cref="ArgumentException">The list holds a null item.</exception>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<IReadOnlyList<AccessDecision>> EvaluateAsync(IReadOnlyList<SecurableRef> securables, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Decides one securable for another user of the tenant: "can they, and why". The user's
        ///     permissions are loaded once per scope, by one prologue-only round trip that touches
        ///     neither the caller's set, the process's caches nor the response's version tags, and
        ///     every later call of the scope for that user shares the load. The caller's own id
        ///     answers from the caller's set, and a scope that names no caller is denied with no load.
        ///     Nothing is recorded as evaluated and nothing is counted.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="resource">The securable's resource, or a former name of it.</param>
        /// <param name="action">The securable's action.</param>
        /// <param name="cancellationToken">
        ///     Stops this call's wait for the load; the load itself, shared by the scope's calls, runs
        ///     until it completes or the scope ends.
        /// </param>
        /// <returns>The decision; a denial of everything for an unknown, deactivated or system user.</returns>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<AccessDecision> EvaluateForAsync(int userId, string resource, string action, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Decides every registered securable for the caller from the set as it is, with no deny
        ///     re-check: the summary a caller's own access is shown from. Nothing is recorded as
        ///     evaluated and nothing is counted, so the summary never stands in for an operation's check.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection, when the scope is not connected yet.</param>
        /// <returns>One decision per registered securable, in the registry's order.</returns>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<IReadOnlyList<AccessDecision>> EvaluateAllAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     The caller's permission set: the connected user's, the system set for the platform's own
        ///     work, or an empty set that holds nothing for a scope that names no caller.
        /// </summary>
        /// <param name="cancellationToken">Cancels the connection, when the scope is not connected yet.</param>
        /// <returns>The set.</returns>
        /// <exception cref="TenantNotFoundException">The caller is not, or no longer, an active user of the tenant.</exception>
        Task<UserAccess> GetAccessAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Drops a user's cached permission set, so the next connection of the user loads it
        ///     afresh, and the scope's load of that user's permissions with it.
        /// </summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="userId">The user.</param>
        void Invalidate(int tenantId, int userId);
    }
}
