// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Connector.AcsEmail.Adapter
{
    /// <summary>
    ///     Lowercase, unpadded base32hex (the RFC 4648 extended-hex alphabet), which the BCL does not
    ///     provide.
    /// </summary>
    /// <remarks>
    ///     Chosen for the message-id correlation channel because its alphabet is a legal RFC 5322
    ///     atom, so an encoded correlation survives an internet message id byte for byte. Neither
    ///     <c>-</c> nor <c>@</c> is in the alphabet, which is what makes the surrounding id format
    ///     unambiguous to split.
    /// </remarks>
    internal static class Base32Hex
    {
        private const string Alphabet = "0123456789abcdefghijklmnopqrstuv";

        /// <summary>Encodes bytes, most significant bit first, with no padding.</summary>
        /// <param name="bytes">The bytes to encode.</param>
        /// <returns>The lowercase base32hex text.</returns>
        internal static string Encode(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty)
            {
                return string.Empty;
            }

            int charCount = ((bytes.Length * 8) + 4) / 5;
            char[] encoded = new char[charCount];
            int bitBuffer = 0;
            int bitCount = 0;
            int written = 0;

            foreach (byte value in bytes)
            {
                bitBuffer = (bitBuffer << 8) | value;
                bitCount += 8;

                while (bitCount >= 5)
                {
                    bitCount -= 5;
                    encoded[written++] = Alphabet[(bitBuffer >> bitCount) & 0x1F];
                }
            }

            if (bitCount > 0)
            {
                // The final partial group is zero-padded on the right, which is what makes the
                // encoding unpadded yet still round-trip.
                encoded[written] = Alphabet[(bitBuffer << (5 - bitCount)) & 0x1F];
            }

            return new string(encoded);
        }

        /// <summary>Decodes base32hex text, tolerating either case.</summary>
        /// <param name="text">The text to decode.</param>
        /// <param name="bytes">The decoded bytes, or null when the text is not valid base32hex.</param>
        /// <returns>True when the text decoded.</returns>
        internal static bool TryDecode(ReadOnlySpan<char> text, out byte[]? bytes)
        {
            bytes = null;
            if (text.IsEmpty)
            {
                return false;
            }

            int byteCount = text.Length * 5 / 8;
            if (byteCount == 0)
            {
                return false;
            }

            byte[] decoded = new byte[byteCount];
            int bitBuffer = 0;
            int bitCount = 0;
            int written = 0;

            foreach (char c in text)
            {
                int value = DecodeChar(c);
                if (value < 0)
                {
                    return false;
                }

                bitBuffer = (bitBuffer << 5) | value;
                bitCount += 5;

                if (bitCount >= 8)
                {
                    bitCount -= 8;
                    decoded[written++] = (byte)((bitBuffer >> bitCount) & 0xFF);
                }
            }

            // Whatever is left over must be padding bits, and padding bits must be zero; anything
            // else means the text was not produced by this encoder.
            if (written != byteCount || (bitBuffer & ((1 << bitCount) - 1)) != 0)
            {
                return false;
            }

            bytes = decoded;
            return true;
        }

        private static int DecodeChar(char c)
        {
            return c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'a' and <= 'v' => c - 'a' + 10,
                >= 'A' and <= 'V' => c - 'A' + 10,
                _ => -1,
            };
        }
    }
}
