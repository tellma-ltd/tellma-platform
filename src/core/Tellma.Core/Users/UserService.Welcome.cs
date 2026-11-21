// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using System.Net;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Identity;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Outbox;

namespace Tellma.Core.Users
{
    // Spec 0018 §3.3 step 3: the welcome email, the one message a user keeps to find the
    // distribution again. It is enqueued through the action's frame, so its rows commit or roll back
    // with the write-back: a cancel after the identity server answered loses both, and the retry that
    // re-invites re-emails. Each email renders in its recipient's own localization, never the
    // caller's, and a recipient whose email the outbox would refuse is skipped with a warning, since
    // one bad address must never fail an invite the identity server already answered.
    public partial class UserService<TUser>
    {
        /// <summary>The resource of the welcome email's subject. Arguments: <c>tenantName</c>, <c>actorName</c>, <c>link</c> and <c>status</c>.</summary>
        internal const string WelcomeSubjectKey = "Email_UserAdded_Subject";

        /// <summary>The resource of the welcome email's plain-text body. Arguments: <c>tenantName</c>, <c>actorName</c>, <c>link</c> and <c>status</c>.</summary>
        internal const string WelcomeTextKey = "Email_UserAdded_Text";

        /// <summary>The resource of the welcome email's HTML body. Arguments: <c>tenantName</c>, <c>actorName</c>, <c>link</c> and <c>status</c>, each HTML-encoded.</summary>
        internal const string WelcomeHtmlKey = "Email_UserAdded_Html";

        /// <summary>The base name of Core's server-only resources.</summary>
        private const string ServerStringsBaseName = CoreStrings.Assembly + ".Resources.ServerStrings";

        /// <summary>
        ///     Enqueues the welcome email of every answered user with a status whose loaded row holds
        ///     no account or the one answered, through one call with the action's frame.
        /// </summary>
        /// <param name="context">The action's context, whose frame the emails commit with.</param>
        /// <param name="targets">The loaded targets in request order.</param>
        /// <param name="outcomes">The answered users' write-back rows, in request order.</param>
        /// <param name="answered">The identity server's answers, in request order.</param>
        /// <param name="settings">The tenant's settings.</param>
        /// <param name="link">The tenant's link, the invitation's return address.</param>
        /// <returns>A task that completes when the emails are enlisted.</returns>
        private async Task EnqueueWelcomeAsync(
            ActionContext<TUser, int> context,
            List<TUser> targets,
            UserInvitationOutcome[] outcomes,
            IReadOnlyList<IdentityInvitationResult> answered,
            TenantSettings settings,
            string link)
        {
            // --- The users the invite added: a status, and an account the row holds or gains ---
            List<(TUser User, InviteStatus Status)> added = [];
            for (int index = 0; index < outcomes.Length; index++)
            {
                TUser user = targets[index];
                if (answered[index].Status is InviteStatus status
                    && outcomes[index].Subject is string subject
                    && (user.Subject is null || string.Equals(user.Subject, subject, StringComparison.Ordinal)))
                {
                    added.Add((user, status));
                }
            }

            if (added.Count == 0)
            {
                return;
            }

            // --- What every email shares: the tenant's names, the caller's names, the resources ---
            // The caller is connected by the action's first round trip, so this costs none.
            TenantDescriptor tenant = context.Context.RequireTenant();
            LocalizedText tenantName = OutgoingEmailText.TenantName(settings, tenant);
            UserProfile caller = (await _dependencies.Connection.CurrentAsync(context.CancellationToken).ConfigureAwait(false)).Profile;
            LocalizedText actorName = new(caller.Name, caller.Name2, caller.Name3);
            IStringLocalizer strings = _dependencies.Localizers.Create(ServerStringsBaseName, CoreStrings.Assembly);

            // --- One request per user; one the outbox would refuse is skipped, never sent to fail the call ---
            List<EmailEnqueueRequest> emails = new(added.Count);
            foreach ((TUser user, InviteStatus status) in added)
            {
                if (Welcome(user, status, settings, tenantName, actorName, link, strings) is EmailEnqueueRequest email)
                {
                    emails.Add(email);
                }
                else
                {
                    UsersLog.WelcomeEmailSkipped(_dependencies.Logger, user.Id);
                }
            }

            if (emails.Count > 0)
            {
                _ = await _dependencies.Outbox.EnqueueAsync(context.Host, emails, context.CancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     The welcome email of one user: to their contact address, else their sign-in address,
        ///     rendered in their own language, calendar and content language; null when they have no
        ///     address the outbox would accept.
        /// </summary>
        /// <param name="user">The user as the action loaded them.</param>
        /// <param name="status">What the identity server answered for them.</param>
        /// <param name="settings">The tenant's settings, which their localization falls back on.</param>
        /// <param name="tenantName">The tenant's name in each content language.</param>
        /// <param name="actorName">The caller's name in each content language.</param>
        /// <param name="link">The tenant's link.</param>
        /// <param name="strings">Core's server resources.</param>
        /// <returns>The request; null when the user cannot be written to.</returns>
        internal EmailEnqueueRequest? Welcome(
            User user, InviteStatus status, TenantSettings settings, LocalizedText tenantName, LocalizedText actorName, string link, IStringLocalizer strings)
        {
            ArgumentNullException.ThrowIfNull(user);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(tenantName);
            ArgumentNullException.ThrowIfNull(actorName);
            ArgumentNullException.ThrowIfNull(link);
            ArgumentNullException.ThrowIfNull(strings);

            // --- The address: the contact address, else the sign-in address ---
            string? address = !string.IsNullOrWhiteSpace(user.ContactEmail) ? user.ContactEmail
                : !string.IsNullOrWhiteSpace(user.Email) ? user.Email
                : null;
            if (address is null)
            {
                return null;
            }

            // --- The user's localization: their preferences over the tenant's, never the caller's headers ---
            LocalizationContext localization = _dependencies.Negotiator.Negotiate(new NegotiationInput(
                AcceptLanguage: null,
                CalendarHeader: null,
                TimeZoneHeader: null,
                user.PreferredLanguage,
                user.PreferredCalendar,
                user.PreferredTimeZone,
                user.PreferredRegion,
                MessageLanguage: null,
                MessageCalendar: null,
                settings));
            int slot = localization.ContentLanguageIndex;

            // --- The subject and bodies, rendered inside the user's localization ---
            string subject, text, html;
            using (LocalizationScope.Enter(localization))
            {
                string tenantText = tenantName.In(slot);
                string actorText = actorName.In(slot);
                string statusText = status.ToString();
                subject = OutgoingEmailText.FitSubject(strings[WelcomeSubjectKey, WelcomeArguments(tenantText, actorText, link, statusText)].Value);
                text = strings[WelcomeTextKey, WelcomeArguments(tenantText, actorText, link, statusText)].Value;
                html = strings[WelcomeHtmlKey, WelcomeArguments(
                    WebUtility.HtmlEncode(tenantText), WebUtility.HtmlEncode(actorText), WebUtility.HtmlEncode(link), statusText)].Value;
            }

            EmailEnqueueRequest request = new(
                new EmailMessage
                {
                    To = [OutgoingEmailText.Mailbox(address, new LocalizedText(user.Name, user.Name2, user.Name3).In(slot))],
                    Subject = subject,
                    TextBody = text,
                    HtmlBody = html,
                    Audience = EmailAudience.Internal,
                },
                RecipientUserId: user.Id,
                RegardingEntity: CoreResources.User,
                RegardingId: user.Id);

            // The outbox's own rules are the last word on what it accepts.
            return EmailOutbox.IsWellFormed(request) ? request : null;
        }

        /// <summary>The ICU arguments of the welcome email's three resources.</summary>
        /// <param name="tenantName">The tenant's name.</param>
        /// <param name="actorName">The caller's name.</param>
        /// <param name="link">The tenant's link.</param>
        /// <param name="status">What the identity server answered: <c>Invited</c>, <c>Reinvited</c> or <c>Active</c>.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> WelcomeArguments(string tenantName, string actorName, string link, string status)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["tenantName"] = tenantName,
                ["actorName"] = actorName,
                ["link"] = link,
                ["status"] = status,
            };
        }
    }
}
