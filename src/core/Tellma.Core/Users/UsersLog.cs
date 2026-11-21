// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Logging;

namespace Tellma.Core.Users
{
    // The events are spec 0018 §8.2's users rows, plus the failure events of §3.6's best-effort
    // post-commit effects, which the spec says are logged and never thrown.
    /// <summary>Source-generated log messages of the users' stack.</summary>
    /// <remarks>
    ///     No event carries an email address, a subject, a client id, a client secret or a token: only
    ///     user ids, counts and the failing component's name.
    /// </remarks>
    internal static partial class UsersLog
    {
        /// <summary>The log category of the users' stack.</summary>
        public const string Category = "Tellma.Core.Users";

        /// <summary>An invite committed: how many users it was asked for, and how they ended.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="count">The users the invite was asked for.</param>
        /// <param name="invited">The users the identity server created and sent an invitation to.</param>
        /// <param name="reinvited">The credential-less accounts sent an invitation again.</param>
        /// <param name="active">The accounts that already held a credential.</param>
        /// <param name="errors">The users refused, by the identity server or for a subject mismatch.</param>
        [LoggerMessage(
            EventName = "UsersInvited",
            Level = LogLevel.Information,
            Message = "Invited {Count} user(s): {Invited} invited, {Reinvited} reinvited, {Active} already active, {Errors} refused.")]
        public static partial void UsersInvited(ILogger logger, int count, int invited, int reinvited, int active, int errors);

        /// <summary>The identity server stopped answering an invite part way: what it answered is recorded, the rest is not attempted.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="processed">The users the identity server answered.</param>
        /// <param name="remaining">The users never attempted.</param>
        [LoggerMessage(
            EventName = "UsersInvitePartial",
            Level = LogLevel.Warning,
            Message = "An invite stopped after {Processed} user(s); {Remaining} user(s) were not attempted.")]
        public static partial void UsersInvitePartial(ILogger logger, int processed, int remaining);

        /// <summary>
        ///     An invited user receives no welcome email: they have no email address, or none the
        ///     outbox can store. The invite itself stands.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The user.</param>
        [LoggerMessage(
            EventName = "WelcomeEmailSkipped",
            Level = LogLevel.Warning,
            Message = "User {UserId} was invited but receives no welcome email: their email address cannot be sent to.")]
        public static partial void WelcomeEmailSkipped(ILogger logger, int userId);

        /// <summary>A service account's previous or orphaned client could not be deleted at the identity server.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">Why the delete failed.</param>
        /// <param name="userId">The service account's user id.</param>
        [LoggerMessage(
            EventName = "ServiceAccountDeleteFailed",
            Level = LogLevel.Warning,
            Message = "The identity client of service account {UserId} could not be deleted.")]
        public static partial void ServiceAccountDeleteFailed(ILogger logger, Exception exception, int userId);

        /// <summary>
        ///     A service account's new client was kept although the persist that links it failed,
        ///     because the database could not tell whether that persist committed and reading the
        ///     account back failed too: deleting it could leave the account linked to a client that no
        ///     longer exists.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The read-back's failure.</param>
        /// <param name="userId">The service account's user id.</param>
        [LoggerMessage(
            EventName = "ServiceAccountClientKept",
            Level = LogLevel.Warning,
            Message = "The new identity client of service account {UserId} was kept: the persist's commit is unknown and reading the account back failed. Nobody holds the client's secret; issue the account's credentials again.")]
        public static partial void ServiceAccountClientKept(ILogger logger, Exception exception, int userId);

        /// <summary>A deactivated user's access to the tenant was revoked: the session listeners were told.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="userId">The user's id.</param>
        [LoggerMessage(
            EventName = "UserAccessRevoked",
            Level = LogLevel.Information,
            Message = "The access of user {UserId} to the tenant was revoked.")]
        public static partial void UserAccessRevoked(ILogger logger, int userId);

        /// <summary>A session-termination listener failed while a deactivated user's access was revoked.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">Why it failed.</param>
        /// <param name="listener">The listener's type name.</param>
        /// <param name="userId">The user's id.</param>
        [LoggerMessage(
            EventName = "UserAccessRevocationFailed",
            Level = LogLevel.Warning,
            Message = "The session listener {Listener} failed to revoke the access of user {UserId}.")]
        public static partial void UserAccessRevocationFailed(ILogger logger, Exception exception, string listener, int userId);

        /// <summary>The membership hints of users who gained, lost or regained access could not be recorded; the reconciliation repairs them.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">Why the record failed.</param>
        /// <param name="count">The hints that were to be recorded.</param>
        [LoggerMessage(
            EventName = "UserMembershipHintFailed",
            Level = LogLevel.Warning,
            Message = "{Count} tenant membership hint(s) could not be recorded; the reconciliation repairs them.")]
        public static partial void MembershipHintFailed(ILogger logger, Exception exception, int count);
    }
}
