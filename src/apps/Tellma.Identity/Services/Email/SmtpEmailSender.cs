// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using Tellma.Identity.Options;

namespace Tellma.Identity.Services.Email
{
    /// <summary>
    ///     The SMTP transport (MailKit): the built-in production sender, first-class for on-prem
    ///     deployments. A batch reuses one connection.
    /// </summary>
    /// <param name="options">The engine options (SMTP settings).</param>
    public sealed class SmtpEmailSender(IOptions<TellmaIdentityOptions> options) : IEmailSender
    {
        /// <inheritdoc />
        public async Task SendAsync(IReadOnlyList<EmailMessage> messages, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(messages);

            if (messages.Count == 0)
            {
                return;
            }

            TellmaIdentityEmailOptions email = options.Value.Email;

            using SmtpClient client = new();

            // SmtpHost presence is enforced by the options validator at startup.
            await client.ConnectAsync(
                email.SmtpHost!,
                email.SmtpPort,
                email.UseStartTls ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto,
                cancellationToken);

            if (!string.IsNullOrEmpty(email.Username))
            {
                await client.AuthenticateAsync(email.Username, email.Password ?? string.Empty, cancellationToken);
            }

            foreach (EmailMessage message in messages)
            {
                MimeMessage mime = new();
                mime.From.Add(new MailboxAddress(email.FromDisplayName, email.FromAddress));
                mime.To.Add(new MailboxAddress(message.ToDisplayName, message.ToEmail));
                mime.Subject = message.Subject;
                mime.Body = new TextPart("plain") { Text = message.TextBody };

                await client.SendAsync(mime, cancellationToken);
            }

            await client.DisconnectAsync(quit: true, cancellationToken);
        }
    }
}
