// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Abstractions.Email;
using Tellma.Core.Abstractions.Errors;
using Tellma.Core.Abstractions.Validation;
using Tellma.Core.Outbox;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The outbox's judgement of each request with no database: a malformed message refused alone
    ///     at its index, the recipient and size limits reported together with their arguments, the
    ///     size counting the bodies, the message's files and the resolved blobs, and the pending row
    ///     each request becomes.
    /// </summary>
    public sealed class EmailOutboxValidationTests
    {
        /// <summary>The path of the second request of a call.</summary>
        private static readonly ValidationPath Second = ValidationPath.Root.Index(1);

        [Fact]
        public void A_well_formed_request_within_the_limits_has_no_errors()
        {
            Assert.Empty(Errors(Request(Message())));
        }

        public static TheoryData<string, EmailEnqueueRequest> Malformed => new()
        {
            { "no primary recipient", Request(Message() with { To = [] }) },
            { "a blank address", Request(Message() with { Cc = [new EmailAddress(" ")] }) },
            { "white space in an address", Request(Message() with { Bcc = [new EmailAddress("eve @example.com")] }) },
            { "a line break in a name", Request(Message() with { To = [new EmailAddress("ada@example.com", "Ada\nBcc: eve@example.com")] }) },
            { "a sender too long to store", Request(Message() with { From = new EmailAddress(new string('a', 500) + "@example.com", "Sender") }) },
            { "a blank subject", Request(Message() with { Subject = " " }) },
            { "a subject on two lines", Request(Message() with { Subject = "Hello\r\nBcc: eve@example.com" }) },
            { "a subject too long to store", Request(Message() with { Subject = new string('s', 501) }) },
            { "a blank text body", Request(Message() with { TextBody = "" }) },
            { "an unnamed attachment", Request(Message() with { Attachments = [new EmailAttachment("", BlobContentTypes.Png, new byte[4])] }) },
            { "a content id too long to store", Request(Message() with { Attachments = [new EmailAttachment("a.png", BlobContentTypes.Png, new byte[4], new string('c', 256))] }) },
            { "more attachments than a row holds", Request(Message() with { Attachments = [.. Enumerable.Range(0, 21).Select(static index => new EmailAttachment($"{index}.png", BlobContentTypes.Png, new byte[4]))] }) },
            { "a blob reference with no kind", new EmailEnqueueRequest(Message(), [new EmailBlobAttachment(" ", 7)]) },
            { "a regarding resource of other characters", new EmailEnqueueRequest(Message(), RegardingEntity: "acme/SalesInvoice") },
            { "a regarding resource too long to store", new EmailEnqueueRequest(Message(), RegardingEntity: new string('r', 129)) },
        };

        [Theory]
        [MemberData(nameof(Malformed))]
        public void A_malformed_message_is_refused_at_its_index_and_judged_no_further(string because, EmailEnqueueRequest request)
        {
            // Every malformed case also breaks the recipient limit, which is not reported beside it.
            EmailEnqueueRequest crowded = request with { Message = request.Message with { Bcc = [.. request.Message.Bcc, .. Many(60)] } };

            ValidationError error = Assert.Single(Errors(crowded));
            Assert.True(error.Code == OutgoingEmailCodes.InvalidMessage, because);
            Assert.Equal(("[1]", 0), (error.Path.ToString(), error.Arguments.Count));
        }

        [Fact]
        public void Too_many_recipients_and_too_many_bytes_are_reported_together_with_their_counts()
        {
            OutboxOptions limits = new() { MaxRecipients = 3, MaxMessageBytes = 1024 };
            EmailEnqueueRequest request = Request(Message() with
            {
                Cc = [.. Many(2)],
                Bcc = [.. Many(1)],
                Attachments = [new EmailAttachment("a.png", BlobContentTypes.Png, new byte[1000])],
            });

            List<ValidationError> errors = Errors(request, [Download(100)], limits);

            Assert.Equal([OutgoingEmailCodes.TooManyRecipients, OutgoingEmailCodes.TooLarge], errors.Select(static error => error.Code));
            Assert.All(errors, static error => Assert.Equal("[1]", error.Path.ToString()));
            Assert.Equal((4, 3), ((int)errors[0].Arguments[OutgoingEmailCodes.CountArgument]!, (int)errors[0].Arguments[OutgoingEmailCodes.MaxArgument]!));
            long size = EmailOutbox.BodyBytes(request.Message) + 1000 + 100;
            Assert.Equal((size, 1024L), ((long)errors[1].Arguments[OutgoingEmailCodes.SizeArgument]!, (long)errors[1].Arguments[OutgoingEmailCodes.MaxBytesArgument]!));
        }

        [Fact]
        public void The_size_counts_both_bodies_as_utf8_and_the_limit_itself_is_allowed()
        {
            EmailMessage message = Message() with { TextBody = "مرحبا", HtmlBody = "<p>é</p>" };
            Assert.Equal(10 + 9, EmailOutbox.BodyBytes(message));

            Assert.Empty(Errors(Request(message), [Download(1024 - 19)], new OutboxOptions { MaxMessageBytes = 1024 }));
            Assert.Equal(OutgoingEmailCodes.TooLarge, Assert.Single(Errors(Request(message), [Download(1024 - 18)], new OutboxOptions { MaxMessageBytes = 1024 })).Code);
        }

        [Fact]
        public void A_request_becomes_a_pending_row_with_its_addresses_stored_as_lists()
        {
            DateTimeOffset after = new(2026, 11, 20, 9, 0, 0, TimeSpan.Zero);
            EmailEnqueueRequest request = new(
                Message() with
                {
                    From = new EmailAddress("noreply@example.com", "Acme"),
                    ReplyTo = new EmailAddress("help@example.com"),
                    Cc = [new EmailAddress("cc@example.com")],
                    HtmlBody = "<p>Hi</p>",
                    Audience = EmailAudience.External,
                },
                RecipientUserId: 7,
                RegardingEntity: "acme.SalesInvoice",
                RegardingId: 42,
                SendAfter: after);

            OutgoingEmail row = EmailOutbox.Row(request, attachments: []);

            Assert.Equal(
                (OutgoingEmailState.Pending, EmailAudience.External, "\"Acme\" <noreply@example.com>", "help@example.com", "\"Ada\" <ada@example.com>", "cc@example.com", null),
                (row.State, row.Audience, row.From, row.ReplyTo, row.To, row.Cc, row.Bcc));
            Assert.Equal(("Hello", "Hello, Ada.", "<p>Hi</p>"), (row.Subject, row.TextBody, row.HtmlBody));
            Assert.Equal((7, "acme.SalesInvoice", 42L, after), (row.RecipientUserId, row.RegardingEntity, row.RegardingId, row.SendAfter));
            Assert.Null(row.OutgoingEmailAttachments);
            Assert.Null(row.JobId);
        }

        /// <summary>A well-formed internal message to Ada.</summary>
        /// <returns>The message.</returns>
        private static EmailMessage Message()
        {
            return new EmailMessage
            {
                To = [new EmailAddress("ada@example.com", "Ada")],
                Subject = "Hello",
                TextBody = "Hello, Ada.",
                Audience = EmailAudience.Internal,
            };
        }

        /// <summary>A request of a message, attaching nothing by reference.</summary>
        /// <param name="message">The message.</param>
        /// <returns>The request.</returns>
        private static EmailEnqueueRequest Request(EmailMessage message)
        {
            return new EmailEnqueueRequest(message);
        }

        /// <summary>Distinct well-formed addresses.</summary>
        /// <param name="count">How many.</param>
        /// <returns>The addresses.</returns>
        private static IEnumerable<EmailAddress> Many(int count)
        {
            return Enumerable.Range(0, count).Select(static index => new EmailAddress($"r{index}@example.com"));
        }

        /// <summary>A resolved committed attachment of a size.</summary>
        /// <param name="size">Its stored size.</param>
        /// <returns>The download.</returns>
        private static BlobDownload Download(long size)
        {
            return new BlobDownload(9, OutgoingEmailAttachment.BlobKind, BlobState.Committed, "email-attachment/k", BlobContentTypes.Pdf, size, "a.pdf", IsImage: false, "\"e\"");
        }

        /// <summary>Validates one request as the second of a call.</summary>
        /// <param name="request">The request.</param>
        /// <param name="resolved">Its blob attachments as resolved; none by default.</param>
        /// <param name="limits">The limits; the defaults by default.</param>
        /// <returns>The errors.</returns>
        private static List<ValidationError> Errors(EmailEnqueueRequest request, BlobDownload?[]? resolved = null, OutboxOptions? limits = null)
        {
            List<ValidationError> errors = [];
            EmailOutbox.Validate(request, resolved ?? [], Second, limits ?? new OutboxOptions(), errors);
            return errors;
        }
    }
}
