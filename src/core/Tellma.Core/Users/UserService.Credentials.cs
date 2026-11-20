// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.8: three phases across the action's two round trips, with no transaction open
    // during the remote call, as for invite. The client is created between the round trips; the row
    // takes its id through context.Update, which a recompose of round trip 2 replays without a second
    // create. The new client is recorded and the previous one deleted once the action committed, so
    // a failure between the two leaves working credentials; a persist that never commits deletes the
    // new client instead.
    public partial class UserService<TUser>
    {
        /// <summary>
        ///     Issues a service account's client credentials, or rotates them: a new client is
        ///     registered at the identity server, the account is linked to it, and once that committed
        ///     the previous client is deleted. The secret is answered once, in this result, and never
        ///     logged or stored.
        /// </summary>
        /// <param name="context">The action's context, over one target.</param>
        /// <returns>The account's id, its new client id and the client's one-time secret.</returns>
        [EntityAction(IssueCredentialsAction, Action = "Credentials", SingleTarget = true, Description = "Issue or rotate a service account's client credentials")]
        public async Task<CredentialsResult> IssueCredentialsAsync(ActionContext<TUser, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Phase 1 (round trip 1): the target, then every validator's verdict ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            await context.ValidatedAsync().ConfigureAwait(false);
            if (context.Errors.HasErrors)
            {
                await FailValidatedAsync(context).ConfigureAwait(false);
            }

            RequestContext request = context.Context;
            int tenantId = request.Tenant?.Id
                ?? throw new InvalidOperationException("Credentials are issued in a tenant, and the scope's context names none.");
            TUser account = context.Entities.Count == 1
                ? context.Entities[0]
                : throw new InvalidOperationException("The issue-credentials action targets exactly one service account.");
            string? previous = account.Subject;

            // --- Phase 2: the remote call, with no batch in flight; a failure raises with nothing written ---
            IdentityServiceAccount client = await _dependencies.Identity
                .CreateServiceAccountAsync(account.Name, context.CancellationToken).ConfigureAwait(false);

            // --- Phase 3 (round trip 2): the account takes the new client, and joins on its first credentials ---
            try
            {
                Dictionary<string, object?> assignments = new(StringComparer.Ordinal) { [nameof(User.Subject)] = client.ClientId };
                if (account.JoinedAt is null)
                {
                    assignments[nameof(User.JoinedAt)] = request.Now;
                }

                context.Update(assignments);
                await context.Persisted.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Nothing committed: the new client is an orphan, deleted best effort; an orphan the
                // delete misses is harmless, since no row names it.
                await DeleteServiceAccountQuietlyAsync(client.ClientId, account.Id).ConfigureAwait(false);
                throw;
            }

            // --- Phase 4 (post-commit, best effort): the new client recorded, then the previous one deleted ---
            await RecordMembershipsAsync([new TenantMembershipRecord { TenantId = tenantId, Subject = client.ClientId, IsActive = true }]).ConfigureAwait(false);
            if (previous is not null && !string.Equals(previous, client.ClientId, StringComparison.Ordinal))
            {
                await DeleteServiceAccountQuietlyAsync(previous, account.Id).ConfigureAwait(false);
            }

            return new CredentialsResult(account.Id, client.ClientId, client.ClientSecret);
        }
    }
}
