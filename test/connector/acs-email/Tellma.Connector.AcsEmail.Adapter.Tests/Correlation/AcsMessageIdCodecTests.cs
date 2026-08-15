// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using Tellma.Core.Abstractions.Email;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Correlation
{
    /// <summary>
    ///     The message id is the whole correlation channel on this transport, so it has to survive a
    ///     round trip byte for byte — and has to fail quietly on anything it did not produce.
    /// </summary>
    public class AcsMessageIdCodecTests
    {
        [Theory]
        [InlineData("outbox", "42", 3)]
        [InlineData("identity", "abc", null)]
        [InlineData("outbox", "invoice:2026:00042", 7)]
        [InlineData("outbox", "فاتورة-٤٢", 9)]
        [InlineData("outbox", "a b\tc\nd", 1)]
        public void Round_trips_a_correlation(string ownerKey, string reference, int? tenantId)
        {
            EmailCorrelation correlation = new(ownerKey, reference, tenantId);

            Assert.True(AcsMessageIdCodec.TryEncode(correlation, "tellma.com", out string? messageId));
            Assert.True(AcsMessageIdCodec.TryParse(messageId, out EmailCorrelation? parsed));
            Assert.Equal(correlation, parsed);
        }

        [Fact]
        public void Produces_a_message_id_shaped_the_way_rfc_5322_expects()
        {
            EmailCorrelation correlation = new("outbox", "42", 3);

            Assert.True(AcsMessageIdCodec.TryEncode(correlation, "tellma.com", out string? messageId));

            Assert.StartsWith("<tlm1-", messageId, StringComparison.Ordinal);
            Assert.EndsWith("@tellma.com>", messageId, StringComparison.Ordinal);
        }

        [Fact]
        public void Gives_every_attempt_a_different_id_for_the_same_correlation()
        {
            // Message ids must be unique per message while correlations repeat across resends; a
            // repeated id risks recipient-side threading and deduplication.
            EmailCorrelation correlation = new("outbox", "42", 3);

            HashSet<string> ids = new(StringComparer.Ordinal);
            for (int i = 0; i < 50; i++)
            {
                Assert.True(AcsMessageIdCodec.TryEncode(correlation, "tellma.com", out string? messageId));
                Assert.True(ids.Add(messageId));
            }
        }

        [Fact]
        public void Parses_an_id_whether_or_not_it_carries_angle_brackets()
        {
            EmailCorrelation correlation = new("outbox", "42", 3);
            Assert.True(AcsMessageIdCodec.TryEncode(correlation, "tellma.com", out string? bracketed));

            string bare = bracketed.Trim('<', '>');

            Assert.True(AcsMessageIdCodec.TryParse(bare, out EmailCorrelation? parsed));
            Assert.Equal(correlation, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        // What ACS generates when it replaces a supplied id, or when we never supplied one.
        [InlineData("<a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d@azurecomm.net>")]
        // Mail sent outside the platform through the same resource.
        [InlineData("<20260101.abcdef@mail.example.com>")]
        // A future encoding this build knows nothing about.
        [InlineData("<tlm2-0123456789-abcdefgh@tellma.com>")]
        // The right shape, but the payload is not base32hex.
        [InlineData("<tlm1-!!!!-abcdefgh@tellma.com>")]
        // The right shape, but the payload decodes to something that is not a correlation.
        [InlineData("<tlm1-c5h66p35cpjmgt10c5h66-abcdefgh@tellma.com>")]
        public void Resolves_anything_it_did_not_produce_to_no_correlation(string? messageId)
        {
            Assert.False(AcsMessageIdCodec.TryParse(messageId, out EmailCorrelation? parsed));
            Assert.Null(parsed);
        }

        [Fact]
        public void Refuses_to_stamp_an_id_that_would_be_over_the_length_budget()
        {
            // The correlation's own length bounds keep the encoded payload comfortably inside RFC
            // 5322's 998-character line limit, so the budget bites only on a pathological sending
            // domain — and a correlation is never worth a rejected email.
            EmailCorrelation correlation = new("outbox", new string('ح', EmailCorrelation.MaxReferenceLength), 3);
            string longDomain = new string('d', 500) + ".example.com";

            Assert.False(AcsMessageIdCodec.TryEncode(correlation, longDomain, out string? messageId));
            Assert.Null(messageId);
        }

        [Fact]
        public void Stamps_the_longest_correlation_the_contract_permits()
        {
            EmailCorrelation longest = new(
                new string('a', EmailCorrelation.MaxOwnerKeyLength),
                new string('ح', EmailCorrelation.MaxReferenceLength),
                int.MinValue);

            Assert.True(AcsMessageIdCodec.TryEncode(longest, "tellma.com", out string? messageId));
            Assert.True(AcsMessageIdCodec.TryParse(messageId, out EmailCorrelation? parsed));
            Assert.Equal(longest, parsed);
        }

        [Fact]
        public void Stays_inside_the_budget_for_an_ordinary_correlation()
        {
            EmailCorrelation ordinary = new("outbox", new string('9', 40), 1234);

            Assert.True(AcsMessageIdCodec.TryEncode(ordinary, "tellma.com", out string? messageId));
            Assert.True(messageId.Length <= AcsMessageIdCodec.MaxMessageIdLength);
        }

        [Theory]
        [InlineData("no-reply@tellma.com", "tellma.com")]
        [InlineData("no-reply@Tellma.COM", "tellma.com")]
        [InlineData("malformed", "localhost")]
        public void Takes_the_right_hand_side_from_the_sending_domain(string address, string expected)
        {
            // A message id that disagrees with the sending domain is a weak spam signal.
            Assert.Equal(expected, AcsMessageIdCodec.GetSendingDomain(address));
        }
    }
}
