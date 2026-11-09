// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging.Tests.Fixtures
{
    /// <summary>The CRC-32 of PNG chunks (ISO 3309, reflected polynomial 0xEDB88320).</summary>
    internal static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        /// <summary>Computes the CRC of <paramref name="bytes" />.</summary>
        /// <param name="bytes">The chunk type and data.</param>
        /// <returns>The CRC.</returns>
        public static uint Compute(ReadOnlySpan<byte> bytes)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in bytes)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc ^ 0xFFFFFFFFu;
        }

        /// <summary>Builds the byte-wise lookup table.</summary>
        /// <returns>The table.</returns>
        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
