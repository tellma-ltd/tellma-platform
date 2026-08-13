// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography;

namespace Tellma.Connector.AcsEmail.Adapter.Tests.Correlation
{
    /// <summary>The codec the BCL does not provide, and the correlation channel depends on.</summary>
    public class Base32HexTests
    {
        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        [InlineData(5)]
        [InlineData(6)]
        [InlineData(7)]
        [InlineData(64)]
        [InlineData(255)]
        public void Round_trips_bytes_of_every_length_remainder(int length)
        {
            byte[] original = RandomNumberGenerator.GetBytes(length);

            string encoded = Base32Hex.Encode(original);

            if (length == 0)
            {
                Assert.Equal(string.Empty, encoded);
                return;
            }

            Assert.True(Base32Hex.TryDecode(encoded, out byte[]? decoded));
            Assert.Equal(original, decoded);
        }

        [Fact]
        public void Emits_only_the_lowercase_extended_hex_alphabet_and_no_padding()
        {
            string encoded = Base32Hex.Encode(RandomNumberGenerator.GetBytes(37));

            Assert.All(encoded, static c => Assert.True(c is (>= '0' and <= '9') or (>= 'a' and <= 'v')));
            Assert.DoesNotContain("=", encoded, StringComparison.Ordinal);
        }

        [Fact]
        public void Decodes_uppercase_input_too_because_parsing_is_tolerant()
        {
            byte[] original = RandomNumberGenerator.GetBytes(11);
            string encoded = Base32Hex.Encode(original);

            Assert.True(Base32Hex.TryDecode(encoded.ToUpperInvariant(), out byte[]? decoded));
            Assert.Equal(original, decoded);
        }

        [Theory]
        [InlineData("")]
        [InlineData("w")]
        [InlineData("0!")]
        [InlineData("0")]
        public void Refuses_input_it_could_not_have_produced(string text)
        {
            Assert.False(Base32Hex.TryDecode(text, out byte[]? decoded));
            Assert.Null(decoded);
        }

        [Fact]
        public void Refuses_a_final_group_whose_padding_bits_are_not_zero()
        {
            // One byte encodes to two characters with two padding bits, which the encoder always
            // leaves zero. Text whose padding bits are set was not produced here, and silently
            // accepting it would break the round trip.
            byte[] original = [0x07];
            string valid = Base32Hex.Encode(original);

            Assert.Equal(2, valid.Length);
            Assert.True(Base32Hex.TryDecode(valid, out byte[]? decoded));
            Assert.Equal(original, decoded);

            Assert.False(Base32Hex.TryDecode("01", out _));
            Assert.False(Base32Hex.TryDecode("0v", out _));
        }
    }
}
