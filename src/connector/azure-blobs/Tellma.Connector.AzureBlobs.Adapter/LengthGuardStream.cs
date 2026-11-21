// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;

namespace Tellma.Connector.AzureBlobs
{
    /// <summary>
    ///     A read-only view of a write's content that yields exactly its declared length: it reports
    ///     that length to the client, and fails a read that finds the content ending early or running
    ///     past it, so an upload of the wrong length never completes. It never disposes the content,
    ///     which its caller owns.
    /// </summary>
    /// <remarks>
    ///     The client reads the stream inside its HTTP request, where a failure may reach the caller
    ///     wrapped in the transport's own exceptions, so the guard also records what it found in
    ///     <see cref="Mismatch" /> for the store to report whatever the wrapping. A seekable content is
    ///     seekable through the guard, so the client can rewind it for a retry.
    /// </remarks>
    internal sealed class LengthGuardStream : Stream
    {
        private readonly Stream _content;
        private readonly long _origin;
        private readonly string _name;
        private long _consumed;

        /// <summary>Creates the guard over a write's content from its current position.</summary>
        /// <param name="content">The content, positioned at its first byte.</param>
        /// <param name="length">The declared length; not negative.</param>
        /// <param name="name">The object's name, for the failure's message.</param>
        public LengthGuardStream(Stream content, long length, string name)
        {
            ArgumentNullException.ThrowIfNull(content);
            ArgumentOutOfRangeException.ThrowIfNegative(length);
            ArgumentNullException.ThrowIfNull(name);
            _content = content;
            _origin = content.CanSeek ? content.Position : 0;
            _name = name;
            Length = length;
        }

        /// <summary>What the guard found wrong with the content's length, or null while it found nothing.</summary>
        public string? Mismatch { get; private set; }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => _content.CanSeek;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <summary>The declared length, whatever the content's own.</summary>
        public override long Length { get; }

        /// <inheritdoc />
        public override long Position
        {
            get => _consumed;
            set => _ = Seek(value, SeekOrigin.Begin);
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            ValidateBufferArguments(buffer, offset, count);
            return Read(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            // --- At the declared end: one probe byte tells a whole content from one running past it ---
            if (_consumed >= Length)
            {
                Span<byte> probe = stackalloc byte[1];
                return buffer.IsEmpty || _content.Read(probe) == 0 ? 0 : throw Fail(RunsPast());
            }

            // --- Before it: never more than the declared remainder, and an early end is a failure ---
            int read = _content.Read(buffer[..Remaining(buffer.Length)]);
            return Advance(read);
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ValidateBufferArguments(buffer, offset, count);
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            // The same two cases as the synchronous read.
            if (_consumed >= Length)
            {
                if (buffer.IsEmpty)
                {
                    return 0;
                }

                byte[] probe = new byte[1];
                return await _content.ReadAsync(probe, cancellationToken).ConfigureAwait(false) == 0 ? 0 : throw Fail(RunsPast());
            }

            int read = await _content.ReadAsync(buffer[..Remaining(buffer.Length)], cancellationToken).ConfigureAwait(false);
            return Advance(read);
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!_content.CanSeek)
            {
                throw new NotSupportedException("The content of this blob write cannot seek.");
            }

            // Positions are the guard's own, from the content's first byte to its declared end.
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _consumed + offset,
                SeekOrigin.End => Length + offset,
                _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, "Not a seek origin."),
            };
            ArgumentOutOfRangeException.ThrowIfNegative(target, nameof(offset));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(target, Length, nameof(offset));

            _ = _content.Seek(_origin + target, SeekOrigin.Begin);
            _consumed = target;
            return target;
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException("A blob write's content is read-only.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("A blob write's content is read-only.");
        }

        /// <summary>How many bytes a read of a buffer may ask for: never past the declared end.</summary>
        /// <param name="bufferLength">The buffer's length.</param>
        /// <returns>The count.</returns>
        private int Remaining(int bufferLength)
        {
            return (int)Math.Min(bufferLength, Length - _consumed);
        }

        /// <summary>Counts a read before the declared end, failing one that found the content's end.</summary>
        /// <param name="read">The bytes the content yielded.</param>
        /// <returns>The same count.</returns>
        private int Advance(int read)
        {
            if (read == 0)
            {
                throw Fail(string.Create(
                    CultureInfo.InvariantCulture,
                    $"The content of the blob '{_name}' ended after {_consumed} of its declared {Length} bytes."));
            }

            _consumed += read;
            return read;
        }

        /// <summary>The message of a content running past its declared length.</summary>
        /// <returns>The message.</returns>
        private string RunsPast()
        {
            return string.Create(CultureInfo.InvariantCulture, $"The content of the blob '{_name}' runs past its declared length of {Length} bytes.");
        }

        /// <summary>Records a mismatch and builds the exception that stops the upload.</summary>
        /// <param name="message">What was found.</param>
        /// <returns>The exception, which is not an I/O failure, so the client's retry policy does not retry it.</returns>
        private InvalidDataException Fail(string message)
        {
            Mismatch = message;
            return new InvalidDataException(message);
        }
    }
}
