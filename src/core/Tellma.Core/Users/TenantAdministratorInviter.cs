// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Provisioning;

namespace Tellma.Core.Users
{
    // Spec 0018 §6.1's deployed half: after the provisioning steps, the migrator invites the first
    // administrator through the users' stack's own invite action, in the tenant's system scope, so
    // §3.3's flow (the identity call between two round trips, the write-back, the core.user.added
    // notice, the welcome email and the membership hint) runs unchanged. Development binds a subject
    // instead and invites nobody.
    /// <summary>
    ///     Invites a tenant's first administrator after its provisioning steps, through the
    ///     <c>invite</c> action of the users' stack, as the platform: the identity server creates or
    ///     finds the administrator's account, emails an invitation when the account holds no
    ///     credential, and the tenant records the account, the outcome, the notice and the welcome
    ///     email. One per scope.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Nobody is invited when the run names a subject, which the administrator bootstrap
    ///         bound in Development, or when the administrator has joined the tenant already. Nor
    ///         when the host holds no service-client secret, which only an in-process Development
    ///         host may lack: such a migrator cannot authenticate to any identity server, so it logs
    ///         that the administrator was not invited and leaves the invitation to a later run that
    ///         holds the secret, or to the users' page.
    ///     </para>
    ///     <para>
    ///         The invitation runs in a provisioning frame of its own, as a step does, so a read-only
    ///         tenant admits it as it admits the bootstrap before it.
    ///     </para>
    ///     <para>
    ///         An administrator the identity server refuses, a sandbox's administrator without a
    ///         usable account among them, fails the invitation with the refusal's code. The steps
    ///         stay recorded, and a run that names the administrator again invites again.
    ///     </para>
    /// </remarks>
    /// <typeparam name="TUser">The distribution's user leaf.</typeparam>
    /// <param name="users">The users' stack.</param>
    /// <param name="database">The tenant's database, which the email address is looked up in.</param>
    /// <param name="accessor">The scope's request context, which must be the platform's own.</param>
    /// <param name="identity">The identity-server client's options, whose service-client secret tells whether the host can invite.</param>
    /// <param name="frames">Opens the provisioning frame the invitation runs in.</param>
    /// <param name="loggers">Creates the users' logger.</param>
    internal sealed class TenantAdministratorInviter<TUser>(
        EntityService<TUser, int> users,
        ITenantDatabase database,
        IRequestContextAccessor accessor,
        IOptions<IdentityServerClientOptions> identity,
        IProvisioningFrameOpener frames,
        ILoggerFactory loggers) : ITenantAdministratorInviter
        where TUser : User
    {
        /// <summary>The name of the invitation's provisioning frame, beside the steps' names.</summary>
        public const string FrameName = "core.invite-administrator";

        private readonly ILogger _logger = loggers.CreateLogger(UsersLog.Category);

        /// <inheritdoc />
        public async Task InviteAsync(TenantProvisioningContext context, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(context);
            if (!accessor.Current.IsSystem)
            {
                throw new InvalidOperationException("The first administrator is invited in the platform's own scope of the tenant, which the migrator opens; this scope belongs to a user.");
            }

            // --- Whether this run invites at all ---
            InvitationPlan plan = Plan(context.AdminEmail, context.AdminSubject, identity.Value.ServiceClientSecret);
            if (plan == InvitationPlan.None)
            {
                return;
            }

            // --- The administrator the bootstrap made or found, by the address as the stack stores it ---
            int tenantId = context.Tenant.Id;
            string email = context.AdminEmail!.Trim().ToLowerInvariant();
            int userId = await FindAsync(email, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Tenant {tenantId} holds no user with the first administrator's email address, which the administrator bootstrap makes before the invitation."));

            if (plan == InvitationPlan.NoSecret)
            {
                UsersLog.TenantAdministratorNotInvited(_logger, tenantId, userId);
                return;
            }

            // --- An administrator who joined already needs no invitation, and the action would refuse one ---
            EntitiesResult<TUser> loaded = await users.GetByIdAsync(userId, DetailsRequest.None, cancellationToken).ConfigureAwait(false);
            if (loaded.Entities.Single().State == UserState.Joined)
            {
                UsersLog.TenantAdministratorAlreadyJoined(_logger, tenantId, userId);
                return;
            }

            // --- The invite action, as the platform, which no securable restrains, in a provisioning frame ---
            IReadOnlyList<InviteResult> results = [];
            await frames.RunAsync(context.Services, FrameName, GetType(), async () =>
            {
                results = await users
                    .ExecuteActionAsync<IReadOnlyList<InviteResult>>(UserService<TUser>.InviteAction, [userId], arguments: null, new ActionOptions(), cancellationToken)
                    .ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);

            // A refusal, the sandbox's among them, fails the provisioning with its code; the steps stay recorded.
            InviteResult result = results.Single();
            if (result.Status is not InviteStatus status)
            {
                throw new InvalidOperationException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The first administrator of tenant {tenantId}, user {userId}, was not invited: {result.Error ?? UserService<TUser>.NoAnswerError}"));
            }

            UsersLog.TenantAdministratorInvited(_logger, tenantId, userId, status);
        }

        /// <summary>
        ///     Whether a run invites its administrator: not without an email address, not when it
        ///     names a subject, which the bootstrap bound; and, when the host holds no service-client
        ///     secret, only far enough to log that it could not.
        /// </summary>
        /// <param name="email">The administrator's email address the run names, or null.</param>
        /// <param name="subject">The administrator's subject the run names, or null.</param>
        /// <param name="serviceClientSecret">The host's service-client secret, or null when it holds none.</param>
        /// <returns>The plan.</returns>
        internal static InvitationPlan Plan(string? email, string? subject, string? serviceClientSecret)
        {
            return string.IsNullOrWhiteSpace(email) || subject is not null ? InvitationPlan.None
                : string.IsNullOrWhiteSpace(serviceClientSecret) ? InvitationPlan.NoSecret
                : InvitationPlan.Invite;
        }

        /// <summary>The id of the user of an email address, if any.</summary>
        /// <param name="email">The normalised email address.</param>
        /// <param name="cancellationToken">Cancels the round trip.</param>
        /// <returns>The user's id; null when no user has the address.</returns>
        private async Task<int?> FindAsync(string email, CancellationToken cancellationToken)
        {
            IDataBatch read = database.CreateBatch(BatchPurpose.Read);
            BatchResult<RawResult> found = read.Sql($"SELECT U.[Id] FROM [core].[Users] AS U WHERE U.[Email] = {email};", new SqlOptions(resultSets: 1));
            _ = await read.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            QueryRowSet rows = found.Value.ResultSets[0];
            return rows.RowCount == 0 ? null : ((int[])rows.GetBuffer(0))[0];
        }
    }

    /// <summary>What a provisioning run does about its administrator's invitation.</summary>
    internal enum InvitationPlan
    {
        /// <summary>Nothing: the run names no administrator, or names a subject the bootstrap bound.</summary>
        None,

        /// <summary>Logs that the administrator was not invited: the host holds no service-client secret.</summary>
        NoSecret,

        /// <summary>Invites the administrator.</summary>
        Invite,
    }
}
