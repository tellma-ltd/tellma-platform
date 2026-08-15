// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Core.Abstractions.Tests.Email
{
    /// <summary>
    ///     Every transport shares this check, so what counts as structurally invalid — and therefore
    ///     rejected on its own rather than failing a batch — is pinned once.
    /// </summary>
    public class EmailMessageValidationTests
    {
        [Fact]
        public void Accepts_a_minimal_message()
        {
            Assert.True(EmailMessageValidation.TryValidate(Minimal(), out string? error));
            Assert.Null(error);
        }

        [Fact]
        public void Rejects_a_message_with_no_recipients()
        {
            Assert.False(EmailMessageValidation.TryValidate(Minimal() with { To = [] }, out string? error));
            Assert.NotNull(error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Rejects_a_blank_recipient_address(string address)
        {
            EmailMessage message = Minimal() with { To = [new EmailAddress(address)] };

            Assert.False(EmailMessageValidation.TryValidate(message, out string? error));
            Assert.NotNull(error);
        }

        [Fact]
        public void Rejects_a_blank_carbon_copy_address()
        {
            EmailMessage message = Minimal() with { Cc = [new EmailAddress(" ")] };

            Assert.False(EmailMessageValidation.TryValidate(message, out _));
        }

        [Fact]
        public void Accepts_an_absent_sender_because_the_transport_default_stands_in()
        {
            Assert.True(EmailMessageValidation.TryValidate(Minimal() with { From = null }, out _));
        }

        [Fact]
        public void Rejects_a_present_but_blank_sender()
        {
            EmailMessage message = Minimal() with { From = new EmailAddress("  ") };

            Assert.False(EmailMessageValidation.TryValidate(message, out _));
        }

        [Fact]
        public void Rejects_an_attachment_with_no_file_name_or_content_type()
        {
            EmailMessage noName = Minimal() with
            {
                Attachments = [new EmailAttachment(" ", "application/pdf", new byte[] { 1 })],
            };
            EmailMessage noType = Minimal() with
            {
                Attachments = [new EmailAttachment("a.pdf", "", new byte[] { 1 })],
            };

            Assert.False(EmailMessageValidation.TryValidate(noName, out _));
            Assert.False(EmailMessageValidation.TryValidate(noType, out _));
        }

        private static EmailMessage Minimal()
        {
            return new EmailMessage
            {
                To = [new EmailAddress("recipient@example.com")],
                Subject = "Subject",
                TextBody = "Body",
                Audience = EmailAudience.Internal,
            };
        }
    }
}
