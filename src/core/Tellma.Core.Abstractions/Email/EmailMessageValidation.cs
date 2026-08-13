// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Diagnostics.CodeAnalysis;

namespace Tellma.Core.Abstractions.Email
{
    /// <summary>
    ///     The structural validity check every <see cref="IEmailSender" /> applies before touching
    ///     the wire, so that one malformed message in a bulk dispatch is rejected on its own instead
    ///     of failing the batch.
    /// </summary>
    /// <remarks>
    ///     Deliberately structural only — it answers "could any transport possibly send this?", not
    ///     "will this particular transport accept it". Address syntax, recipient counts, and size
    ///     limits are transport-specific and stay with the adapter that knows them.
    /// </remarks>
    public static class EmailMessageValidation
    {
        /// <summary>
        ///     Reports whether a message is structurally sendable, and why not when it is not.
        /// </summary>
        /// <param name="message">The message to check.</param>
        /// <param name="error">A human-readable reason, suitable for an
        ///     <see cref="EmailSendResult.Error" />, or null when the message is valid.</param>
        /// <returns>True when the message is structurally valid.</returns>
        public static bool TryValidate(EmailMessage message, [NotNullWhen(false)] out string? error)
        {
            ArgumentNullException.ThrowIfNull(message);

            if (message.To.Count == 0)
            {
                error = "The message has no recipients.";
                return false;
            }

            // Recipient lists first: a blank address is the single most common malformed row in a
            // bulk dispatch, and naming the list makes the offending column obvious.
            if (!TryValidateAddresses(message.To, "To", out error)
                || !TryValidateAddresses(message.Cc, "Cc", out error)
                || !TryValidateAddresses(message.Bcc, "Bcc", out error)
                || !TryValidateOptionalAddress(message.From, "From", out error)
                || !TryValidateOptionalAddress(message.ReplyTo, "ReplyTo", out error))
            {
                return false;
            }

            // Subject and TextBody are `required`, so these only fire when a caller has explicitly
            // written a null through a null-forgiving assignment.
            if (message.Subject is null)
            {
                error = "The message has no subject.";
                return false;
            }

            if (message.TextBody is null)
            {
                error = "The message has no plain-text body.";
                return false;
            }

            foreach (EmailAttachment attachment in message.Attachments)
            {
                if (attachment is null)
                {
                    error = "The message has a null attachment.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(attachment.FileName))
                {
                    error = "The message has an attachment with no file name.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(attachment.ContentType))
                {
                    error = $"Attachment '{attachment.FileName}' has no content type.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool TryValidateAddresses(
            IReadOnlyList<EmailAddress> addresses, string listName, [NotNullWhen(false)] out string? error)
        {
            if (addresses is null)
            {
                error = $"The message's {listName} list is null.";
                return false;
            }

            foreach (EmailAddress address in addresses)
            {
                if (!TryValidateAddress(address, listName, out error))
                {
                    return false;
                }
            }

            error = null;
            return true;
        }

        private static bool TryValidateAddress(
            EmailAddress? address, string listName, [NotNullWhen(false)] out string? error)
        {
            if (address is null)
            {
                error = $"The message's {listName} list contains a null address.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(address.Address))
            {
                error = $"The message's {listName} list contains a blank address.";
                return false;
            }

            error = null;
            return true;
        }

        // From and ReplyTo are legitimately absent — the transport's configured default stands in
        // for From — so only a present-but-blank address is a defect.
        private static bool TryValidateOptionalAddress(
            EmailAddress? address, string slotName, [NotNullWhen(false)] out string? error)
        {
            if (address is not null && string.IsNullOrWhiteSpace(address.Address))
            {
                error = $"The message's {slotName} address is blank.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
