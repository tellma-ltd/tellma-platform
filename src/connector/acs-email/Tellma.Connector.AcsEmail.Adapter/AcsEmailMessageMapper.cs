// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Azure.Communication.Email;
using Tellma.Core.Abstractions.Email;
// Azure.Communication.Email and the platform contract spell several type names the same way, so the
// overlapping SDK types are reached through aliases.
using AcsEmailAddress = Azure.Communication.Email.EmailAddress;
using AcsEmailAttachment = Azure.Communication.Email.EmailAttachment;
using AcsEmailMessage = Azure.Communication.Email.EmailMessage;
using TellmaEmailAddress = Tellma.Core.Abstractions.Email.EmailAddress;
using TellmaEmailAttachment = Tellma.Core.Abstractions.Email.EmailAttachment;
using TellmaEmailMessage = Tellma.Core.Abstractions.Email.EmailMessage;

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>Maps a platform message onto the Azure SDK's message model.</summary>
    internal static class AcsEmailMessageMapper
    {
        /// <summary>The header ACS reads a customer-supplied internet message id from.</summary>
        internal const string MessageIdHeaderName = "Message-Id";

        /// <summary>
        ///     The header that would make ACS validate — and reject on — a supplied message id. It is
        ///     deliberately never set: under the lenient default an invalid or duplicate id is
        ///     silently replaced with a generated one, so the worst case is an event that meters as
        ///     uncorrelated, where strict validation would instead turn a correlation nicety into a
        ///     rejected email.
        /// </summary>
        internal const string ValidateMessageIdHeaderName = "x-ms-acsemail-validate-message-id";

        /// <summary>Builds the SDK message.</summary>
        /// <param name="message">The platform message.</param>
        /// <param name="defaultFrom">The configured sender, used when the message carries none.</param>
        /// <param name="stampedMessageId">The message id that was stamped, or null when the message
        ///     is uncorrelated or the id would have been too long.</param>
        /// <returns>The SDK message.</returns>
        internal static AcsEmailMessage Map(
            TellmaEmailMessage message, TellmaEmailAddress defaultFrom, out string? stampedMessageId)
        {
            TellmaEmailAddress from = message.From ?? defaultFrom;

            EmailRecipients recipients = new(
                [.. message.To.Select(ToAddress)],
                [.. message.Cc.Select(ToAddress)],
                [.. message.Bcc.Select(ToAddress)]);

            EmailContent content = new(message.Subject)
            {
                PlainText = message.TextBody,
                Html = message.HtmlBody,
            };

            // The sender is a bare address and nothing else. ACS validates senderAddress against a
            // configured MailFrom address, so an RFC 5322 display-name form — quoted or not — is
            // refused with a 400 naming the property. The sender's display name is a property of the
            // MailFrom address on the domain resource, set there rather than per message, so a
            // display name on this address is deliberately dropped.
            AcsEmailMessage acsMessage = new(from.Address, recipients, content);

            if (message.ReplyTo is TellmaEmailAddress replyTo)
            {
                acsMessage.ReplyTo.Add(ToAddress(replyTo));
            }

            foreach (TellmaEmailAttachment attachment in message.Attachments)
            {
                AcsEmailAttachment acsAttachment = new(
                    attachment.FileName, attachment.ContentType, BinaryData.FromBytes(attachment.Content))
                {
                    ContentId = attachment.ContentId,
                };

                acsMessage.Attachments.Add(acsAttachment);
            }

            stampedMessageId = null;
            if (message.Correlation is EmailCorrelation correlation
                && AcsMessageIdCodec.TryEncode(
                    correlation, AcsMessageIdCodec.GetSendingDomain(from.Address), out string? messageId))
            {
                // Only correlated mail carries an id; everything else lets ACS generate its own.
                acsMessage.Headers[MessageIdHeaderName] = messageId;
                stampedMessageId = messageId;
            }

            return acsMessage;
        }

        private static AcsEmailAddress ToAddress(TellmaEmailAddress address)
        {
            return string.IsNullOrWhiteSpace(address.DisplayName)
                ? new AcsEmailAddress(address.Address)
                : new AcsEmailAddress(address.Address, address.DisplayName);
        }
    }
}
