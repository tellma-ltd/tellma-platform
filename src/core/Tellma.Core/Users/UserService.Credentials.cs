// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.8: three phases across the action's two round trips, with no transaction open
    // during the remote call, as for invite. The client is created between the round trips; the row
    // takes its id through a guarded statement on the action's batch, which a recompose of round
    // trip 2 replays without a second create. The new client is recorded and the previous one
    // deleted once the action committed, so a failure between the two leaves working credentials; a
    // persist that failed deletes the new client instead, unless a read-back finds the commit
    // happened.
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
            // An error, an unconfirmed warning or the tenant's state refuses the action after the
            // rounds: the identity server is never called for an action that will not persist.
            if (!await context.ValidatedAsync().ConfigureAwait(false))
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

            // --- Phase 3 (round trip 2): the account joins on its first credentials, then takes the new
            // client only while it is still active and still names the client loaded in round trip 1 ---
            try
            {
                // The update re-checks the target under the caller's read and action filters. It
                // assigns what a service account always holds (invite refuses one, so it never has an
                // invitation error), so it writes nothing; the guarded statement after it is the write.
                context.Update(new Dictionary<string, object?>(StringComparer.Ordinal) { [nameof(User.LastInviteError)] = null });
                _ = ComposeClientSwap(context.Batch, account.Id, previous, client.ClientId, request.Now);
                await context.Persisted.ConfigureAwait(false);
            }
            catch (Exception failure)
            {
                await SettleNewClientAsync(failure, account.Id, previous, client.ClientId, tenantId).ConfigureAwait(false);
                throw;
            }

            // --- Phase 4 (post-commit, best effort): the new client recorded, then the previous one deleted ---
            await AfterLinkedAsync(account.Id, previous, client.ClientId, tenantId).ConfigureAwait(false);
            return new CredentialsResult(account.Id, client.ClientId, client.ClientSecret);
        }

        /// <summary>
        ///     Appends the statement that links the account to its new client and joins it on its
        ///     first credentials: only while the account is active and still names the client it was
        ///     loaded with, else the batch fails as inactive or as changed concurrently, and nothing
        ///     commits. The row is stamped and the account's preferences tag moved in the epilogue.
        /// </summary>
        /// <param name="batch">The action's batch.</param>
        /// <param name="userId">The account's id.</param>
        /// <param name="previous">The client the account was loaded with; null for none.</param>
        /// <param name="clientId">The new client's id.</param>
        /// <param name="now">When the account joins, if it has not yet.</param>
        /// <returns>The statement's result, which carries no result set.</returns>
        internal static BatchResult<RawResult> ComposeClientSwap(IDataBatch batch, int userId, string? previous, string clientId, DateTimeOffset now)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentException.ThrowIfNullOrEmpty(clientId);
            SqlIdentifier ids = batch.Tvp([new IdList { Id = userId }]);
            SqlOptions options = new(writes: new HashSet<TableName> { PersistChecks.Users }, userIds: ids);

            // Two concurrent issues load the same client: the second to commit finds the first's and
            // fails, so no caller is answered credentials the row no longer names. A deactivation that
            // committed in between fails it too. The codes are AccessCodes.Inactive and
            // AccessCodes.CredentialsConcurrent; a THROW's message is a literal. The subject and the
            // join are one assignment, as the service accounts' evidence constraint requires.
            return previous is null
                ? batch.Sql(
                    $"""
                    UPDATE [core].[Users] SET [Subject] = {clientId}, [JoinedAt] = COALESCE([JoinedAt], {now}), [ModifiedAt] = SYSUTCDATETIME(), [ModifiedById] = @tm_UserId
                    WHERE [Id] = {userId} AND [IsActive] = 1 AND [Subject] IS NULL;
                    IF @@ROWCOUNT <> 1
                    BEGIN
                        IF EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = {userId} AND [IsActive] = 0) THROW 50422, N'Users.Inactive', 1;
                        THROW 50422, N'Users.CredentialsConcurrent', 1;
                    END;
                    """,
                    options)
                : batch.Sql(
                    $"""
                    UPDATE [core].[Users] SET [Subject] = {clientId}, [JoinedAt] = COALESCE([JoinedAt], {now}), [ModifiedAt] = SYSUTCDATETIME(), [ModifiedById] = @tm_UserId
                    WHERE [Id] = {userId} AND [IsActive] = 1 AND [Subject] = {previous};
                    IF @@ROWCOUNT <> 1
                    BEGIN
                        IF EXISTS (SELECT 1 FROM [core].[Users] WHERE [Id] = {userId} AND [IsActive] = 0) THROW 50422, N'Users.Inactive', 1;
                        THROW 50422, N'Users.CredentialsConcurrent', 1;
                    END;
                    """,
                    options);
        }

        // Spec 0018 §3.8 deletes the new client on a failed persist. One whose commit is unknown (an
        // update cannot be probed after a lost connection) is settled by reading the row back once,
        // outside any transaction: a row naming the new client committed, so the post-commit phase
        // runs and the failure is still raised, its secret delivered to nobody; any other row did
        // not, so the client is deleted. Only a failed read leaves the client, and logs it.
        /// <summary>Settles the new client after the persist failed: deleted, or, when the link committed, kept and the previous one deleted.</summary>
        /// <param name="failure">The persist's failure.</param>
        /// <param name="userId">The account's id.</param>
        /// <param name="previous">The client the account was loaded with; null for none.</param>
        /// <param name="clientId">The new client's id.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>A task that completes when the client was settled or the failure logged.</returns>
        private async Task SettleNewClientAsync(Exception failure, int userId, string? previous, string clientId, int tenantId)
        {
            bool? linked = CommitUnknown(failure) ? await LinkedAsync(userId, clientId).ConfigureAwait(false) : false;
            switch (linked)
            {
                case true:
                    await AfterLinkedAsync(userId, previous, clientId, tenantId).ConfigureAwait(false);
                    break;
                case false:
                    await DeleteServiceAccountQuietlyAsync(clientId, userId).ConfigureAwait(false);
                    break;
                default:
                    break;
            }
        }

        /// <summary>Reads back whether the account names a client, in one round trip of its own; a failed read is logged.</summary>
        /// <param name="userId">The account's id.</param>
        /// <param name="clientId">The client's id; never logged.</param>
        /// <returns>Whether the account names the client; null when the read failed.</returns>
        private async Task<bool?> LinkedAsync(int userId, string clientId)
        {
            try
            {
                // The request may be gone by now; the read runs regardless.
                BatchResult<RawResult>? found = null;
                return await _dependencies.Runner.RunAsync(
                    BatchPurpose.Read,
                    (_, batch) =>
                    {
                        found = batch.Sql($"SELECT [Id] FROM [core].[Users] WHERE [Id] = {userId} AND [Subject] = {clientId};", new SqlOptions(resultSets: 1));
                        return ValueTask.CompletedTask;
                    },
                    (_, _) => found!.Value.ResultSets[0].RowCount == 1,
                    CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                UsersLog.ServiceAccountClientKept(_dependencies.Logger, failure, userId);
                return null;
            }
        }

        /// <summary>The post-commit phase of a link: the new client recorded, then the previous one deleted.</summary>
        /// <param name="userId">The account's id.</param>
        /// <param name="previous">The client the account was loaded with; null for none.</param>
        /// <param name="clientId">The new client's id.</param>
        /// <param name="tenantId">The tenant.</param>
        /// <returns>A task that completes when both ran or their failures were logged.</returns>
        private async Task AfterLinkedAsync(int userId, string? previous, string clientId, int tenantId)
        {
            await RecordMembershipsAsync([new TenantMembershipRecord { TenantId = tenantId, Subject = clientId, IsActive = true }]).ConfigureAwait(false);
            if (previous is not null && !string.Equals(previous, clientId, StringComparison.Ordinal))
            {
                await DeleteServiceAccountQuietlyAsync(previous, userId).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Whether a persist failure leaves its commit unknown: a round trip that may have committed
        ///     with nothing to tell, anywhere in the failure's chain of causes.
        /// </summary>
        /// <param name="failure">The persist's failure.</param>
        /// <returns>True when the persist may have committed.</returns>
        internal static bool CommitUnknown(Exception failure)
        {
            ArgumentNullException.ThrowIfNull(failure);
            for (Exception? cause = failure; cause is not null; cause = cause.InnerException)
            {
                if (cause is DataAccessAmbiguousException { Verdict: not CommitVerdict.NotCommitted })
                {
                    return true;
                }
            }

            return false;
        }
    }
}
