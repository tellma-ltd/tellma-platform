// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Tellma.Distro.Acme.Development
{
    // Spec 0018 §6.3 gives some sample customers images without saying where their bytes come from.
    // They are drawn here rather than shipped as files: a plain PNG encoder over a generated picture
    // keeps the library free of binary assets and of an imaging dependency, and the platform's image
    // processor normalises the PNG to the avatar's format as it does any upload.
    /// <summary>
    ///     Draws the sample customers' pictures: a square PNG of a light silhouette, a head over
    ///     shoulders, on a background colour that varies with the picture's number.
    /// </summary>
    internal static class SamplePortraits
    {
        /// <summary>The side of every picture, in pixels.</summary>
        public const int Size = 96;

        /// <summary>The backgrounds, chosen in turn by the picture's number.</summary>
        private static readonly (byte Red, byte Green, byte Blue)[] Backgrounds =
        [
            (0x2E, 0x5E, 0xAA), (0x1F, 0x8A, 0x70), (0xC0, 0x5A, 0x2B), (0x7B, 0x4B, 0x94),
            (0x2B, 0x7A, 0x9E), (0xA3, 0x3B, 0x5A), (0x5C, 0x7A, 0x29), (0x8A, 0x6D, 0x1F),
        ];

        /// <summary>The silhouette's colour.</summary>
        private static readonly (byte Red, byte Green, byte Blue) Silhouette = (0xF4, 0xF4, 0xF4);

        /// <summary>The PNG chunk checksums' table, the CRC-32 of each byte value.</summary>
        private static readonly uint[] CrcTable = BuildCrcTable();

        /// <summary>Draws one picture as a PNG: 8-bit RGB, no interlacing, every row unfiltered.</summary>
        /// <param name="number">The picture's number, which picks its background.</param>
        /// <returns>The PNG's bytes.</returns>
        public static byte[] Png(int number)
        {
            (byte Red, byte Green, byte Blue) background = Backgrounds[(int)((uint)number % (uint)Backgrounds.Length)];

            // --- The scanlines: a filter byte of 0, then three bytes per pixel ---
            const int Stride = 1 + (Size * 3);
            byte[] pixels = new byte[Size * Stride];
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    (byte red, byte green, byte blue) = InSilhouette(x, y) ? Silhouette : background;
                    int at = (y * Stride) + 1 + (x * 3);
                    pixels[at] = red;
                    pixels[at + 1] = green;
                    pixels[at + 2] = blue;
                }
            }

            // --- The image data, deflated in a zlib wrapper as the format requires ---
            byte[] data;
            using (MemoryStream deflated = new())
            {
                using (ZLibStream zlib = new(deflated, CompressionLevel.Optimal, leaveOpen: true))
                {
                    zlib.Write(pixels);
                }

                data = deflated.ToArray();
            }

            // --- The file: the signature, then the header, data and end chunks ---
            byte[] header = new byte[13];
            BinaryPrimitives.WriteInt32BigEndian(header, Size);
            BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), Size);
            header[8] = 8; // bit depth
            header[9] = 2; // colour type: RGB

            using MemoryStream png = new();
            png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
            WriteChunk(png, "IHDR", header);
            WriteChunk(png, "IDAT", data);
            WriteChunk(png, "IEND", []);
            return png.ToArray();
        }

        /// <summary>Whether a pixel falls on the silhouette: a disc for the head, half an ellipse for the shoulders.</summary>
        /// <param name="x">The pixel's column.</param>
        /// <param name="y">The pixel's row.</param>
        /// <returns>True on the silhouette.</returns>
        private static bool InSilhouette(int x, int y)
        {
            // Centred horizontally; the head above the middle, the shoulders rising from the bottom edge.
            double dx = x - (Size / 2.0) + 0.5;
            double head = (dx * dx) + Math.Pow(y - (Size * 0.38), 2);
            double shoulders = Math.Pow(dx / (Size * 0.36), 2) + Math.Pow((y - Size) / (Size * 0.32), 2);
            return head <= Math.Pow(Size * 0.19, 2) || shoulders <= 1;
        }

        /// <summary>Writes one chunk: its length, its type, its data and the checksum over type and data.</summary>
        /// <param name="png">The file being written.</param>
        /// <param name="type">The four-letter chunk type.</param>
        /// <param name="data">The chunk's data.</param>
        private static void WriteChunk(MemoryStream png, string type, ReadOnlySpan<byte> data)
        {
            Span<byte> word = stackalloc byte[4];
            byte[] name = Encoding.ASCII.GetBytes(type);
            BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
            png.Write(word);
            png.Write(name);
            png.Write(data);

            uint crc = Update(0xFFFF_FFFFu, name);
            crc = Update(crc, data) ^ 0xFFFF_FFFFu;
            BinaryPrimitives.WriteUInt32BigEndian(word, crc);
            png.Write(word);
        }

        /// <summary>Runs the CRC-32 over more bytes.</summary>
        /// <param name="crc">The running value.</param>
        /// <param name="bytes">The bytes.</param>
        /// <returns>The new running value.</returns>
        private static uint Update(uint crc, ReadOnlySpan<byte> bytes)
        {
            foreach (byte value in bytes)
            {
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        /// <summary>Builds the CRC-32 table of the reflected polynomial the PNG format uses.</summary>
        /// <returns>The table.</returns>
        private static uint[] BuildCrcTable()
        {
            uint[] table = new uint[256];
            for (uint n = 0; n < table.Length; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB8_8320u ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
