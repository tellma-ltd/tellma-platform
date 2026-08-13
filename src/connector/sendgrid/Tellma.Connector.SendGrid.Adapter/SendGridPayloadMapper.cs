// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Hosting;

namespace Tellma.Connector.SendGrid.Adapter
{
    /// <summary>Maps a platform message onto a v3 mail-send payload.</summary>
    internal static class SendGridPayloadMapper
    {
        /// <summary>
        ///     SendGrid's documented cap on the combined recipient count of one request. A message
        ///     over it is rejected without a request rather than eating a 400.
        /// </summary>
        internal const int MaxRecipientsPerRequest = 1000;

        /// <summary>The custom argument the correlation rides back on.</summary>
        internal const string CorrelationCustomArg = "tellma_correlation";

        /// <summary>Builds the payload.</summary>
        /// <param name="message">The platform message.</param>
        /// <param name="options">The transport's options, for the default sender.</param>
        /// <param name="deployment">This deployment, whose id envelopes the correlation.</param>
        /// <param name="channel">Which channel this send is on.</param>
        /// <returns>The mail-send payload.</returns>
        internal static SendGridMailRequest Map(
            EmailMessage message,
            SendGridEmailOptions options,
            DeploymentIdentity deployment,
            SendGridChannel channel)
        {
            EmailAddress from = message.From ?? options.From.ToEmailAddress();

            // One personalization: each platform message carries its own subject and bodies, so
            // SendGrid's same-content fan-out does not apply to this contract.
            SendGridPersonalization personalization = new(
                [.. message.To.Select(ToAddress)],
                message.Cc.Count > 0 ? [.. message.Cc.Select(ToAddress)] : null,
                message.Bcc.Count > 0 ? [.. message.Bcc.Select(ToAddress)] : null);

            // text/plain must come before text/html; SendGrid rejects the reverse.
            List<SendGridContent> content = [new SendGridContent("text/plain", message.TextBody)];
            if (message.HtmlBody is not null)
            {
                content.Add(new SendGridContent("text/html", message.HtmlBody));
            }

            List<SendGridAttachment>? attachments = null;
            if (message.Attachments.Count > 0)
            {
                attachments = new List<SendGridAttachment>(message.Attachments.Count);
                foreach (EmailAttachment attachment in message.Attachments)
                {
                    attachments.Add(new SendGridAttachment(
                        Convert.ToBase64String(attachment.Content.Span),
                        attachment.ContentType,
                        attachment.FileName,
                        attachment.ContentId is null ? "attachment" : "inline",
                        attachment.ContentId));
                }
            }

            Dictionary<string, string>? customArgs = null;
            if (message.Correlation is EmailCorrelation correlation)
            {
                // The deployment id prefixes the correlation as a wire envelope, so an event
                // returning under shared provider credentials can be told apart from a misrouted one.
                customArgs = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    [CorrelationCustomArg] = $"{deployment.DeploymentId}:{correlation}",
                };
            }

            return new SendGridMailRequest
            {
                Personalizations = [personalization],
                From = ToAddress(from),
                ReplyTo = message.ReplyTo is null ? null : ToAddress(message.ReplyTo),
                Subject = message.Subject,
                Content = content,
                Attachments = attachments,
                CustomArgs = customArgs,
                MailSettings = channel == SendGridChannel.Sandbox
                    ? new SendGridMailSettings(new SendGridSandboxMode(true))
                    : null,
            };
        }

        /// <summary>The combined recipient count of a message, against SendGrid's per-request cap.</summary>
        /// <param name="message">The message.</param>
        /// <returns>The number of addresses one request would carry.</returns>
        internal static int CountRecipients(EmailMessage message)
        {
            return message.To.Count + message.Cc.Count + message.Bcc.Count;
        }

        private static SendGridEmailAddress ToAddress(EmailAddress address)
        {
            return new SendGridEmailAddress(address.Address, address.DisplayName);
        }
    }
}
