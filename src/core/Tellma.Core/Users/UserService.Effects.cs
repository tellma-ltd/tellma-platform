// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Tenancy;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.6: the post-commit side effects of deactivating and activating users, and the
    // helpers the invite and issue-credentials actions share for theirs. Every effect runs after the
    // commit, outside any transaction; a failure is logged and never thrown, and the membership
    // hints a failure leaves stale are repaired by the membership reconciliation, never here.
    public partial class UserService<TUser>
    {
        /// <summary>
        ///     Runs the post-commit effects of the built-in actions: a deactivated user's sessions are
        ///     told the tenant's access is revoked, its membership hint turns inactive, and a service
        ///     account's client is deleted, so it obtains no token; an activated user's hint turns
        ///     active. Users without a subject have nothing to tell. Failures are logged, never thrown.
        /// </summary>
        /// <param name="outcome">The committed operation's outcome.</param>
        /// <returns>A task that completes when the effects ran.</returns>
        protected override async Task AfterCommitAsync(PersistOutcome<TUser> outcome)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            await base.AfterCommitAsync(outcome).ConfigureAwait(false);

            // --- Only the built-in activation actions; invite and issue-credentials run theirs in the method ---
            if (outcome is not ActionPersistOutcome<TUser> action)
            {
                return;
            }

            bool deactivated = string.Equals(action.Action, DeactivateAction, StringComparison.Ordinal);
            bool activated = string.Equals(action.Action, ActivateAction, StringComparison.Ordinal);
            if ((!deactivated && !activated) || outcome.Context.Tenant?.Id is not int tenantId)
            {
                return;
            }

            // The targets are the rows loaded before the update; their subjects and kinds are unchanged by it.
            List<TUser> linked = [.. outcome.Entities.Where(static user => !string.IsNullOrEmpty(user.Subject))];
            if (linked.Count == 0)
            {
                return;
            }

            // --- A deactivation: every session listener told, each failure logged on its own ---
            if (deactivated)
            {
                await RevokeAccessAsync(tenantId, linked).ConfigureAwait(false);
            }

            // --- The membership hints, in one call ---
            await RecordMembershipsAsync(
                [.. linked.Select(user => new TenantMembershipRecord { TenantId = tenantId, Subject = user.Subject!, IsActive = activated })]).ConfigureAwait(false);

            // --- A deactivated service account's client: deleted, so reactivation needs new credentials ---
            if (deactivated)
            {
                foreach (TUser user in linked.Where(static user => user.Kind == UserKind.Service))
                {
                    await DeleteServiceAccountQuietlyAsync(user.Subject!, user.Id).ConfigureAwait(false);
                }
            }
        }

        /// <summary>Tells every session-termination listener that each user's access to the tenant was revoked.</summary>
        /// <param name="tenantId">The tenant.</param>
        /// <param name="users">The deactivated users, each with a subject.</param>
        /// <returns>A task that completes when every listener was told or its failure logged.</returns>
        private async Task RevokeAccessAsync(int tenantId, IReadOnlyList<TUser> users)
        {
            IReadOnlyList<ISessionTerminationListener> listeners;
            try
            {
                listeners = _dependencies.SessionListeners();
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                foreach (TUser user in users)
                {
                    UsersLog.UserAccessRevocationFailed(_dependencies.Logger, failure, nameof(ISessionTerminationListener), user.Id);
                }

                return;
            }

            foreach (TUser user in users)
            {
                foreach (ISessionTerminationListener listener in listeners)
                {
                    try
                    {
                        // A listener that hands back no task fails like one that throws.
                        await (listener.TenantAccessRevokedAsync(tenantId, user.Subject!)
                            ?? throw new InvalidOperationException("The session-termination listener returned no task.")).ConfigureAwait(false);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        UsersLog.UserAccessRevocationFailed(_dependencies.Logger, failure, listener.GetType().FullName ?? listener.GetType().Name, user.Id);
                    }
                }

                UsersLog.UserAccessRevoked(_dependencies.Logger, user.Id);
            }
        }

        /// <summary>Records tenant-membership hints, best effort: a failure is logged and left to the reconciliation.</summary>
        /// <param name="records">The hints; nothing is called for none.</param>
        /// <returns>A task that completes when the hints were recorded or the failure logged.</returns>
        private async Task RecordMembershipsAsync(List<TenantMembershipRecord> records)
        {
            if (records.Count == 0)
            {
                return;
            }

            try
            {
                // The request may be gone by now; the hints are recorded regardless.
                await _dependencies.Memberships.RecordAsync(records, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                UsersLog.MembershipHintFailed(_dependencies.Logger, failure, records.Count);
            }
        }

        /// <summary>Deletes a service account's client at the identity server, best effort: a failure is logged.</summary>
        /// <param name="clientId">The client's id; never logged.</param>
        /// <param name="userId">The service account's user id, which the failure is logged with.</param>
        /// <returns>A task that completes when the client was deleted or the failure logged.</returns>
        private async Task DeleteServiceAccountQuietlyAsync(string clientId, int userId)
        {
            try
            {
                // The request may be gone by now; the delete runs regardless, under the client's own timeout.
                await _dependencies.Identity.DeleteServiceAccountAsync(clientId, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                UsersLog.ServiceAccountDeleteFailed(_dependencies.Logger, failure, userId);
            }
        }
    }
}
