// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Tellma.Core.Abstractions.Email;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Services.Audit;
using Tellma.Identity.Services.Email;
using Tellma.Identity.Services.Tokens;

namespace Tellma.Identity.Services.Invitations
{
    /// <summary>One user to invite.</summary>
    /// <param name="Email">The user's email.</param>
    /// <param name="DisplayName">The user's display name.</param>
    /// <param name="Locale">The user's preferred language.</param>
    /// <param name="Gender">How to address the user grammatically, when stated.</param>
    /// <param name="ReturnUrl">Where the accepted invitation returns the user.</param>
    /// <param name="ExistingOnly">
    ///     Whether the item is a lookup by email that never creates a user, never issues a link and
    ///     never sends an email (see <see cref="ExistingOnlyResolution" />).
    /// </param>
    public sealed record InvitationRequestItem(
        string Email,
        string? DisplayName,
        string? Locale,
        string? ReturnUrl,
        UserGender? Gender = null,
        bool ExistingOnly = false);

    /// <summary>
    ///     The stable per-user error codes of an <see cref="InvitationRequestItem.ExistingOnly" />
    ///     item. They are codes rather than sentences because callers branch on them: a sandbox
    ///     tenant maps both to one validation error of its own.
    /// </summary>
    public static class InvitationErrors
    {
        /// <summary>The address belongs to no user.</summary>
        public const string UnknownEmail = "unknown_email";

        /// <summary>The address belongs to a user who holds no credential yet.</summary>
        public const string ExistingUserHasNoCredential = "existing_user_has_no_credential";
    }

    /// <summary>What an <see cref="InvitationRequestItem.ExistingOnly" /> lookup answers.</summary>
    public enum ExistingOnlyOutcome
    {
        /// <summary>The user holds a credential: <c>Active</c> with their subject.</summary>
        Active = 0,

        /// <summary>No user has the address: <see cref="InvitationErrors.UnknownEmail" />.</summary>
        UnknownEmail = 1,

        /// <summary>
        ///     The user holds no credential: <see cref="InvitationErrors.ExistingUserHasNoCredential" />,
        ///     never <c>Reinvited</c>, because finishing their onboarding needs an email.
        /// </summary>
        NoCredential = 2,

        /// <summary>The user is disabled or purged: the refusal an ordinary invite gives.</summary>
        Refused = 3,
    }

    /// <summary>
    ///     The outcome table of an <see cref="InvitationRequestItem.ExistingOnly" /> lookup, kept
    ///     apart from the store so that the table itself is what the tests pin.
    /// </summary>
    public static class ExistingOnlyResolution
    {
        /// <summary>Decides what a lookup answers for the user an address belongs to.</summary>
        /// <param name="state">The user's lifecycle state; null when no user has the address.</param>
        /// <param name="hasCredential">Whether the user holds a passkey, a password or an external login.</param>
        /// <returns>The outcome.</returns>
        /// <remarks>
        ///     An orphaned user who holds a credential answers <see cref="ExistingOnlyOutcome.Active" />:
        ///     the caller is recording a membership, which is what ends the orphaned state, and the
        ///     service restores it as an ordinary invite does, without a link or an email. A
        ///     disabled or purged user is refused before the credential question is asked, and the
        ///     refusal does not tell the two administrative states apart.
        /// </remarks>
        public static ExistingOnlyOutcome Resolve(UserLifecycleState? state, bool hasCredential)
        {
            return state switch
            {
                null => ExistingOnlyOutcome.UnknownEmail,
                UserLifecycleState.Disabled or UserLifecycleState.Purged => ExistingOnlyOutcome.Refused,
                UserLifecycleState.Active or UserLifecycleState.Orphaned => hasCredential
                    ? ExistingOnlyOutcome.Active
                    : ExistingOnlyOutcome.NoCredential,

                // A state this table does not know is never answered Active by default.
                _ => throw new ArgumentOutOfRangeException(nameof(state), state, "Unknown lifecycle state."),
            };
        }
    }

    /// <summary>The per-user outcome of a bulk invitation.</summary>
    public enum InvitationStatus
    {
        /// <summary>A new user was created and invited.</summary>
        Invited = 0,

        /// <summary>An existing (credential-less or orphaned) user was re-invited.</summary>
        Reinvited = 1,

        /// <summary>An already-active user needs no invitation; membership is recorded by the caller.</summary>
        Active = 2,
    }

    /// <summary>
    ///     One user's invitation result: either a <paramref name="Status" /> with the user's
    ///     <paramref name="Subject" />, or an <paramref name="Error" /> — never both. The
    ///     invitation link is never returned.
    /// </summary>
    /// <param name="Email">The invited email.</param>
    /// <param name="Subject">The user's stable subject identifier; null when the user was refused.</param>
    /// <param name="Status">The per-user outcome; null when the user was refused.</param>
    /// <param name="Error">Why the user was refused; null on success.</param>
    public sealed record InvitationResultItem(string Email, string? Subject, InvitationStatus? Status, string? Error);

    /// <summary>
    ///     Bulk user invitation: create-or-get each user by email, assign each a <c>sub</c>, send a
    ///     localized single-use link, and return per-user status plus <c>sub</c>. A user that must
    ///     be refused (administratively disabled, invalid email) yields a per-user error and never
    ///     aborts the batch — each write commits individually, so the rest of the batch proceeds
    ///     and its emails still go out. The link — the email-ownership proof — is never returned in
    ///     any environment. Users are created through the Identity user manager (one persistence
    ///     operation each, so its validators and normalizers run), while the email delivery for the
    ///     whole batch is a single hand-off to the background dispatcher. An
    ///     <see cref="InvitationRequestItem.ExistingOnly" /> item is a lookup instead: it never
    ///     creates a user, issues a link or queues an email, whatever the rest of the batch does.
    /// </summary>
    /// <param name="context">The identity store, shared across the batch.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tokens">One-time invitation tokens.</param>
    /// <param name="emailDispatcher">The background mail dispatch queue.</param>
    /// <param name="templates">Localized message construction.</param>
    /// <param name="links">Builds the absolute invitation link.</param>
    /// <param name="returnUrls">Decides where an accepted invitation may send the user.</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="timeProvider">The clock.</param>
    /// <param name="logger">Per-user failure diagnostics.</param>
    public sealed class InvitationService(
        TellmaIdentityDbContext context,
        UserManager<TellmaIdentityUser> userManager,
        IOneTimeTokenService tokens,
        IEmailDispatcher emailDispatcher,
        EmailTemplateService templates,
        InvitationLinkBuilder links,
        InvitationReturnUrlValidator returnUrls,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        TimeProvider timeProvider,
        ILogger<InvitationService> logger)
    {
        /// <summary>The invitation link lifetime.</summary>
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

        /// <summary>
        ///     The metric status recorded for a user the batch did not invite, whether it was
        ///     refused outright or lost to a failure. One label rather than two: both mean "no
        ///     invitation", so splitting them would make the obvious dashboard — successes over
        ///     the total — undercount whichever one its query forgot.
        /// </summary>
        private const string RefusedMetricStatus = "Refused";

        /// <summary>
        ///     The refusal for a disabled or purged user. It does not disclose which of the two
        ///     administrative states the account is in.
        /// </summary>
        private const string AdministrativelyRefused =
            "The user cannot be invited; an operator must re-enable the account first.";

        /// <summary>Invites a batch of users, returning per-user status and subject.</summary>
        /// <param name="items">The users to invite.</param>
        /// <param name="createdByClientId">The calling client, for audit and token attribution.</param>
        /// <param name="cancellationToken">Aborts the operation.</param>
        /// <returns>The per-user results, in request order.</returns>
        public async Task<IReadOnlyList<InvitationResultItem>> InviteAsync(
            IReadOnlyList<InvitationRequestItem> items,
            string? createdByClientId,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(items);

            List<InvitationResultItem> results = [];
            List<EmailMessage> emails = [];

            try
            {
                foreach (InvitationRequestItem item in items)
                {
                    // Where this user's rows start, so a failure discards exactly what it added —
                    // matching by email would delete a namesake's delivered result when the same
                    // address appears twice in one batch. The queued mail is discarded with it:
                    // a user reported as an error must not receive an invitation anyway. What is
                    // already committed stays committed: a user created before the failure keeps
                    // its row (see the note on RecordFailure).
                    int resultsBefore = results.Count;
                    int emailsBefore = emails.Count;
                    HashSet<object> trackedBefore = TrackedEntities();
                    try
                    {
                        await InviteOneAsync(item, createdByClientId, results, emails, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        // The caller is gone (timeout or disconnect). Stop taking on new work, but
                        // fall through to the send: users created so far already hold live
                        // invitation tokens, and a link that is never delivered strands them.
                        RecordFailure(item, results, resultsBefore, emails, emailsBefore, trackedBefore);
                        await AuditFailureAsync(createdByClientId);
                        break;
                    }
                    catch (Exception exception)
                    {
                        // One user's failure — a duplicate-email race surfacing as a store
                        // exception, a token-issuance fault — is that user's result, never the
                        // batch's. The rest proceed and their links still go out.
                        RecordFailure(item, results, resultsBefore, emails, emailsBefore, trackedBefore);
                        InvitationLog.UserFailed(logger, exception, item.Email);
                        await AuditFailureAsync(createdByClientId);
                    }
                }
            }
            finally
            {
                // One batched hand-off for the whole invitation, queued rather than sent inline:
                // whatever was persisted is delivered by the background worker (which drains on
                // graceful shutdown), so a slow or unreachable SMTP host cannot pin a request
                // whose caller may already be gone, and the hand-off cannot throw from a finally.
                if (emails.Count > 0)
                {
                    emailDispatcher.Enqueue(emails);
                }
            }

            return results;
        }

        /// <summary>
        ///     Replaces whatever this user added with a failure the caller can act on. This
        ///     unwinds only in-memory state and pending tracked changes; a write this user already
        ///     committed — the user row, when creation succeeded and a later step did not — stays.
        ///     The account is left credential-less and Active, so re-inviting the same address
        ///     resolves it as <c>Reinvited</c> and completes the invitation.
        /// </summary>
        /// <param name="item">The user that failed.</param>
        /// <param name="results">The batch's results so far.</param>
        /// <param name="resultsBefore">The result count before this user was processed.</param>
        /// <param name="emails">The batch's queued mail so far.</param>
        /// <param name="emailsBefore">The queued-mail count before this user was processed.</param>
        /// <param name="trackedBefore">The entities tracked before this user was processed.</param>
        private void RecordFailure(
            InvitationRequestItem item,
            List<InvitationResultItem> results,
            int resultsBefore,
            List<EmailMessage> emails,
            int emailsBefore,
            HashSet<object> trackedBefore)
        {
            // The store is shared across the batch, so a failed save leaves the attempted change
            // tracked; the next user's save would flush it again and fail for a reason that is not
            // theirs — or silently commit a change this user was told had been refused. Detach
            // only what this user tracked: the context is the request's, so clearing it outright
            // would discard pending changes staged by anything else in the same scope.
            DetachChangesSince(trackedBefore);

            results.RemoveRange(resultsBefore, results.Count - resultsBefore);
            emails.RemoveRange(emailsBefore, emails.Count - emailsBefore);
            results.Add(new InvitationResultItem(item.Email, null, null, "The user could not be invited."));
            metrics.Invitation(RefusedMetricStatus);
        }

        /// <summary>The entities the shared store is tracking right now, by reference.</summary>
        /// <returns>The tracked entity instances.</returns>
        private HashSet<object> TrackedEntities()
        {
            // Reference identity: two distinct rows must never collide through an overridden Equals.
            return new HashSet<object>(
                context.ChangeTracker.Entries().Select(static entry => entry.Entity), ReferenceEqualityComparer.Instance);
        }

        /// <summary>Detaches every entity the store started tracking after the given snapshot.</summary>
        /// <param name="trackedBefore">The entities tracked before the work being unwound.</param>
        private void DetachChangesSince(HashSet<object> trackedBefore)
        {
            // Materialized first: detaching mutates the tracker the entries are enumerated from.
            foreach (EntityEntry entry in context.ChangeTracker.Entries().ToList())
            {
                if (!trackedBefore.Contains(entry.Entity))
                {
                    entry.State = EntityState.Detached;
                }
            }
        }

        /// <summary>Records a per-user failure in the audit trail.</summary>
        private Task AuditFailureAsync(string? createdByClientId)
        {
            return auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.UserInvited,
                    ClientId = createdByClientId,
                    Outcome = "failure",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { reason = "exception" }),
                },
                CancellationToken.None);
        }

        /// <summary>Processes one user of the batch, appending its result and any email to send.</summary>
        private async Task InviteOneAsync(
            InvitationRequestItem item,
            string? createdByClientId,
            List<InvitationResultItem> results,
            List<EmailMessage> emails,
            CancellationToken cancellationToken)
        {
            // A lookup by email takes its own path, which never reaches the token issuance or the
            // mail queue below: nothing it does can add a link or an email to the batch.
            if (item.ExistingOnly)
            {
                await ResolveExistingOnlyAsync(item, createdByClientId, results);
                return;
            }

            // Checked before the user is created, so a destination this client may not send to is
            // refused outright rather than silently dropped — which is what happened before, and
            // left an invited user finishing on the authority's own page with no way onward.
            if (!await returnUrls.IsAllowedAsync(item.ReturnUrl, createdByClientId, cancellationToken))
            {
                results.Add(new InvitationResultItem(
                    item.Email,
                    null,
                    null,
                    "The return url is not a destination this client is registered to receive users at."));
                metrics.Invitation(RefusedMetricStatus);
                return;
            }

            (TellmaIdentityUser? user, InvitationStatus status, string? error) = await CreateOrGetAsync(item);
            if (error is not null || user is null)
            {
                await RefuseAsync(item, user?.Id, error, createdByClientId, results);
                return;
            }

            // Active users need no link — nothing to prove; the distribution records membership.
            if (status != InvitationStatus.Active)
            {
                string token = await tokens.IssueAsync(
                    user.Id, SingleUseCodePurpose.Invitation, InvitationLifetime, item.ReturnUrl, createdByClientId, cancellationToken);
                string link = links.Build(token);
                emails.Add(templates.Invitation(user, link, InvitationLifetime.Days, OneTimeTokenFormat.IdOf(token)));

                // As above: the user's link is issued and queued, so recording that must not be
                // undone by the caller giving up.
                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.UserInvited,
                        Subject = user.Id,
                        ClientId = createdByClientId,
                        Outcome = "success",
                    },
                    CancellationToken.None);
            }

            // Recorded only once the user's link is queued: a result claiming Invited for a user
            // whose token issuance then failed would tell the caller to record a membership no
            // invitation can complete.
            results.Add(new InvitationResultItem(item.Email, user.Id, status, null));
            metrics.Invitation(status.ToString());
        }

        /// <summary>
        ///     Answers an <see cref="InvitationRequestItem.ExistingOnly" /> item: a lookup by email
        ///     that never creates a user, issues no link and queues no email. Its only write is the
        ///     restore of an orphaned user who holds a credential, the one an ordinary invite makes.
        /// </summary>
        /// <param name="item">The item, whose return URL is ignored because no link carries it.</param>
        /// <param name="createdByClientId">The calling client, for audit attribution.</param>
        /// <param name="results">The batch's results so far.</param>
        private async Task ResolveExistingOnlyAsync(
            InvitationRequestItem item, string? createdByClientId, List<InvitationResultItem> results)
        {
            TellmaIdentityUser? existing = await userManager.FindByEmailAsync(item.Email);

            // The credential question is asked only of a user who could answer Active: a disabled
            // or purged user is refused whatever they hold.
            bool hasCredential = existing is not null
                && existing.LifecycleState is not (UserLifecycleState.Disabled or UserLifecycleState.Purged)
                && await HasCredentialAsync(existing);

            switch (ExistingOnlyResolution.Resolve(existing?.LifecycleState, hasCredential))
            {
                case ExistingOnlyOutcome.UnknownEmail:
                    await RefuseAsync(item, null, InvitationErrors.UnknownEmail, createdByClientId, results);
                    return;

                case ExistingOnlyOutcome.Refused:
                    await RefuseAsync(item, existing!.Id, AdministrativelyRefused, createdByClientId, results);
                    return;

                case ExistingOnlyOutcome.NoCredential:
                    await RefuseAsync(
                        item, existing!.Id, InvitationErrors.ExistingUserHasNoCredential, createdByClientId, results);
                    return;

                case ExistingOnlyOutcome.Active:
                default:
                    break;
            }

            // Active: the caller records a membership, which ends an orphaned state exactly as an
            // ordinary invite ends it — but no link is issued and no email is queued.
            if (existing!.LifecycleState == UserLifecycleState.Orphaned)
            {
                string? restoreError = await RestoreOrphanedAsync(existing, reason: "existing_only");
                if (restoreError is not null)
                {
                    await RefuseAsync(item, existing.Id, restoreError, createdByClientId, results);
                    return;
                }
            }

            results.Add(new InvitationResultItem(item.Email, existing.Id, InvitationStatus.Active, null));
            metrics.Invitation(nameof(InvitationStatus.Active));
        }

        /// <summary>
        ///     Records a refused user: an error result with no status and no subject, the refusal
        ///     metric and a failure audit row. A refused user never aborts the batch: earlier users
        ///     are already persisted, their emails must still go out, and the caller needs every
        ///     user's outcome to record membership.
        /// </summary>
        /// <param name="item">The refused user.</param>
        /// <param name="subject">The existing user's subject, for the audit row only.</param>
        /// <param name="error">Why the user was refused.</param>
        /// <param name="createdByClientId">The calling client.</param>
        /// <param name="results">The batch's results so far.</param>
        private async Task RefuseAsync(
            InvitationRequestItem item,
            string? subject,
            string? error,
            string? createdByClientId,
            List<InvitationResultItem> results)
        {
            results.Add(new InvitationResultItem(item.Email, null, null, error));
            metrics.Invitation(RefusedMetricStatus);

            // Audit writes carry no cancellation: the audit logger rethrows one, which would turn
            // a recorded outcome into a thrown failure for a user whose fate is settled.
            await auditLogger.LogAsync(
                new AuditEventEntry
                {
                    Action = AuditActions.UserInvited,
                    Subject = subject,
                    ClientId = createdByClientId,
                    Outcome = "failure",
                    DetailsJson = System.Text.Json.JsonSerializer.Serialize(
                        new { reason = error, existingOnly = item.ExistingOnly }),
                },
                CancellationToken.None);
        }

        /// <summary>Whether a user holds any credential: a passkey, a password or an external login.</summary>
        /// <param name="user">The user.</param>
        /// <returns>Whether the user can sign in without an invitation link.</returns>
        private async Task<bool> HasCredentialAsync(TellmaIdentityUser user)
        {
            return (await userManager.GetPasskeysAsync(user)).Count > 0
                || await userManager.HasPasswordAsync(user)
                || (await userManager.GetLoginsAsync(user)).Count > 0;
        }

        /// <summary>Restores an orphaned user to <c>Active</c> and audits the change.</summary>
        /// <param name="user">The orphaned user, tracked by the shared store.</param>
        /// <param name="reason">Why the user was restored, for the audit row.</param>
        /// <returns>Null on success, or the per-user error when the restore failed.</returns>
        private async Task<string?> RestoreOrphanedAsync(TellmaIdentityUser user, string reason)
        {
            user.LifecycleState = UserLifecycleState.Active;
            user.OrphanedUtc = null;
            IdentityResult restored = await userManager.UpdateAsync(user);
            if (!restored.Succeeded)
            {
                // Reporting success for a user still marked orphaned would be a lie the caller
                // records as membership. Discard the in-memory mutation too: the store is shared
                // across the batch, so a later user's save would otherwise commit the very restore
                // this user was told had been refused. Only this user's entry is detached —
                // clearing the tracker would take the rest of the request scope's pending changes
                // with it.
                context.Entry(user).State = EntityState.Detached;
                return "The user could not be restored from the orphaned state.";
            }

            await auditLogger.LogAsync(new AuditEventEntry
            {
                Action = AuditActions.UserLifecycleChanged,
                Subject = user.Id,
                Outcome = "success",
                DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { from = "Orphaned", to = "Active", reason }),
            });
            return null;
        }

        /// <summary>
        ///     Creates a new user or resolves the existing one, returning the outcome status — or a
        ///     per-user error when the user must be refused.
        /// </summary>
        private async Task<(TellmaIdentityUser? User, InvitationStatus Status, string? Error)> CreateOrGetAsync(
            InvitationRequestItem item)
        {
            TellmaIdentityUser? existing = await userManager.FindByEmailAsync(item.Email);
            if (existing is not null)
            {
                // A disabled or purged user is never reactivated by an invitation: re-enabling an
                // administratively disabled account, or resurrecting a data-erased identity, must
                // be a deliberate operator action, not a side effect of a bulk invite. The error
                // does not disclose which administrative state the account is in.
                if (existing.LifecycleState is UserLifecycleState.Disabled or UserLifecycleState.Purged)
                {
                    return (existing, default, AdministrativelyRefused);
                }

                if (existing.LifecycleState == UserLifecycleState.Active && await HasCredentialAsync(existing))
                {
                    return (existing, InvitationStatus.Active, null);
                }

                // Restore an orphaned user; a credential-less Active user is simply re-invited.
                if (existing.LifecycleState == UserLifecycleState.Orphaned)
                {
                    string? restoreError = await RestoreOrphanedAsync(existing, reason: "reinvited");
                    if (restoreError is not null)
                    {
                        return (existing, default, restoreError);
                    }
                }

                return (existing, InvitationStatus.Reinvited, null);
            }

            TellmaIdentityUser user = new()
            {
                Id = Guid.NewGuid().ToString("D"),
                UserName = item.Email,
                Email = item.Email,
                EmailConfirmed = false,
                DisplayName = item.DisplayName,
                Locale = string.IsNullOrWhiteSpace(item.Locale) ? "en" : item.Locale,
                Gender = item.Gender,
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = timeProvider.GetUtcNow(),
            };

            IdentityResult result = await userManager.CreateAsync(user);
            return result.Succeeded
                ? (user, InvitationStatus.Invited, null)
                : (null, default, "The user could not be created: "
                    + string.Join("; ", result.Errors.Select(static e => e.Description)));
        }
    }

    /// <summary>Source-generated log messages for <see cref="InvitationService" />.</summary>
    internal static partial class InvitationLog
    {
        /// <summary>One user of a batch could not be invited.</summary>
        /// <param name="logger">The logger.</param>
        /// <param name="exception">The failure.</param>
        /// <param name="email">The user that failed.</param>
        [LoggerMessage(Level = LogLevel.Warning, Message = "Inviting {Email} failed; the rest of the batch continues.")]
        public static partial void UserFailed(ILogger logger, Exception exception, string email);

    }
}
