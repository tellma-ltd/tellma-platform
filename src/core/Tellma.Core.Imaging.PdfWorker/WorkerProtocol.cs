// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;

namespace Tellma.Core.Imaging.PdfWorker
{
    /// <summary>What the worker reports it enforces on itself, in its ready frame.</summary>
    [Flags]
    internal enum WorkerCapabilities
    {
        /// <summary>Nothing beyond what the host enforces from outside.</summary>
        None = 0,

        /// <summary>
        ///     A kernel system-call filter refuses sockets, new programs and processes, access to
        ///     other processes, and the kernel interfaces exploits favour.
        /// </summary>
        SystemCallFilter = 1,
    }

    /// <summary>How the worker answered a request.</summary>
    internal enum WorkerStatus
    {
        /// <summary>The payload is a PNG of page 1.</summary>
        Rendered = 0,

        /// <summary>The PDF cannot be rendered within the caps; the payload is a UTF-8 detail.</summary>
        Refused = 1,

        /// <summary>The worker itself failed, out of memory for example; the payload is a UTF-8 detail.</summary>
        Failed = 2,
    }

    /// <summary>One render request: the thumbnail size and the PDF.</summary>
    /// <param name="Size">The square's side, in pixels, that page 1 is fitted to.</param>
    /// <param name="Pdf">The document.</param>
    internal readonly record struct WorkerRequest(int Size, byte[] Pdf);

    /// <summary>The worker's answer to a request.</summary>
    /// <param name="Status">How it answered.</param>
    /// <param name="Payload">The PNG, or the UTF-8 detail of a refusal or failure.</param>
    internal readonly record struct WorkerResult(WorkerStatus Status, byte[] Payload);

    /// <summary>
    ///     The wire format between the image processor and its PDF worker: three length-prefixed,
    ///     size-capped frames over the worker's standard input and output, little-endian.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The worker writes a ready frame once its runtime has started and its sandbox is in
    ///         place, before it reads anything of the upload; the host applies its limits to the
    ///         worker then, and only then writes the request. The worker answers with one result
    ///         frame and exits.
    ///     </para>
    ///     <para>
    ///         Either side refuses a frame that does not begin with its magic number or declares a
    ///         length beyond its cap, before allocating for it: the host treats the worker as
    ///         compromised by the document it is rendering, so nothing it writes is trusted until
    ///         checked, and a cap bounds what the host ever reads from it.
    ///     </para>
    /// </remarks>
    internal static class WorkerProtocol
    {
        /// <summary>The ready frame's magic number: the bytes <c>TPW1</c>.</summary>
        public const uint ReadyMagic = 0x3157_5054;

        /// <summary>The request frame's magic number: the bytes <c>TPRQ</c>.</summary>
        public const uint RequestMagic = 0x5152_5054;

        /// <summary>The result frame's magic number: the bytes <c>TPRS</c>.</summary>
        public const uint ResultMagic = 0x5352_5054;

        /// <summary>The largest PDF the worker accepts: 32 MiB, which also bounds parsing and a damaged-file rebuild scan.</summary>
        public const int MaxInputBytes = 32 * 1024 * 1024;

        /// <summary>The largest thumbnail side the worker renders, in pixels.</summary>
        public const int MaxThumbnailSize = 2048;

        /// <summary>The longest detail a refusal or failure may carry, in bytes.</summary>
        public const int MaxDetailBytes = 1024;

        /// <summary>The ready frame's length: the magic number and the capabilities.</summary>
        private const int ReadyLength = 8;

        /// <summary>The request and result headers' length: the magic number and two integers.</summary>
        private const int HeaderLength = 12;

        /// <summary>
        ///     The most bytes a PNG of a page fitted within <paramref name="size" /> can take: the
        ///     filtered RGBA scanlines, stored uncompressed in the worst case, plus the format's
        ///     framing.
        /// </summary>
        /// <param name="size">The thumbnail side; at most <see cref="MaxThumbnailSize" />.</param>
        /// <returns>The cap.</returns>
        public static int MaxImageBytes(int size)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(size, MaxThumbnailSize);

            // One filter byte and four bytes per pixel per row; deflate's stored blocks, the
            // chunking and the signature add well under a sixty-fourth and a few kilobytes.
            long scanlines = size * (1L + (4L * size));
            return checked((int)(scanlines + (scanlines / 64) + 4096));
        }

        /// <summary>Writes the ready frame.</summary>
        /// <param name="output">The worker's output.</param>
        /// <param name="capabilities">What the worker enforces on itself.</param>
        public static void WriteReady(Stream output, WorkerCapabilities capabilities)
        {
            ArgumentNullException.ThrowIfNull(output);
            Span<byte> frame = stackalloc byte[ReadyLength];
            BinaryPrimitives.WriteUInt32LittleEndian(frame, ReadyMagic);
            BinaryPrimitives.WriteInt32LittleEndian(frame[4..], (int)capabilities);
            output.Write(frame);
            output.Flush();
        }

        /// <summary>Reads the ready frame.</summary>
        /// <param name="input">The worker's output, as the host reads it.</param>
        /// <returns>What the worker enforces on itself.</returns>
        /// <exception cref="EndOfStreamException">The worker closed its output first.</exception>
        /// <exception cref="InvalidDataException">The frame is not a ready frame.</exception>
        public static WorkerCapabilities ReadReady(Stream input)
        {
            ArgumentNullException.ThrowIfNull(input);
            Span<byte> frame = stackalloc byte[ReadyLength];
            input.ReadExactly(frame);
            if (BinaryPrimitives.ReadUInt32LittleEndian(frame) != ReadyMagic)
            {
                throw new InvalidDataException("The worker's first frame is not a ready frame.");
            }

            int capabilities = BinaryPrimitives.ReadInt32LittleEndian(frame[4..]);
            return (capabilities & ~(int)WorkerCapabilities.SystemCallFilter) == 0
                ? (WorkerCapabilities)capabilities
                : throw new InvalidDataException($"The worker's ready frame declares unknown capabilities ({capabilities}).");
        }

        /// <summary>Writes a request.</summary>
        /// <param name="output">The worker's input, as the host writes it.</param>
        /// <param name="size">The thumbnail side.</param>
        /// <param name="pdf">The document.</param>
        public static void WriteRequest(Stream output, int size, ReadOnlySpan<byte> pdf)
        {
            ArgumentNullException.ThrowIfNull(output);
            Span<byte> header = stackalloc byte[HeaderLength];
            BinaryPrimitives.WriteUInt32LittleEndian(header, RequestMagic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], size);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], pdf.Length);
            output.Write(header);
            output.Write(pdf);
            output.Flush();
        }

        /// <summary>Reads a request, refusing an out-of-range size or length before allocating.</summary>
        /// <param name="input">The worker's input.</param>
        /// <returns>The request.</returns>
        /// <exception cref="EndOfStreamException">The host closed the input first.</exception>
        /// <exception cref="InvalidDataException">The frame is not a valid request.</exception>
        public static WorkerRequest ReadRequest(Stream input)
        {
            ArgumentNullException.ThrowIfNull(input);
            Span<byte> header = stackalloc byte[HeaderLength];
            input.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != RequestMagic)
            {
                throw new InvalidDataException("The frame is not a request.");
            }

            int size = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            if (size is < 1 or > MaxThumbnailSize || length is < 1 or > MaxInputBytes)
            {
                throw new InvalidDataException($"The request's size ({size}) or length ({length}) is out of range.");
            }

            byte[] pdf = new byte[length];
            input.ReadExactly(pdf);
            return new WorkerRequest(size, pdf);
        }

        /// <summary>Writes a result.</summary>
        /// <param name="output">The worker's output.</param>
        /// <param name="status">How the worker answered.</param>
        /// <param name="payload">The PNG or the detail.</param>
        public static void WriteResult(Stream output, WorkerStatus status, ReadOnlySpan<byte> payload)
        {
            ArgumentNullException.ThrowIfNull(output);
            Span<byte> header = stackalloc byte[HeaderLength];
            BinaryPrimitives.WriteUInt32LittleEndian(header, ResultMagic);
            BinaryPrimitives.WriteInt32LittleEndian(header[4..], (int)status);
            BinaryPrimitives.WriteInt32LittleEndian(header[8..], payload.Length);
            output.Write(header);
            output.Write(payload);
            output.Flush();
        }

        /// <summary>
        ///     Reads a result, refusing an unknown status or a length beyond its cap before
        ///     allocating: <see cref="MaxImageBytes" /> for a PNG, <see cref="MaxDetailBytes" /> for a
        ///     detail.
        /// </summary>
        /// <param name="input">The worker's output, as the host reads it.</param>
        /// <param name="size">The thumbnail side the request asked for.</param>
        /// <returns>The result.</returns>
        /// <exception cref="EndOfStreamException">The worker closed its output first.</exception>
        /// <exception cref="InvalidDataException">The frame is not a valid result.</exception>
        public static WorkerResult ReadResult(Stream input, int size)
        {
            ArgumentNullException.ThrowIfNull(input);
            Span<byte> header = stackalloc byte[HeaderLength];
            input.ReadExactly(header);
            if (BinaryPrimitives.ReadUInt32LittleEndian(header) != ResultMagic)
            {
                throw new InvalidDataException("The worker's answer is not a result frame.");
            }

            int status = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            int length = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            int cap = status switch
            {
                (int)WorkerStatus.Rendered => MaxImageBytes(size),
                (int)WorkerStatus.Refused or (int)WorkerStatus.Failed => MaxDetailBytes,
                _ => throw new InvalidDataException($"The worker's answer has an unknown status ({status})."),
            };

            if (length < 0 || length > cap)
            {
                throw new InvalidDataException($"The worker's answer declares {length} bytes; at most {cap} are allowed.");
            }

            byte[] payload = new byte[length];
            input.ReadExactly(payload);
            return new WorkerResult((WorkerStatus)status, payload);
        }
    }
}
