// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.AspNetCore.Identity;
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
    /// <param name="ReturnUrl">Where the accepted invitation returns the user.</param>
    public sealed record InvitationRequestItem(string Email, string? DisplayName, string? Locale, string? ReturnUrl);

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

    /// <summary>One user's invitation result. The invitation link is never returned.</summary>
    /// <param name="Email">The invited email.</param>
    /// <param name="Subject">The user's stable subject identifier.</param>
    /// <param name="Status">The per-user outcome.</param>
    public sealed record InvitationResultItem(string Email, string Subject, InvitationStatus Status);

    /// <summary>
    ///     Bulk user invitation: create-or-get each user by email, assign each a <c>sub</c>, send a
    ///     localized single-use link, and return per-user status plus <c>sub</c>. The link — the
    ///     email-ownership proof — is never returned in any environment. Users are created through
    ///     the Identity user manager (one persistence operation each, so its validators and
    ///     normalizers run), while the email delivery for the whole batch is a single hand-off.
    /// </summary>
    /// <param name="userManager">The Identity user manager.</param>
    /// <param name="tokens">One-time invitation tokens.</param>
    /// <param name="emailSender">The batch email transport.</param>
    /// <param name="templates">Localized message construction.</param>
    /// <param name="options">The engine options (issuer for the link base).</param>
    /// <param name="auditLogger">Audit emission.</param>
    /// <param name="metrics">Identity metrics.</param>
    /// <param name="timeProvider">The clock.</param>
    public sealed class InvitationService(
        UserManager<TellmaIdentityUser> userManager,
        IOneTimeTokenService tokens,
        IEmailSender emailSender,
        EmailTemplateService templates,
        IOptions<TellmaIdentityOptions> options,
        IAuditLogger auditLogger,
        IdentityMetrics metrics,
        TimeProvider timeProvider)
    {
        /// <summary>The invitation link lifetime.</summary>
        public static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);

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

            foreach (InvitationRequestItem item in items)
            {
                (TellmaIdentityUser user, InvitationStatus status) = await CreateOrGetAsync(item);
                results.Add(new InvitationResultItem(item.Email, user.Id, status));
                metrics.Invitation(status.ToString());

                // Active users need no link — nothing to prove; the distribution records membership.
                if (status == InvitationStatus.Active)
                {
                    continue;
                }

                string token = await tokens.IssueAsync(
                    user.Id, SingleUseCodePurpose.Invitation, InvitationLifetime, item.ReturnUrl, createdByClientId, cancellationToken);
                string link = BuildLink(token);
                emails.Add(templates.Invitation(user, link, InvitationLifetime.Days));

                await auditLogger.LogAsync(
                    new AuditEventEntry
                    {
                        Action = AuditActions.UserInvited,
                        Subject = user.Id,
                        ClientId = createdByClientId,
                        Outcome = "success",
                    },
                    cancellationToken);
            }

            // One batched hand-off for the whole invitation.
            if (emails.Count > 0)
            {
                await emailSender.SendAsync(emails, cancellationToken);
            }

            return results;
        }

        /// <summary>Creates a new user or resolves the existing one, returning the outcome status.</summary>
        private async Task<(TellmaIdentityUser User, InvitationStatus Status)> CreateOrGetAsync(InvitationRequestItem item)
        {
            TellmaIdentityUser? existing = await userManager.FindByEmailAsync(item.Email);
            if (existing is not null)
            {
                // A disabled or purged user is never reactivated by an invitation: re-enabling an
                // administratively disabled account, or resurrecting a data-erased identity, must
                // be a deliberate operator action, not a side effect of a bulk invite.
                if (existing.LifecycleState is UserLifecycleState.Disabled or UserLifecycleState.Purged)
                {
                    throw new Provisioning.ProvisioningValidationException(
                        $"The user '{item.Email}' is {existing.LifecycleState} and cannot be re-invited; an operator must re-enable it first.");
                }

                bool hasCredentials = (await userManager.GetPasskeysAsync(existing)).Count > 0
                    || await userManager.HasPasswordAsync(existing)
                    || (await userManager.GetLoginsAsync(existing)).Count > 0;

                if (existing.LifecycleState == UserLifecycleState.Active && hasCredentials)
                {
                    return (existing, InvitationStatus.Active);
                }

                // Restore an orphaned user; a credential-less Active user is simply re-invited.
                if (existing.LifecycleState == UserLifecycleState.Orphaned)
                {
                    existing.LifecycleState = UserLifecycleState.Active;
                    existing.OrphanedUtc = null;
                    await userManager.UpdateAsync(existing);
                    await auditLogger.LogAsync(new AuditEventEntry
                    {
                        Action = AuditActions.UserLifecycleChanged,
                        Subject = existing.Id,
                        Outcome = "success",
                        DetailsJson = System.Text.Json.JsonSerializer.Serialize(new { from = "Orphaned", to = "Active", reason = "reinvited" }),
                    });
                }

                return (existing, InvitationStatus.Reinvited);
            }

            TellmaIdentityUser user = new()
            {
                Id = Guid.NewGuid().ToString("D"),
                UserName = item.Email,
                Email = item.Email,
                EmailConfirmed = false,
                DisplayName = item.DisplayName,
                Locale = string.IsNullOrWhiteSpace(item.Locale) ? "en" : item.Locale,
                LifecycleState = UserLifecycleState.Active,
                CreatedUtc = timeProvider.GetUtcNow(),
            };

            IdentityResult result = await userManager.CreateAsync(user);
            return !result.Succeeded
                ? throw new InvalidOperationException(
                    "Failed to create the invited user: " + string.Join("; ", result.Errors.Select(static e => e.Description)))
                : ((TellmaIdentityUser User, InvitationStatus Status))(user, InvitationStatus.Invited);
        }

        /// <summary>Builds the absolute invitation link (the token, not the return url, is in the URL).</summary>
        private string BuildLink(string token)
        {
            string prefix = options.Value.PathBase;
            return new Uri(options.Value.Issuer!, $"{prefix}/Identity/Account/Invitation?code={Uri.EscapeDataString(token)}").AbsoluteUri;
        }
    }
}
