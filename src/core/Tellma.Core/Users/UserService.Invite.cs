// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.TableTypes;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Data.Batches;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.3: three phases across the action's two round trips, with no transaction open
    // during the remote call. Round trip 1 loads, counts and validates the targets; the method then
    // waits for every validator's verdict (ActionContext.ValidatedAsync), calls the identity server
    // once per chunk, composes the write-back onto the deferred persist batch, and reads the
    // statement's mismatch set once the action committed. A recompose of round trip 2 replays the
    // composed write-back and never invokes the method, so the identity server is called once.
    // The core.user.added notice and the welcome email of §3.3 step 3 join with spec 0021's and
    // spec 0022's contributions.
    public partial class UserService<TUser>
    {
        /// <summary>The identity server's per-user error for an unknown address on a lookup that may not create users.</summary>
        internal const string UnknownEmailError = "unknown_email";

        /// <summary>The identity server's per-user error for a credential-less account on a lookup that may not invite.</summary>
        internal const string NoCredentialError = "existing_user_has_no_credential";

        /// <summary>The most characters a user's last invitation error keeps.</summary>
        internal const int MaxInviteErrorLength = 1024;

        /// <summary>The problem code of an invite the identity server answered only part of.</summary>
        internal const string PartialFailureCode = "partial-failure";

        /// <summary>The error recorded when the identity server answered neither an account nor a refusal.</summary>
        internal const string NoAnswerError = "The identity server answered neither an account nor a refusal.";

        /// <summary>
        ///     Invites the target users through the identity server: each is created there or found by
        ///     email, sent an invitation link when it needs one, and linked to the account it resolved
        ///     to. One invalid target refuses the whole action before anyone is invited; a user the
        ///     identity server refuses is a result of its own.
        /// </summary>
        /// <param name="context">The action's context.</param>
        /// <returns>One result per target, in request order.</returns>
        /// <exception cref="PartialFailureException">
        ///     The identity server stopped answering part way: what it answered was recorded, and the
        ///     exception carries those results and the ids never attempted.
        /// </exception>
        [EntityAction(InviteAction, Description = "Invite users through the identity server")]
        public async Task<IReadOnlyList<InviteResult>> InviteAsync(ActionContext<TUser, int> context)
        {
            ArgumentNullException.ThrowIfNull(context);

            // --- Phase 1 (round trip 1): the targets, then every validator's verdict ---
            await context.Loader.LoadAsync().ConfigureAwait(false);
            await context.ValidatedAsync().ConfigureAwait(false);
            if (context.Errors.HasErrors)
            {
                await FailValidatedAsync(context).ConfigureAwait(false);
            }

            RequestContext request = context.Context;
            int tenantId = request.Tenant?.Id
                ?? throw new InvalidOperationException("An invite runs in a tenant, and the scope's context names none.");
            TenantSettings settings = request.TenantSettings
                ?? throw new InvalidOperationException("The scope's context carries no tenant settings, so the invitations have no default language.");
            List<TUser> targets = InRequestOrder(context);
            bool existingOnly = request.IsSandbox;
            string returnUrl = _dependencies.Links.Tenant(tenantId);
            IdentityInvitation[] invitations = [.. targets.Select(user => Invitation(user, settings, returnUrl, existingOnly))];

            // --- Phase 2: the remote call, with no batch in flight; a failure before any chunk
            // completed raises with nothing written ---
            IReadOnlyList<IdentityInvitationResult> answered = await _dependencies.Identity
                .InviteAsync(invitations, context.CancellationToken).ConfigureAwait(false);
            if (answered.Count > invitations.Length)
            {
                throw new InvalidOperationException(
                    $"The identity server answered {answered.Count} invitation(s) where {invitations.Length} were sent.");
            }

            // --- Phase 3 (round trip 2): the write-back of what was answered, its tag bumps riding the epilogue ---
            UserInvitationOutcome[] outcomes = [.. answered.Select((result, index) => Outcome(targets[index].Id, result, existingOnly))];
            BatchResult<RawResult>? written = outcomes.Length > 0 ? ComposeWriteBack(context.Batch, outcomes) : null;

            // --- Phase 4: the commit, then the statement's mismatches ---
            await context.Persisted.ConfigureAwait(false);
            HashSet<int> mismatched = written is null ? [] : Mismatched(written.Value);

            // --- The membership hints of the users who gained a subject (post-commit, best effort) ---
            List<TenantMembershipRecord> gained = [];
            for (int index = 0; index < outcomes.Length; index++)
            {
                TUser user = targets[index];
                if (user.Subject is null && outcomes[index].Subject is string subject && !mismatched.Contains(user.Id))
                {
                    gained.Add(new TenantMembershipRecord { TenantId = tenantId, Subject = subject, IsActive = user.IsActive });
                }
            }

            await RecordMembershipsAsync(gained).ConfigureAwait(false);

            // --- The results in request order, counted by outcome ---
            List<InviteResult> results = [.. outcomes.Select((outcome, index) => Result(outcome, answered[index], mismatched))];
            var tally = InviteTally.Of(results);
            CountInvites(tally, targets.Count - results.Count);

            // --- A prefix: what was answered is recorded; the rest is the admin's to re-invite ---
            if (results.Count < targets.Count)
            {
                UsersLog.UsersInvitePartial(_dependencies.Logger, results.Count, targets.Count - results.Count);
                List<string> notAttempted = [.. targets.Skip(results.Count).Select(static user => user.Id.ToString(CultureInfo.InvariantCulture))];
                throw new PartialFailureException(PartialFailureCode, results, notAttempted);
            }

            UsersLog.UsersInvited(_dependencies.Logger, results.Count, tally.Invited, tally.Reinvited, tally.Active, tally.Errors + tally.Mismatched);
            return results;
        }

        /// <summary>
        ///     The invitation of one user: the email; the locale, the user's language or else the
        ///     tenant's first; the name in that locale's content language; the gender; the tenant's link;
        ///     and whether the identity server may only look the user up.
        /// </summary>
        /// <param name="user">The user.</param>
        /// <param name="settings">The tenant's settings.</param>
        /// <param name="returnUrl">The tenant's link, where an accepted invitation lands.</param>
        /// <param name="existingOnly">True on a sandbox tenant: nobody is created and nobody is emailed.</param>
        /// <returns>The invitation.</returns>
        /// <exception cref="InvalidOperationException">The user has no email.</exception>
        internal static IdentityInvitation Invitation(User user, TenantSettings settings, string returnUrl, bool existingOnly)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(settings);
            string email = user.Email
                ?? throw new InvalidOperationException("A user is invited by email, and the target has none.");
            string locale = user.PreferredLanguage ?? settings.Languages[0].Code;
            string displayName = UserRules.NameIn(user, UserRules.Slot(settings.Languages, locale));
            return new IdentityInvitation(email, displayName, locale, user.Gender, returnUrl, existingOnly);
        }

        /// <summary>
        ///     The write-back row of one identity answer: the account and the status of a success; the
        ///     refusal of anything else, verbatim and capped, except a sandbox lookup's two refusals,
        ///     which become the sandbox's own code. A refusal writes no account, even one the identity
        ///     server created before the step that failed: a later invite links it.
        /// </summary>
        /// <param name="userId">The user's id.</param>
        /// <param name="result">The identity server's answer.</param>
        /// <param name="existingOnly">Whether the invitation was a sandbox lookup.</param>
        /// <returns>The row.</returns>
        internal static UserInvitationOutcome Outcome(int userId, IdentityInvitationResult result, bool existingOnly)
        {
            ArgumentNullException.ThrowIfNull(result);
            if (result.Status is InviteStatus status && !string.IsNullOrEmpty(result.Subject))
            {
                return new UserInvitationOutcome { UserId = userId, Subject = result.Subject, InviteStatus = status.ToString() };
            }

            string error = result.Error ?? NoAnswerError;
            if (existingOnly && (string.Equals(error, UnknownEmailError, StringComparison.Ordinal) || string.Equals(error, NoCredentialError, StringComparison.Ordinal)))
            {
                error = AccessCodes.SandboxRequiresExistingIdentity;
            }

            return new UserInvitationOutcome
            {
                UserId = userId,
                LastInviteError = error.Length > MaxInviteErrorLength ? error[..MaxInviteErrorLength] : error,
            };
        }

        /// <summary>One user's result: a mismatch the statement found, else the identity server's answer.</summary>
        /// <param name="outcome">The user's write-back row.</param>
        /// <param name="answer">The identity server's answer.</param>
        /// <param name="mismatched">The users whose stored subject differs from the one answered.</param>
        /// <returns>The result.</returns>
        internal static InviteResult Result(UserInvitationOutcome outcome, IdentityInvitationResult answer, IReadOnlySet<int> mismatched)
        {
            ArgumentNullException.ThrowIfNull(outcome);
            ArgumentNullException.ThrowIfNull(answer);
            ArgumentNullException.ThrowIfNull(mismatched);
            return mismatched.Contains(outcome.UserId) ? new InviteResult(outcome.UserId, null, AccessCodes.SubjectMismatch)
                : outcome.LastInviteError is string error ? new InviteResult(outcome.UserId, null, error)
                : new InviteResult(outcome.UserId, answer.Status, null);
        }

        /// <summary>
        ///     Appends the write-back statement: each answered user's account linked unless it holds
        ///     another, its invitation stamped on success, its status kept when refused, its last error
        ///     replaced, its row stamped; then the users whose stored account differs from the one
        ///     answered. Every answered user's preferences tag is bumped in the epilogue.
        /// </summary>
        /// <param name="batch">The action's batch.</param>
        /// <param name="outcomes">The answered users' rows.</param>
        /// <returns>The statement's result: one set, the mismatched users' ids.</returns>
        internal static BatchResult<RawResult> ComposeWriteBack(IDataBatch batch, IReadOnlyList<UserInvitationOutcome> outcomes)
        {
            ArgumentNullException.ThrowIfNull(batch);
            ArgumentNullException.ThrowIfNull(outcomes);
            SqlIdentifier rows = batch.Tvp(outcomes);
            SqlIdentifier ids = batch.Tvp([.. outcomes.Select(static outcome => new IdList { Id = outcome.UserId })]);

            // The statement's clock is one variable of its own, so the invitation and the row's stamp
            // agree; an action composes one write-back, so its batch declares the variable once.
            return batch.Sql(
                $"""
                DECLARE @inviteNow datetimeoffset(7) = SYSUTCDATETIME();
                UPDATE u SET
                    u.[Subject]         = COALESCE(u.[Subject], o.[Subject]),
                    u.[InvitedAt]       = CASE WHEN o.[Subject] IS NULL THEN u.[InvitedAt] ELSE @inviteNow END,
                    u.[InviteStatus]    = COALESCE(o.[InviteStatus], u.[InviteStatus]),
                    u.[LastInviteError] = o.[LastInviteError],
                    u.[ModifiedAt]      = @inviteNow,
                    u.[ModifiedById]    = @tm_UserId
                FROM [core].[Users] AS u
                JOIN {rows} AS o ON o.[UserId] = u.[Id]
                WHERE u.[Subject] IS NULL OR o.[Subject] IS NULL OR u.[Subject] = o.[Subject];
                SELECT o.[UserId] FROM {rows} AS o JOIN [core].[Users] AS u ON u.[Id] = o.[UserId]
                WHERE o.[Subject] IS NOT NULL AND u.[Subject] <> o.[Subject];
                """,
                new SqlOptions(writes: new HashSet<TableName> { PersistChecks.Users }, resultSets: 1, userIds: ids));
        }

        /// <summary>The users the write-back left untouched because their stored account differs from the one answered.</summary>
        /// <param name="result">The statement's result.</param>
        /// <returns>The users' ids.</returns>
        private static HashSet<int> Mismatched(RawResult result)
        {
            QueryRowSet set = result.ResultSets[0];
            return set.RowCount == 0 ? [] : [.. ((int[])set.GetBuffer(0)).Take(set.RowCount)];
        }

        /// <summary>The loaded targets in request order, each once.</summary>
        /// <param name="context">The action's context.</param>
        /// <returns>The targets.</returns>
        private static List<TUser> InRequestOrder(ActionContext<TUser, int> context)
        {
            var byId = context.Entities.ToDictionary(static user => user.Id);
            return [.. context.Ids.Distinct().Where(byId.ContainsKey).Select(id => byId[id])];
        }

        /// <summary>Counts the invite's users by outcome.</summary>
        /// <param name="tally">The answered users' outcomes.</param>
        /// <param name="notAttempted">How many users were never attempted.</param>
        private void CountInvites(InviteTally tally, int notAttempted)
        {
            UsersMetrics metrics = _dependencies.Metrics;
            metrics.RecordInvites(UsersTelemetryNames.InviteInvited, tally.Invited);
            metrics.RecordInvites(UsersTelemetryNames.InviteReinvited, tally.Reinvited);
            metrics.RecordInvites(UsersTelemetryNames.InviteActive, tally.Active);
            metrics.RecordInvites(UsersTelemetryNames.InviteSubjectMismatch, tally.Mismatched);
            metrics.RecordInvites(UsersTelemetryNames.InviteError, tally.Errors);
            metrics.RecordInvites(UsersTelemetryNames.InviteNotAttempted, notAttempted);
        }

        /// <summary>The answered users of one invite, counted by outcome.</summary>
        /// <param name="Invited">The users created and sent an invitation.</param>
        /// <param name="Reinvited">The credential-less accounts sent an invitation again.</param>
        /// <param name="Active">The accounts that already held a credential.</param>
        /// <param name="Mismatched">The users whose email belongs to another account.</param>
        /// <param name="Errors">The users refused.</param>
        private readonly record struct InviteTally(int Invited, int Reinvited, int Active, int Mismatched, int Errors)
        {
            /// <summary>Counts results by outcome.</summary>
            /// <param name="results">The results.</param>
            /// <returns>The tally.</returns>
            public static InviteTally Of(IReadOnlyList<InviteResult> results)
            {
                InviteTally tally = default;
                foreach (InviteResult result in results)
                {
                    tally = result switch
                    {
                        { Status: InviteStatus.Invited } => tally with { Invited = tally.Invited + 1 },
                        { Status: InviteStatus.Reinvited } => tally with { Reinvited = tally.Reinvited + 1 },
                        { Status: InviteStatus.Active } => tally with { Active = tally.Active + 1 },
                        { Error: AccessCodes.SubjectMismatch } => tally with { Mismatched = tally.Mismatched + 1 },
                        _ => tally with { Errors = tally.Errors + 1 },
                    };
                }

                return tally;
            }
        }
    }
}
