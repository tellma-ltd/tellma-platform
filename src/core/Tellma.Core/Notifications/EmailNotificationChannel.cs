// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text.Json;
using Tellma.Core.Abstractions.Access;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Access;
using Tellma.Core.Composition;
using Tellma.Core.Outbox;

namespace Tellma.Core.Notifications
{
    // Spec 0022 §4: the email channel renders like any producer, in C#, in each recipient's
    // language, calendar and zone, and hands the outbox one request per addressed recipient, so every
    // row records exactly what was sent and the dispatch only sends. The rows are enlisted with the
    // delivery job's frame and commit with its completion. Spec 0021 §4.4: channels have no
    // duplicate detection, and a notification email has no attachments. Spec 0018 §3.5: the test
    // send goes straight to the email sender, outside any batch and the outbox, to the caller's own
    // address, so a user learns at once whether email reaches them.
    /// <summary>
    ///     The sender of the <c>email</c> notification channel: each recipient gets one outgoing email,
    ///     to their contact address or else their sign-in address, rendered in their own language,
    ///     calendar and display zone, linking to the notification's target.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The recipients are grouped by their localization, and each group renders the
    ///         notification's text once. The subject and the bodies are Core's server resources
    ///         <c>Email_Notification_Subject</c>, <c>Email_Notification_Text</c> and
    ///         <c>Email_Notification_Html</c>, given the tenant's name in the recipient's content
    ///         language, the notification's text and the link; the HTML body receives each of them
    ///         HTML-encoded. The subject takes the text's first line only, and is cut to fit its column.
    ///     </para>
    ///     <para>
    ///         The emails are addressed to members of the tenant, so their audience is
    ///         <see cref="EmailAudience.Internal" />, and each row records its recipient and the
    ///         notification's target as the record it concerns. A recipient with no address is
    ///         skipped, and so, with a warning, is one whose address the outbox cannot store, so one
    ///         unusable address never fails the other recipients' emails; a display name loses its
    ///         control characters instead.
    ///     </para>
    ///     <para>
    ///         A test send goes to the connected user's contact address, else their sign-in address,
    ///         through the email sender directly: the subject and text are Core's server resources
    ///         <c>Email_TestNotification_Subject</c> and <c>Email_TestNotification_Text</c>, given the
    ///         tenant's name and the current time, rendered in the user's language, calendar and
    ///         region and in the tenant's zone. A host that composes no email sender answers
    ///         <see cref="TestNotificationResult.NotConfigured" />; a transport that fails or refuses
    ///         the message is an unavailable dependency.
    ///     </para>
    ///     <para>
    ///         The constructor needs no tenant, and the email sender is resolved only when a test
    ///         send needs it, so the startup gate can create the channel in a scope of no tenant to
    ///         read its key.
    ///     </para>
    /// </remarks>
    /// <param name="outbox">The scope's outbox, which the emails are enqueued through.</param>
    /// <param name="renderer">Renders the notification's text for each group of recipients.</param>
    /// <param name="localizers">Creates the localizer of Core's server resources.</param>
    /// <param name="links">Builds the link to the notification's target.</param>
    /// <param name="accessor">The scope's request context, which names the tenant and may hold its settings.</param>
    /// <param name="settings">The tenant's settings, when the scope's context holds none.</param>
    /// <param name="connection">The scope's connection, whose connected user a test send goes to.</param>
    /// <param name="negotiator">Negotiates the connected user's localization for a test send.</param>
    /// <param name="services">The scope's services, which hold the email sender a test send uses, if the host composes one.</param>
    /// <param name="loggers">Creates the logger that reports a skipped recipient and a failed test send.</param>
    internal sealed class EmailNotificationChannel(
        IEmailOutbox outbox,
        INotificationRenderer renderer,
        IStringLocalizerFactory localizers,
        ITellmaLinks links,
        IRequestContextAccessor accessor,
        ITenantSettingsCache settings,
        IScopeConnection connection,
        ILocalizationNegotiator negotiator,
        IServiceProvider services,
        ILoggerFactory loggers) : INotificationChannel
    {
        /// <summary>The resource of an email notification's subject. Arguments: <c>tenantName</c> and <c>text</c>.</summary>
        public const string SubjectKey = "Email_Notification_Subject";

        /// <summary>The resource of an email notification's plain-text body. Arguments: <c>tenantName</c>, <c>text</c> and <c>link</c>.</summary>
        public const string TextKey = "Email_Notification_Text";

        /// <summary>The resource of an email notification's HTML body. Arguments: <c>tenantName</c>, <c>text</c> and <c>link</c>, each HTML-encoded.</summary>
        public const string HtmlKey = "Email_Notification_Html";

        /// <summary>The resource of a test email's subject. Arguments: <c>tenantName</c> and <c>sentAt</c>.</summary>
        public const string TestSubjectKey = "Email_TestNotification_Subject";

        /// <summary>The resource of a test email's plain-text body. Arguments: <c>tenantName</c> and <c>sentAt</c>.</summary>
        public const string TestTextKey = "Email_TestNotification_Text";

        /// <summary>The base name of Core's server-only resources.</summary>
        private const string ServerStringsBaseName = CoreStrings.Assembly + ".Resources.ServerStrings";

        /// <summary>The argument naming the tenant.</summary>
        private const string TenantNameArgument = "tenantName";

        /// <summary>The argument holding the notification's rendered text.</summary>
        private const string TextArgument = "text";

        /// <summary>The argument holding the link to the notification's target.</summary>
        private const string LinkArgument = "link";

        /// <summary>The argument holding the instant a test email is sent.</summary>
        private const string SentAtArgument = "sentAt";

        /// <summary>The dependency a failed test send reports as unavailable.</summary>
        private const string EmailDependency = "email";

        /// <summary>Reports a recipient skipped because their email cannot be stored.</summary>
        private readonly ILogger _logger = loggers.CreateLogger(NotificationsLog.Category);

        /// <inheritdoc />
        public string Key => CoreFeature.EmailChannel;

        /// <inheritdoc />
        public async Task DeliverAsync(
            IOpenWriteHost host,
            NotificationRequest request,
            IReadOnlyList<NotificationRecipient> recipients,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(host);
            ArgumentNullException.ThrowIfNull(request);
            ArgumentNullException.ThrowIfNull(recipients);

            // --- The addressed recipients: the contact address, else the sign-in address ---
            // Spec 0022 §4 sends to a recipient with an address and skips one without. An address
            // the outbox would refuse is skipped the same way, with a warning: the outbox refuses a
            // whole call for one bad request, and that would fail every other recipient's email.
            List<(NotificationRecipient Recipient, EmailAddress Mailbox)> addressed = [];
            foreach (NotificationRecipient recipient in recipients)
            {
                if (Address(recipient) is not string address)
                {
                    continue;
                }

                EmailAddress mailbox = OutgoingEmailText.Mailbox(address, recipient.Name);
                if (EmailAddressList.IsWellFormed(mailbox))
                {
                    addressed.Add((recipient, mailbox));
                }
                else
                {
                    NotificationsLog.EmailRecipientSkipped(_logger, recipient.UserId);
                }
            }

            if (addressed.Count == 0)
            {
                return;
            }

            // --- What every email shares: the tenant's names, the link, the stored arguments ---
            int tenantId = accessor.Current.RequireTenant().Id;
            TenantSettings tenant = accessor.Current.TenantSettings
                ?? (await settings.GetAsync().ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("The tenant settings cache served no settings.");
            LocalizedText tenantName = OutgoingEmailText.TenantName(tenant, accessor.Current.RequireTenant());
            string link = links.Target(tenantId, request.TargetResource, request.TargetId);
            string? argumentsJson = ArgumentsJson(request.Arguments);
            IStringLocalizer strings = localizers.Create(ServerStringsBaseName, CoreStrings.Assembly);

            // --- One rendering per localization, one request per addressed recipient, in their order ---
            Dictionary<LocalizationKey, Rendered> rendered = [];
            List<EmailEnqueueRequest> emails = new(addressed.Count);
            foreach ((NotificationRecipient recipient, EmailAddress mailbox) in addressed)
            {
                var key = LocalizationKey.Of(recipient.Localization);
                if (!rendered.TryGetValue(key, out Rendered? email))
                {
                    email = Render(strings, request, argumentsJson, tenantName.In(recipient.Localization.ContentLanguageIndex), link, recipient.Localization);
                    rendered.Add(key, email);
                }

                EmailEnqueueRequest message = new(
                    new EmailMessage
                    {
                        To = [mailbox],
                        Subject = email.Subject,
                        TextBody = email.Text,
                        HtmlBody = email.Html,
                        Audience = EmailAudience.Internal,
                    },
                    RecipientUserId: recipient.UserId,
                    RegardingEntity: request.TargetResource,
                    RegardingId: request.TargetId,
                    Source: EmailEnqueueRequest.NotificationSource);

                // The outbox's own rules are the last word: a request it would refuse is skipped
                // like an unusable address, never sent to fail the whole call.
                if (EmailOutbox.IsWellFormed(message))
                {
                    emails.Add(message);
                }
                else
                {
                    NotificationsLog.EmailRecipientSkipped(_logger, recipient.UserId);
                }
            }

            // --- One enqueue, enlisted with the delivery job's frame ---
            if (emails.Count > 0)
            {
                _ = await outbox.EnqueueAsync(host, emails, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <inheritdoc />
        /// <exception cref="DependencyUnavailableException">
        ///     The transport failed or refused the message, or the sender threw before it answered; a
        ///     refusal carries a <see cref="TestMessageRejectedException" /> as its inner exception.
        /// </exception>
        public async Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
        {
            // --- A host that composes no email sender cannot send one ---
            if (services.GetService<IEmailSender>() is not IEmailSender sender)
            {
                return TestNotificationResult.NotConfigured;
            }

            // --- The connected user's address: the contact address, else the sign-in address ---
            // Spec 0018 §3.5: the profile is in memory once connected, so this costs no round trip.
            UserProfile profile = (await connection.CurrentAsync(cancellationToken).ConfigureAwait(false)).Profile;
            string? address = !string.IsNullOrWhiteSpace(profile.ContactEmail) ? profile.ContactEmail
                : !string.IsNullOrWhiteSpace(profile.Email) ? profile.Email
                : null;
            if (address is null)
            {
                return TestNotificationResult.NoContactAddress;
            }

            // --- The user's localization: their language, calendar and region, in the tenant's zone ---
            // The zone preference is left out so the time reads as the tenant's current time.
            TenantSettings tenant = accessor.Current.TenantSettings
                ?? (await settings.GetAsync().ConfigureAwait(false)).Value
                ?? throw new InvalidOperationException("The tenant settings cache served no settings.");
            LocalizationContext localization = negotiator.Negotiate(new NegotiationInput(
                AcceptLanguage: null,
                CalendarHeader: null,
                TimeZoneHeader: null,
                profile.PreferredLanguage,
                profile.PreferredCalendar,
                PreferredTimeZone: null,
                profile.PreferredRegion,
                MessageLanguage: null,
                MessageCalendar: null,
                tenant));
            int slot = localization.ContentLanguageIndex;

            // --- The mailbox; an address no transport could parse is no contact address ---
            EmailAddress mailbox = OutgoingEmailText.Mailbox(address, new LocalizedText(profile.Name, profile.Name2, profile.Name3).In(slot));
            if (!EmailAddressList.IsWellFormed(mailbox))
            {
                return TestNotificationResult.NoContactAddress;
            }

            // --- The subject and the text, rendered inside the user's localization ---
            string tenantName = OutgoingEmailText.TenantName(tenant, accessor.Current.RequireTenant()).In(slot);
            DateTimeOffset sentAt = accessor.Current.Now;
            IStringLocalizer strings = localizers.Create(ServerStringsBaseName, CoreStrings.Assembly);
            string subject, text;
            using (LocalizationScope.Enter(localization))
            {
                subject = OutgoingEmailText.FitSubject(strings[TestSubjectKey, TestArguments(tenantName, sentAt)].Value);
                text = strings[TestTextKey, TestArguments(tenantName, sentAt)].Value;
            }

            EmailMessage message = new()
            {
                To = [mailbox],
                Subject = subject,
                TextBody = text,
                Audience = EmailAudience.Internal,
            };

            // --- One send; anything but an accepted message is an unavailable dependency, a
            // refusal marked as one so that it is counted apart from an outage ---
            IReadOnlyList<EmailSendResult> results;
            try
            {
                results = await sender.SendAsync([message], cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException and not DependencyUnavailableException)
            {
                NotificationsLog.TestEmailFailed(_logger, exception);
                throw new DependencyUnavailableException(EmailDependency, null, exception);
            }

            EmailSendResult result = results.Count == 1
                ? results[0]
                : throw new InvalidOperationException("The email sender answered a result count other than the one message it was handed.");
            if (result.Outcome is EmailSendOutcome.Sent or EmailSendOutcome.Sandboxed)
            {
                return TestNotificationResult.Sent;
            }

            NotificationsLog.TestEmailNotSent(_logger, result.Outcome, result.Error);
            throw new DependencyUnavailableException(
                EmailDependency,
                null,
                result.Outcome == EmailSendOutcome.Rejected ? new TestMessageRejectedException() : null);
        }

        /// <summary>The address a recipient's email goes to: their contact address, else their sign-in address; null for neither.</summary>
        /// <param name="recipient">The recipient.</param>
        /// <returns>The address, or null.</returns>
        internal static string? Address(NotificationRecipient recipient)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            return !string.IsNullOrWhiteSpace(recipient.ContactEmail) ? recipient.ContactEmail
                : !string.IsNullOrWhiteSpace(recipient.Email) ? recipient.Email
                : null;
        }

        /// <summary>The first line of a rendered notification: its title, without the detail line.</summary>
        /// <param name="text">The rendered text.</param>
        /// <returns>The title.</returns>
        internal static string Title(string text)
        {
            ArgumentNullException.ThrowIfNull(text);
            int end = text.IndexOf('\n', StringComparison.Ordinal);
            return end < 0 ? text : text[..end];
        }

        /// <summary>The notification's stored arguments as JSON text; null for none.</summary>
        /// <param name="arguments">The request's arguments: the stored JSON as a <see cref="JsonElement" />, or null.</param>
        /// <returns>The JSON text, or null.</returns>
        private static string? ArgumentsJson(object? arguments)
        {
            return arguments switch
            {
                null => null,
                JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
                JsonElement element => element.GetRawText(),
                _ => JsonSerializer.Serialize(arguments, arguments.GetType(), TellmaJson.Options),
            };
        }

        /// <summary>Renders one group's email: the text once, then the subject and both bodies, all in the group's localization.</summary>
        /// <param name="strings">Core's server resources.</param>
        /// <param name="request">The notification.</param>
        /// <param name="argumentsJson">Its stored arguments.</param>
        /// <param name="tenantName">The tenant's name in the group's content language.</param>
        /// <param name="link">The link to the notification's target.</param>
        /// <param name="localization">The group's localization.</param>
        /// <returns>The email's subject and bodies.</returns>
        private Rendered Render(IStringLocalizer strings, NotificationRequest request, string? argumentsJson, string tenantName, string link, LocalizationContext localization)
        {
            using (LocalizationScope.Enter(localization))
            {
                // --- The notification's text, in the group's language, calendar and zone ---
                string text = renderer.Render(request.Type, argumentsJson, request.TargetResource, request.TargetId, localization);

                // --- The subject and the text body from the plain values ---
                string subject = OutgoingEmailText.FitSubject(strings[SubjectKey, Arguments(tenantName, Title(text), link)].Value);
                string body = strings[TextKey, Arguments(tenantName, text, link)].Value;

                // --- The HTML body from the encoded values, the text's lines kept as line breaks ---
                string html = strings[HtmlKey, Arguments(
                    WebUtility.HtmlEncode(tenantName),
                    WebUtility.HtmlEncode(text).Replace("\n", "<br>", StringComparison.Ordinal),
                    WebUtility.HtmlEncode(link))].Value;
                return new Rendered(subject, body, html);
            }
        }

        /// <summary>The ICU arguments of the three resources.</summary>
        /// <param name="tenantName">The tenant's name.</param>
        /// <param name="text">The notification's text.</param>
        /// <param name="link">The link.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> Arguments(string tenantName, string text, string link)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TenantNameArgument] = tenantName,
                [TextArgument] = text,
                [LinkArgument] = link,
            };
        }

        /// <summary>The ICU arguments of the test email's two resources.</summary>
        /// <param name="tenantName">The tenant's name.</param>
        /// <param name="sentAt">When the email is sent.</param>
        /// <returns>The arguments.</returns>
        private static Dictionary<string, object?> TestArguments(string tenantName, DateTimeOffset sentAt)
        {
            return new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                [TenantNameArgument] = tenantName,
                [SentAtArgument] = sentAt,
            };
        }

        /// <summary>One group's rendered email.</summary>
        /// <param name="Subject">The subject.</param>
        /// <param name="Text">The plain-text body.</param>
        /// <param name="Html">The HTML body.</param>
        private sealed record Rendered(string Subject, string Text, string Html);

        /// <summary>
        ///     What makes two recipients' text identical: the culture, the calendar, the display zone and
        ///     the content slot. A localization is a record whose content languages compare by
        ///     reference, so it is not the key itself.
        /// </summary>
        /// <param name="CultureTag">The negotiated culture's tag.</param>
        /// <param name="Language">The negotiated language.</param>
        /// <param name="Calendar">The calendar's code.</param>
        /// <param name="TimeZone">The display zone's id.</param>
        /// <param name="ContentLanguageIndex">The content slot.</param>
        private readonly record struct LocalizationKey(string CultureTag, string Language, string Calendar, string TimeZone, int ContentLanguageIndex)
        {
            /// <summary>The key of a localization.</summary>
            /// <param name="localization">The localization.</param>
            /// <returns>The key.</returns>
            public static LocalizationKey Of(LocalizationContext localization)
            {
                return new LocalizationKey(localization.CultureTag, localization.Language, localization.Calendar.Code, localization.TimeZone.Id, localization.ContentLanguageIndex);
            }
        }
    }
}
