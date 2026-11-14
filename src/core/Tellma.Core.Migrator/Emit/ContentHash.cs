// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Security.Cryptography;

namespace Tellma.Core.Migrator.Emit
{
    /// <summary>
    ///     The hash in an emitted file's name: the first characters of the SHA-256 of its bytes in
    ///     Crockford's base32 (digits and uppercase letters without I, L, O and U), so the name
    ///     changes exactly when the bytes do and reads unambiguously.
    /// </summary>
    internal static class ContentHash
    {
        /// <summary>How many characters a name carries: 60 bits of the digest.</summary>
        public const int Length = 12;

        /// <summary>Crockford's base32 alphabet.</summary>
        private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

        /// <summary>The hash of a file's bytes.</summary>
        /// <param name="content">The bytes.</param>
        /// <returns>The first <see cref="Length" /> base32 characters of their SHA-256.</returns>
        public static string Of(ReadOnlySpan<byte> content)
        {
            Span<byte> digest = stackalloc byte[SHA256.HashSizeInBytes];
            _ = SHA256.HashData(content, digest);
            return Encode(digest)[..Length];
        }

        /// <summary>Encodes bytes in Crockford's base32, most significant bit first, the last group padded with zero bits.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The text, without padding characters.</returns>
        public static string Encode(ReadOnlySpan<byte> bytes)
        {
            char[] text = new char[((bytes.Length * 8) + 4) / 5];
            int buffer = 0;
            int bits = 0;
            int next = 0;

            // --- Five bits at a time, from a buffer refilled a byte at a time ---
            foreach (byte value in bytes)
            {
                // Fewer than 13 bits are ever pending, so 16 bits hold them all.
                buffer = ((buffer << 8) | value) & 0xFFFF;
                bits += 8;
                while (bits >= 5)
                {
                    bits -= 5;
                    text[next++] = Alphabet[(buffer >> bits) & 0x1F];
                }
            }

            // --- The remaining bits, padded on the right ---
            if (bits > 0)
            {
                text[next++] = Alphabet[(buffer << (5 - bits)) & 0x1F];
            }

            return new string(text, 0, next);
        }
    }
}
