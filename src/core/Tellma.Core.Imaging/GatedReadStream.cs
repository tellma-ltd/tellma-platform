// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Imaging
{
    /// <summary>
    ///     A read-only, seekable view of an input that a native decoder pulls its bytes through, and
    ///     that cancellation closes: once the token is cancelled or the gate is closed, every read
    ///     returns end of input.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Skia's streaming codecs (JPEG, PNG, GIF) and PDFium read their input on demand, through
    ///         callbacks, while they decode. Ending the input is the one way to stop a native decode
    ///         from outside it: the decoder sees a truncated file and returns, instead of running to
    ///         completion after its caller has given up. That is what makes a thumbnail's time budget
    ///         a real cancellation rather than an abandoned thread.
    ///     </para>
    ///     <para>
    ///         Native code calls these members through reverse P/Invoke, where a managed exception
    ///         would cross native frames and take the process down. No member that a reader calls
    ///         throws: a failing inner stream closes the gate and reads as end of input. The view
    ///         never disposes the inner stream, which stays the caller's.
    ///     </para>
    ///     <para>
    ///         One lock covers the gate check and the inner read, and closing the gate takes it too:
    ///         once <see cref="CloseGate" /> (or disposal) returns, no read of the inner stream is in
    ///         flight and none can start. Inner reads are single bounded calls on an in-memory or
    ///         file stream, so closing waits at most for one of them.
    ///     </para>
    /// </remarks>
    internal sealed class GatedReadStream : Stream
    {
        // The inner stream's position at construction: the view's position 0.
        private readonly long _origin;

        // The view's length, read once: native readers ask for it repeatedly.
        private readonly long _length;

        private readonly Stream _inner;
        private readonly CancellationToken _token;

        // Serialises the gate check with the inner read, and closing with both.
        private readonly Lock _sync = new();
        private long _position;
        private volatile bool _closed;

        /// <summary>Opens a view of <paramref name="inner" /> from its current position to its end.</summary>
        /// <param name="inner">The input; readable and seekable.</param>
        /// <param name="token">Closes the gate when cancelled.</param>
        /// <exception cref="ArgumentException"><paramref name="inner" /> is not readable and seekable.</exception>
        public GatedReadStream(Stream inner, CancellationToken token)
        {
            ArgumentNullException.ThrowIfNull(inner);
            if (!inner.CanRead || !inner.CanSeek)
            {
                throw new ArgumentException("The input must be readable and seekable.", nameof(inner));
            }

            _inner = inner;
            _token = token;
            _origin = inner.Position;
            _length = Math.Max(0, inner.Length - _origin);
        }

        /// <summary>Whether the gate is closed: reads return end of input from now on.</summary>
        public bool IsClosed => _closed || _token.IsCancellationRequested;

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => true;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => _length;

        /// <inheritdoc />
        public override long Position
        {
            get => _position;
            set => _position = Math.Clamp(value, 0, _length);
        }

        /// <summary>
        ///     Closes the gate, waiting for a read already inside the inner stream to finish; from
        ///     then on the inner stream is never touched again.
        /// </summary>
        public void CloseGate()
        {
            lock (_sync)
            {
                _closed = true;
            }
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            // Section: the gate, checked under the same lock as the read, so a close never races a
            // read into the caller's stream. A closed gate is end of input, never an exception.
            lock (_sync)
            {
                if (IsClosed || buffer.IsEmpty || _position >= _length)
                {
                    return 0;
                }

                int count = (int)Math.Min(buffer.Length, _length - _position);
                try
                {
                    _inner.Position = _origin + _position;
                    int read = _inner.Read(buffer[..count]);
                    _position += read;
                    return read;
                }
#pragma warning disable CA1031 // A native caller cannot receive an exception; a failed read ends the input instead.
                catch (Exception)
#pragma warning restore CA1031
                {
                    _closed = true;
                    return 0;
                }
            }
        }

        /// <inheritdoc />
        public override int ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            return Read(one) == 1 ? one[0] : -1;
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            // An out-of-range target clamps rather than throws, for the same reason reads never throw.
            long target = origin switch
            {
                SeekOrigin.Begin => offset,
                SeekOrigin.Current => _position + offset,
                SeekOrigin.End => _length + offset,
                _ => _position,
            };

            _position = Math.Clamp(target, 0, _length);
            return _position;
        }

        /// <inheritdoc />
        public override void Flush()
        {
            // Read-only: nothing to flush.
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException("The view is read-only.");
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException("The view is read-only.");
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            // Disposing closes the gate only: the inner stream is the caller's, and a native reader
            // that still holds the view keeps reading end of input.
            CloseGate();
            base.Dispose(disposing);
        }
    }
}
