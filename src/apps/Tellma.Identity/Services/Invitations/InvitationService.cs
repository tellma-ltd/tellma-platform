// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tellma.Identity.Data;
using Tellma.Identity.Data.Entities;
using Tellma.Identity.Infrastructure;
using Tellma.Identity.Options;
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
    public sealed record InvitationRequestItem(
        string Email, string? DisplayName, string? Locale, string? ReturnUrl, UserGender? Gender = null);

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
    ///     whole batch is a single hand-off to the background dispatcher.
    /// </summary>
    /// <param name="context">The identity store, shared across the batch.</param>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tokens">One-time invitation tokens.</param>
    /// <param name="emailDispatcher">The background mail dispatch queue.</param>
    /// <param name="templates">Localized message construction.</param>
    /// <param name="options">The engine options (issuer for the link base).</param>
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
        IOptions<TellmaIdentityOptions> options,
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
            (TellmaIdentityUser? user, InvitationStatus status, string? error) = await CreateOrGetAsync(item);
            if (error is not null || user is null)
            {
                // A refused user never aborts the batch: earlier users are already persisted,
                // their emails must still go out, and the caller needs every user's outcome to
                // record membership.
                results.Add(new InvitationResultItem(item.Email, null, null, error));
                metrics.Invitation(RefusedMetricStatus);

                // Audit writes carry no cancellation: the audit logger rethrows one, which would
                // turn a recorded outcome into a thrown failure for a user whose fate is settled.
                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.UserInvited,
                        Subject = user?.Id,
                        ClientId = createdByClientId,
                        Outcome = "failure",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { reason = error }),
                    },
                    CancellationToken.None);
                return;
            }

            // Active users need no link — nothing to prove; the distribution records membership.
            if (status != InvitationStatus.Active)
            {
                string token = await tokens.IssueAsync(
                    user.Id, SingleUseCodePurpose.Invitation, InvitationLifetime, item.ReturnUrl, createdByClientId, cancellationToken);
                string link = BuildLink(token);
                emails.Add(templates.Invitation(user, link, InvitationLifetime.Days));

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
                    return (existing, default, "The user cannot be invited; an operator must re-enable the account first.");
                }

                bool hasCredentials = (await userManager.GetPasskeysAsync(existing)).Count > 0
                    || await userManager.HasPasswordAsync(existing)
                    || (await userManager.GetLoginsAsync(existing)).Count > 0;

                if (existing.LifecycleState == UserLifecycleState.Active && hasCredentials)
                {
                    return (existing, InvitationStatus.Active, null);
                }

                // Restore an orphaned user; a credential-less Active user is simply re-invited.
                if (existing.LifecycleState == UserLifecycleState.Orphaned)
                {
                    existing.LifecycleState = UserLifecycleState.Active;
                    existing.OrphanedUtc = null;
                    IdentityResult restored = await userManager.UpdateAsync(existing);
                    if (!restored.Succeeded)
                    {
                        // Reporting Reinvited for a user still marked orphaned would be a lie the
                        // caller records as membership. Discard the in-memory mutation too: the
                        // store is shared across the batch, so a later user's save would otherwise
                        // commit the very restore this user was told had been refused. Only this
                        // user's entry is detached — clearing the tracker would take the rest of
                        // the request scope's pending changes with it.
                        context.Entry(existing).State = EntityState.Detached;
                        return (existing, default, "The user could not be restored from the orphaned state.");
                    }

                    await auditLogger.LogAsync(new AuditEventEntry
                    {
                        Action = AuditActions.UserLifecycleChanged,
                        Subject = existing.Id,
                        Outcome = "success",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { from = "Orphaned", to = "Active", reason = "reinvited" }),
                    });
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

        /// <summary>Builds the absolute invitation link (the token, not the return url, is in the URL).</summary>
        private string BuildLink(string token)
        {
            string prefix = options.Value.PathBase;
            return new Uri(options.Value.Issuer!, $"{prefix}/Identity/Account/Invitation?code={Uri.EscapeDataString(token)}").AbsoluteUri;
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
