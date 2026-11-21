// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Localization;
using Tellma.Core.Abstractions.Notifications;
using Tellma.Core.Calendars;
using Tellma.Core.Localization;
using Tellma.Core.Notifications;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The email channel's rules with no database: the address a recipient is written to, the
    ///     subject's title line, and the subject cut to one line that fits its column.
    /// </summary>
    public sealed class EmailNotificationChannelTests
    {
        /// <summary>The localization every recipient here has; the rules ignore it.</summary>
        private static readonly LocalizationContext English = new(NegotiatedCultures.Get("en", "GB"), new GregorianCalendarSystem(), TimeZoneInfo.Utc, 1, [], "en", "en-GB");

        [Theory]
        [InlineData("ada@example.com", "contact@example.com", "contact@example.com")]
        [InlineData("ada@example.com", " ", "ada@example.com")]
        [InlineData("ada@example.com", null, "ada@example.com")]
        [InlineData(" ", null, null)]
        [InlineData(null, null, null)]
        public void A_recipient_is_written_to_their_contact_address_else_their_sign_in_address(string? email, string? contactEmail, string? expected)
        {
            Assert.Equal(expected, EmailNotificationChannel.Address(new NotificationRecipient(7, "Ada", email, contactEmail, null, English)));
        }

        [Theory]
        [InlineData("Shipment SO-7 shipped", "Shipment SO-7 shipped")]
        [InlineData("Shipment SO-7 shipped\nBy Ada, on 20 Nov", "Shipment SO-7 shipped")]
        [InlineData("", "")]
        public void The_subject_takes_the_notifications_title_line(string text, string title)
        {
            Assert.Equal(title, EmailNotificationChannel.Title(text));
        }

        [Fact]
        public void A_subject_is_one_line_and_one_that_is_too_long_is_cut_with_an_ellipsis()
        {
            Assert.Equal("Acme: a  b", EmailNotificationChannel.FitSubject(" Acme: a\r\tb "));

            string exact = new('s', 500);
            Assert.Equal(exact, EmailNotificationChannel.FitSubject(exact));

            string cut = EmailNotificationChannel.FitSubject(new string('s', 600));
            Assert.Equal(500, cut.Length);
            Assert.EndsWith("…", cut, StringComparison.Ordinal);
        }

        [Fact]
        public void A_cut_never_splits_a_surrogate_pair()
        {
            // The 499th and 500th characters are one emoji: the cut keeps neither half of it.
            string subject = new string('s', 498) + "\U0001F4E6" + new string('t', 10);

            string cut = EmailNotificationChannel.FitSubject(subject);

            Assert.Equal(new string('s', 498) + "…", cut);
            Assert.All(cut, static c => Assert.False(char.IsSurrogate(c)));
        }
    }
}
