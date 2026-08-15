// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using MailKit.Security;
using MimeKit;
using Tellma.Connector.Smtp.Adapter.Tests.Infrastructure;
using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.Smtp.Adapter.Tests.Protocol
{
    /// <summary>
    ///     The adapter against a real SMTP endpoint: what actually goes on the wire, and what the
    ///     server's reply codes turn into.
    /// </summary>
    public class SmtpProtocolTests
    {
        [Fact]
        public async Task Sends_a_full_feature_message_as_correct_mime()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            EmailMessage message = new()
            {
                To = [new EmailAddress("recipient@example.com", "الشيخ محمد")],
                Cc = [new EmailAddress("cc@example.com")],
                ReplyTo = new EmailAddress("support@tellma.com"),
                Subject = "فاتورة رقم ٤٢",
                TextBody = "Plain body",
                HtmlBody = "<p>HTML body <img src=\"cid:logo\" /></p>",
                Audience = EmailAudience.External,
                Attachments =
                [
                    new EmailAttachment("logo.png", "image/png", new byte[] { 1, 2, 3 }, "logo"),
                    new EmailAttachment("invoice.pdf", "application/pdf", new byte[] { 4, 5 }),
                ],
            };

            IReadOnlyList<EmailSendResult> results =
                await sender.SendAsync([message], TestContext.Current.CancellationToken);

            EmailSendResult result = Assert.Single(results);
            Assert.Equal(EmailSendOutcome.Sent, result.Outcome);

            MimeMessage received = Assert.Single(server.Store.Messages);

            // The configured sender stands in when the message carries none.
            Assert.Equal("no-reply@tellma.com", received.From.Mailboxes.Single().Address);
            Assert.Equal("Tellma", received.From.Mailboxes.Single().Name);
            Assert.Equal("support@tellma.com", received.ReplyTo.Mailboxes.Single().Address);

            // Non-ASCII survives the round trip through RFC 2047 encoding.
            Assert.Equal("فاتورة رقم ٤٢", received.Subject);
            Assert.Equal("الشيخ محمد", received.To.Mailboxes.Single().Name);

            Assert.Equal("Plain body", received.TextBody);
            Assert.Contains("HTML body", received.HtmlBody, StringComparison.Ordinal);

            // The inline resource is a linked resource of the HTML part, which is what makes cid:
            // resolve in a mail client; the other attachment is an ordinary one.
            MimePart inline = Assert.Single(
                received.BodyParts.OfType<MimePart>(), static p => p.ContentId == "logo");
            Assert.Equal("logo.png", inline.FileName);
            Assert.Contains(received.Attachments.OfType<MimePart>(), static a => a.FileName == "invoice.pdf");

            // The reported provider id is the message id a receiving server's logs can be searched by.
            Assert.Equal(received.MessageId, result.ProviderMessageId);
        }

        [Fact]
        public async Task Sends_a_text_only_message_without_inventing_an_html_part()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            await sender.SendAsync([SmtpSenderFactory.Message("plain")], TestContext.Current.CancellationToken);

            MimeMessage received = Assert.Single(server.Store.Messages);
            Assert.Null(received.HtmlBody);
            Assert.Equal("Body", received.TextBody);
        }

        [Fact]
        public async Task Maps_a_transient_refusal_and_keeps_the_rest_of_the_batch_going()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            server.Store.RespondTo("refused", 451, "4.7.1 greylisted, try again later");

            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [SmtpSenderFactory.Message("first"), SmtpSenderFactory.Message("refused"), SmtpSenderFactory.Message("third")],
                TestContext.Current.CancellationToken);

            Assert.Equal(EmailSendOutcome.Sent, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.TransientFailure, results[1].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[2].Outcome);

            // One malformed or refused row must not poison a bulk dispatch.
            Assert.Contains("451", results[1].Error, StringComparison.Ordinal);
            Assert.Equal(3, server.Store.Messages.Count);
        }

        [Fact]
        public async Task Maps_a_permanent_refusal_to_a_rejection()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            server.Store.RespondTo("refused", 550, "5.1.1 unknown user");

            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [SmtpSenderFactory.Message("refused")], TestContext.Current.CancellationToken);

            EmailSendResult result = Assert.Single(results);
            Assert.Equal(EmailSendOutcome.Rejected, result.Outcome);
            Assert.Contains("550", result.Error, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Authenticates_when_credentials_are_configured()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port, "mailer"));

            await sender.SendAsync([SmtpSenderFactory.Message("authenticated")], TestContext.Current.CancellationToken);

            Assert.Equal(("mailer", "secret"), server.Authenticator.LastCredentials);
        }

        [Fact]
        public async Task Throws_when_authentication_fails_because_nothing_was_attempted()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            server.Authenticator.RefuseEveryone = true;

            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port, "mailer"));

            // The specific exception matters: an assertion on any Exception would also pass if the
            // adapter had failed for an unrelated reason, such as never reaching the server at all.
            await Assert.ThrowsAsync<AuthenticationException>(
                () => sender.SendAsync([SmtpSenderFactory.Message("never-sent")], TestContext.Current.CancellationToken));

            // The caller knows the whole batch can be retried safely.
            Assert.Empty(server.Store.Messages);
        }

        [Fact]
        public async Task Fails_closed_when_the_server_cannot_offer_tls()
        {
            // StartTls is the default precisely because it refuses to fall back to plaintext: a
            // credentialed submission that silently downgraded would be worse than a failed one.
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(
                SmtpSenderFactory.Options(server.Port, secureSocket: SecureSocketOptions.StartTls));

            // NotSupportedException specifically: MailKit raises it when the server advertises no
            // STARTTLS and the client refuses to continue. Asserting on any Exception would pass
            // just as happily if the client had never connected, which proves nothing about the
            // downgrade behaviour under test.
            NotSupportedException failure = await Assert.ThrowsAsync<NotSupportedException>(
                () => sender.SendAsync([SmtpSenderFactory.Message("never-sent")], TestContext.Current.CancellationToken));

            Assert.Contains("STARTTLS", failure.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(server.Store.Messages);
        }

        [Fact]
        public async Task Rejects_a_malformed_address_without_touching_the_wire()
        {
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            EmailMessage malformed = SmtpSenderFactory.Message("malformed") with
            {
                To = [new EmailAddress("not an address")],
            };

            IReadOnlyList<EmailSendResult> results = await sender.SendAsync(
                [malformed, SmtpSenderFactory.Message("valid")], TestContext.Current.CancellationToken);

            // MailKit would throw while building the MIME message, which would take the whole batch
            // down; rejecting the one message is what the contract asks for.
            Assert.Equal(EmailSendOutcome.Rejected, results[0].Outcome);
            Assert.Equal(EmailSendOutcome.Sent, results[1].Outcome);
            Assert.Single(server.Store.Messages);
        }

        [Fact]
        public async Task Never_expects_delivery_events()
        {
            // SMTP's only feedback is the synchronous accept, which means the smarthost took
            // responsibility — not that anything was delivered.
            await using InProcessSmtpServer server = await InProcessSmtpServer.StartAsync();
            IEmailSender sender = SmtpSenderFactory.Sender(SmtpSenderFactory.Options(server.Port));

            EmailMessage correlated = SmtpSenderFactory.Message("correlated") with
            {
                Correlation = new EmailCorrelation("outbox", "42", 3),
            };

            IReadOnlyList<EmailSendResult> results =
                await sender.SendAsync([correlated], TestContext.Current.CancellationToken);

            Assert.False(Assert.Single(results).ExpectsDeliveryEvents);
        }
    }
}
