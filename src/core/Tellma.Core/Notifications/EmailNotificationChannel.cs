// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Composition;
using Tellma.Core.Abstractions.Crud;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Abstractions.Settings;
using Tellma.Core.Abstractions.Tenancy;
using Tellma.Core.Composition;
using Tellma.Core.Outbox;

namespace Tellma.Core.Notifications
{
    // Spec 0022 §4: the email channel renders like any producer, in C#, in each recipient's
    // language, calendar and zone, and hands the outbox one request per addressed recipient, so every
    // row records exactly what was sent and the dispatch only sends. The rows are enlisted with the
    // delivery job's frame and commit with its completion. Spec 0021 §4.4: channels have no
    // duplicate detection, and a notification email has no attachments.
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
    ///         control characters instead. The constructor needs no tenant, so the startup gate can
    ///         read the key.
    ///     </para>
    /// </remarks>
    /// <param name="outbox">The scope's outbox, which the emails are enqueued through.</param>
    /// <param name="renderer">Renders the notification's text for each group of recipients.</param>
    /// <param name="localizers">Creates the localizer of Core's server resources.</param>
    /// <param name="links">Builds the link to the notification's target.</param>
    /// <param name="accessor">The scope's request context, which names the tenant and may hold its settings.</param>
    /// <param name="settings">The tenant's settings, when the scope's context holds none.</param>
    /// <param name="loggers">Creates the logger that reports a skipped recipient.</param>
    internal sealed class EmailNotificationChannel(
        IEmailOutbox outbox,
        INotificationRenderer renderer,
        IStringLocalizerFactory localizers,
        ITellmaLinks links,
        IRequestContextAccessor accessor,
        ITenantSettingsCache settings,
        ILoggerFactory loggers) : INotificationChannel
    {
        /// <summary>The resource of an email notification's subject. Arguments: <c>tenantName</c> and <c>text</c>.</summary>
        public const string SubjectKey = "Email_Notification_Subject";

        /// <summary>The resource of an email notification's plain-text body. Arguments: <c>tenantName</c>, <c>text</c> and <c>link</c>.</summary>
        public const string TextKey = "Email_Notification_Text";

        /// <summary>The resource of an email notification's HTML body. Arguments: <c>tenantName</c>, <c>text</c> and <c>link</c>, each HTML-encoded.</summary>
        public const string HtmlKey = "Email_Notification_Html";

        /// <summary>The base name of Core's server-only resources.</summary>
        private const string ServerStringsBaseName = CoreStrings.Assembly + ".Resources.ServerStrings";

        /// <summary>The argument naming the tenant.</summary>
        private const string TenantNameArgument = "tenantName";

        /// <summary>The argument holding the notification's rendered text.</summary>
        private const string TextArgument = "text";

        /// <summary>The argument holding the link to the notification's target.</summary>
        private const string LinkArgument = "link";

        /// <summary>What a subject cut to fit its column ends with.</summary>
        private const char Ellipsis = '…';

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

                EmailAddress mailbox = Mailbox(address, recipient.Name);
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
                    email = Render(strings, request, argumentsJson, TenantName(tenant, recipient.Localization), link, recipient.Localization);
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
        /// <exception cref="NotSupportedException">Always: the email channel's test send is not built yet.</exception>
        public Task<TestNotificationResult> SendTestAsync(CancellationToken cancellationToken = default)
        {
            // The test send goes through the email sender outside any batch (spec 0018 §3.5); it
            // ships with the users' test-notification endpoint that calls it.
            throw new NotSupportedException("The email channel's test send is not available yet.");
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

        /// <summary>
        ///     A recipient's mailbox: the address without surrounding white space, and their name on
        ///     one line (each control character a space), or none when it is blank.
        /// </summary>
        /// <param name="address">The address.</param>
        /// <param name="name">The recipient's name; null for none.</param>
        /// <returns>The mailbox, which the outbox may still refuse for its address.</returns>
        internal static EmailAddress Mailbox(string address, string? name)
        {
            ArgumentNullException.ThrowIfNull(address);
            string? displayName = name is null ? null : OneLine(name);
            return new EmailAddress(address.Trim(), string.IsNullOrEmpty(displayName) ? null : displayName);
        }

        /// <summary>A subject on one line that fits its column: control characters become spaces, and a longer one is cut and ends with an ellipsis.</summary>
        /// <param name="subject">The rendered subject.</param>
        /// <returns>The subject to store.</returns>
        internal static string FitSubject(string subject)
        {
            ArgumentNullException.ThrowIfNull(subject);
            string line = OneLine(subject);
            if (line.Length <= OutgoingEmail.SubjectLength)
            {
                return line;
            }

            // Cut before a whole character, never between the halves of a surrogate pair.
            int cut = OutgoingEmail.SubjectLength - 1;
            if (char.IsLowSurrogate(line[cut]))
            {
                cut--;
            }

            return string.Concat(line.AsSpan(0, cut).TrimEnd(), Ellipsis.ToString());
        }

        /// <summary>A text on one line: each control character a space, and no surrounding white space.</summary>
        /// <param name="text">The text.</param>
        /// <returns>The line.</returns>
        private static string OneLine(string text)
        {
            StringBuilder builder = new(text.Length);
            foreach (char c in text)
            {
                _ = builder.Append(char.IsControl(c) ? ' ' : c);
            }

            return builder.ToString().Trim();
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

        /// <summary>The tenant's name in a reader's content language: the settings' names, else the catalog's.</summary>
        /// <param name="tenant">The tenant's settings.</param>
        /// <param name="localization">The reader's localization.</param>
        /// <returns>The name.</returns>
        private string TenantName(TenantSettings tenant, LocalizationContext localization)
        {
            IReadOnlyList<string?> names = tenant.Names;
            TenantDescriptor catalog = accessor.Current.RequireTenant();
            LocalizedText name = names.Count > 0 && !string.IsNullOrWhiteSpace(names[0])
                ? new LocalizedText(names[0]!, names.Count > 1 ? names[1] : null, names.Count > 2 ? names[2] : null)
                : new LocalizedText(catalog.Name, catalog.Name2, catalog.Name3);
            return name.In(localization.ContentLanguageIndex);
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
                string subject = FitSubject(strings[SubjectKey, Arguments(tenantName, Title(text), link)].Value);
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
