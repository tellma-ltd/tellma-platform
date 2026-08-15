// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MimeKit;
using MimeKit.Utils;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter
{
    /// <summary>Turns a platform message into the MIME message MailKit puts on the wire.</summary>
    internal static class SmtpMessageBuilder
    {
        /// <summary>Builds the MIME message.</summary>
        /// <param name="message">The platform message.</param>
        /// <param name="defaultFrom">The channel's configured sender, used when the message has none.</param>
        /// <returns>The MIME message, with its own generated <c>Message-Id</c>.</returns>
        internal static MimeMessage Build(EmailMessage message, EmailAddress defaultFrom)
        {
            MimeMessage mime = new();
            mime.From.Add(ToMailbox(message.From ?? defaultFrom));

            if (message.ReplyTo is EmailAddress replyTo)
            {
                mime.ReplyTo.Add(ToMailbox(replyTo));
            }

            AddAll(mime.To, message.To);
            AddAll(mime.Cc, message.Cc);
            AddAll(mime.Bcc, message.Bcc);
            mime.Subject = message.Subject;

            BodyBuilder body = new()
            {
                TextBody = message.TextBody,
            };

            if (message.HtmlBody is not null)
            {
                body.HtmlBody = message.HtmlBody;
            }

            foreach (EmailAttachment attachment in message.Attachments)
            {
                ContentType contentType = ParseContentType(attachment.ContentType);
                byte[] content = attachment.Content.ToArray();

                if (attachment.ContentId is null)
                {
                    body.Attachments.Add(attachment.FileName, content, contentType);
                    continue;
                }

                // A content id means the HTML body references the part with cid:, which requires it
                // to be a linked resource of a multipart/related — an ordinary attachment would not
                // resolve in any mail client.
                MimeEntity linked = body.LinkedResources.Add(attachment.FileName, content, contentType);
                linked.ContentId = attachment.ContentId;
            }

            mime.Body = body.ToMessageBody();
            return mime;
        }

        private static void AddAll(InternetAddressList list, IReadOnlyList<EmailAddress> addresses)
        {
            foreach (EmailAddress address in addresses)
            {
                list.Add(ToMailbox(address));
            }
        }

        private static MailboxAddress ToMailbox(EmailAddress address)
        {
            return new MailboxAddress(address.DisplayName, address.Address);
        }

        private static ContentType ParseContentType(string value)
        {
            // A malformed content type is caught by the adapter's own message validation before this
            // point; falling back keeps a defect from turning into an unhandled exception mid-batch.
            return ContentType.TryParse(value, out ContentType? parsed)
                ? parsed
                : new ContentType("application", "octet-stream");
        }

        /// <summary>
        ///     Reports the message id MimeKit generated, without the angle brackets its header
        ///     carries — that bare form is what a receiving server's logs and support tickets are
        ///     searched by.
        /// </summary>
        /// <param name="mime">The built message.</param>
        /// <returns>The message id.</returns>
        internal static string GetMessageId(MimeMessage mime)
        {
            // MimeMessage generates one in its constructor, so this is never empty in practice; the
            // fallback keeps a hand-built message from producing a null provider id.
            return string.IsNullOrEmpty(mime.MessageId) ? MimeUtils.GenerateMessageId() : mime.MessageId;
        }
    }
}
