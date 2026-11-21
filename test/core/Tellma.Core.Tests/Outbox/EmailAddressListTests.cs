// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;
using Tellma.Core.Outbox;

namespace Tellma.Core.Tests.Outbox
{
    /// <summary>
    ///     The stored address lists: each well-formed address formats and parses back unchanged, names
    ///     are quoted and escaped, an ambiguous bare address is bracketed, the hand-written forms parse,
    ///     and an address that could break a header is refused.
    /// </summary>
    public sealed class EmailAddressListTests
    {
        [Fact]
        public void A_list_is_written_as_quoted_names_and_bare_addresses_joined_by_commas()
        {
            string? text = EmailAddressList.Format([new EmailAddress("ada@example.com", "Ada Lovelace"), new EmailAddress("ben@example.com")]);

            Assert.Equal("\"Ada Lovelace\" <ada@example.com>, ben@example.com", text);
        }

        [Fact]
        public void An_empty_list_is_written_as_none_and_none_parses_as_empty()
        {
            Assert.Null(EmailAddressList.Format([]));
            Assert.Empty(EmailAddressList.Parse(null));
            Assert.Empty(EmailAddressList.Parse("  "));
        }

        [Theory]
        [InlineData("ada@example.com", null)]
        [InlineData("ada@example.com", "Ada \"The Countess\" Lovelace")]
        [InlineData("ada@example.com", "Back\\slash, comma; colon: (parens)")]
        [InlineData("ada@example.com", "أدا لوفلايس")]
        [InlineData("\"odd,local\"@example.com", null)]
        [InlineData("a(b)c@example.com", "Ada")]
        public void Every_well_formed_address_parses_back_unchanged(string address, string? name)
        {
            EmailAddress original = new(address, name);
            Assert.True(EmailAddressList.IsWellFormed(original));

            Assert.Equal([original, new EmailAddress("next@example.com")], EmailAddressList.Parse(EmailAddressList.Format([original, new EmailAddress("next@example.com")])));
        }

        [Fact]
        public void A_bare_address_a_list_would_misread_is_written_in_angle_brackets()
        {
            Assert.Equal("<a,b@example.com>", EmailAddressList.Format(new EmailAddress("a,b@example.com")));
        }

        [Fact]
        public void The_hand_written_forms_parse_and_a_blank_name_parses_as_none()
        {
            Assert.Equal(
                [new EmailAddress("ada@example.com", "Ada Lovelace"), new EmailAddress("ben@example.com"), new EmailAddress("cy@example.com")],
                EmailAddressList.Parse(" Ada Lovelace <ada@example.com> ,<ben@example.com>, \"  \" <cy@example.com>"));
        }

        [Theory]
        [InlineData("\"Ada <ada@example.com>")]
        [InlineData("<ada@example.com")]
        [InlineData("ada@example.com,")]
        [InlineData("\"Ada\" ada@example.com")]
        [InlineData("<>")]
        public void Malformed_text_is_refused(string text)
        {
            _ = Assert.Throws<FormatException>(() => EmailAddressList.Parse(text));
        }

        [Theory]
        [InlineData("", null)]
        [InlineData("ada @example.com", null)]
        [InlineData("ada@example.com\r\nBcc: eve@example.com", null)]
        [InlineData("<ada@example.com>", null)]
        [InlineData("ada@example.com", "Ada\r\nBcc: eve@example.com")]
        public void An_address_that_could_break_a_header_is_not_well_formed(string address, string? name)
        {
            Assert.False(EmailAddressList.IsWellFormed(new EmailAddress(address, name)));
        }
    }
}
