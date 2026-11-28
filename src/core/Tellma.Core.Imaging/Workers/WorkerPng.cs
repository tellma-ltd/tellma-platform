// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;

namespace Tellma.Core.Imaging.Workers
{
    /// <summary>
    ///     Checks the PNG a worker answers with before any decoder sees it: the signature, a header
    ///     with the dimensions a page fitted to the thumbnail square must have, 8-bit RGB or RGBA
    ///     without interlacing, and nothing after it but the encoder's significant-bits chunk and
    ///     image data.
    /// </summary>
    /// <remarks>
    ///     The worker may have been compromised by the document it rendered, so its answer is
    ///     treated like an upload: the structure is checked here, in managed code, and the pixels
    ///     are then decoded by the same PNG decoder uploads go through, under a pixel cap the
    ///     thumbnail size sets. Only the chunks the worker's encoder writes are accepted, so an
    ///     answer cannot carry metadata, a colour profile or an orientation into the thumbnail.
    /// </remarks>
    internal static class WorkerPng
    {
        // The PNG signature, then each chunk's length, type and CRC around its data.
        private static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        private const int ChunkOverhead = 12;
        private const int HeaderLength = 13;
        private const uint HeaderType = 0x4948_4452; // IHDR
        private const uint DataType = 0x4944_4154; // IDAT
        private const uint EndType = 0x4945_4E44; // IEND
        private const uint SignificantBitsType = 0x7342_4954; // sBIT
        private const byte ColourTypeRgb = 2;
        private const byte ColourTypeRgba = 6;

        /// <summary>Checks a worker's PNG of a page fitted within <paramref name="size" />.</summary>
        /// <param name="png">The worker's answer.</param>
        /// <param name="size">The thumbnail square's side the request asked for.</param>
        /// <returns>The image's dimensions, which the decoder must then report too.</returns>
        /// <exception cref="PdfWorkerException">The answer is not such a PNG.</exception>
        public static (int Width, int Height) Check(ReadOnlySpan<byte> png, int size)
        {
            // Section: the signature and the header chunk, which must come first.
            if (!png.StartsWith(Signature) || png.Length < Signature.Length + ChunkOverhead + HeaderLength)
            {
                throw Malformed("The answer is not a PNG.");
            }

            ReadOnlySpan<byte> rest = png[Signature.Length..];
            if (BinaryPrimitives.ReadUInt32BigEndian(rest) != HeaderLength || BinaryPrimitives.ReadUInt32BigEndian(rest[4..]) != HeaderType)
            {
                throw Malformed("The PNG does not begin with its header.");
            }

            ReadOnlySpan<byte> header = rest.Slice(8, HeaderLength);
            uint width = BinaryPrimitives.ReadUInt32BigEndian(header);
            uint height = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
            (byte depth, byte colour, byte compression, byte filter, byte interlace) = (header[8], header[9], header[10], header[11], header[12]);

            // Section: a page is always fitted so that its longer side is exactly the thumbnail size.
            if (width < 1 || height < 1 || width > size || height > size || Math.Max(width, height) != size)
            {
                throw Malformed($"The PNG measures {width} x {height}; a page fitted to {size} must measure {size} on its longer side.");
            }

            if (depth != 8 || colour is not (ColourTypeRgb or ColourTypeRgba) || compression != 0 || filter != 0 || interlace != 0)
            {
                throw Malformed($"The PNG's format (depth {depth}, colour type {colour}, interlace {interlace}) is not the worker's.");
            }

            // Section: then, optionally, the significant-bits chunk the encoder writes (all channels
            // 8-bit), image data, and an empty end chunk with nothing after it.
            rest = rest[(ChunkOverhead + HeaderLength)..];
            bool data = false;
            while (true)
            {
                if (rest.Length < ChunkOverhead)
                {
                    throw Malformed("The PNG ends inside a chunk.");
                }

                uint length = BinaryPrimitives.ReadUInt32BigEndian(rest);
                uint type = BinaryPrimitives.ReadUInt32BigEndian(rest[4..]);
                if (length > rest.Length - ChunkOverhead)
                {
                    throw Malformed("A PNG chunk runs past the end of the answer.");
                }

                ReadOnlySpan<byte> content = rest.Slice(8, (int)length);
                switch (type)
                {
                    case EndType:
                        return length == 0 && rest.Length == ChunkOverhead && data
                            ? ((int)width, (int)height)
                            : throw Malformed("The PNG's end chunk is not its last, or it holds no image data.");

                    case DataType:
                        data = true;
                        break;

                    case SignificantBitsType when !data && length == (colour == ColourTypeRgba ? 4u : 3u) && !content.ContainsAnyExcept((byte)8):
                        break;

                    default:
                        throw Malformed($"The PNG holds a {Name(rest.Slice(4, 4))} chunk where only image data may be.");
                }

                rest = rest[(ChunkOverhead + (int)length)..];
            }
        }

        /// <summary>A chunk type as text for a log line: its letters, any other byte as a question mark.</summary>
        /// <param name="type">The four type bytes.</param>
        /// <returns>The name.</returns>
        private static string Name(ReadOnlySpan<byte> type)
        {
            Span<char> name = stackalloc char[type.Length];
            for (int i = 0; i < type.Length; i++)
            {
                name[i] = type[i] is (>= (byte)'A' and <= (byte)'Z') or (>= (byte)'a' and <= (byte)'z') ? (char)type[i] : '?';
            }

            return new string(name);
        }

        /// <summary>A protocol failure: the worker's answer is not the image it must be.</summary>
        /// <param name="detail">What is wrong.</param>
        /// <returns>The exception.</returns>
        private static PdfWorkerException Malformed(string detail)
        {
            return new PdfWorkerException(PdfWorkerFailure.Protocol, detail);
        }
    }
}
