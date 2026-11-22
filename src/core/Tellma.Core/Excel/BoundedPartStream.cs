// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A part of a workbook's package, read: it counts the decompressed bytes against the length
    ///     the archive's directory declares, and the longest run of bytes between two markup
    ///     delimiters (<c>&lt;</c> and <c>&gt;</c>) against a bound. Every token an XML reader
    ///     materialises, a text node, a tag or an attribute value, lies inside such a run, so the
    ///     bound caps what one token costs in memory however the part was crafted.
    /// </summary>
    /// <remarks>
    ///     The delimiters are found in UTF-8, and in UTF-16 of either byte order when the part opens
    ///     with a byte order mark or an unmarked <c>&lt;</c>; XML allows no other encoding without a
    ///     declaration, and a workbook's producers write UTF-8.
    /// </remarks>
    internal sealed class BoundedPartStream : Stream
    {
        /// <summary>The longest run of bytes between two markup delimiters a part may hold.</summary>
        public const int MaxTokenBytes = 4 * 1024 * 1024;

        /// <summary>The part's stream, owned.</summary>
        private readonly Stream _inner;

        /// <summary>The part's length as the directory declares it.</summary>
        private readonly long _declared;

        /// <summary>The bytes read so far.</summary>
        private long _read;

        /// <summary>The bytes read since the last delimiter.</summary>
        private long _run;

        /// <summary>The width of a code unit once the encoding is known: 1 for UTF-8, 2 for UTF-16; 0 before the first byte.</summary>
        private int _width;

        /// <summary>Whether UTF-16 is big-endian.</summary>
        private bool _bigEndian;

        /// <summary>The first byte of a UTF-16 code unit split across reads; -1 when none.</summary>
        private int _pending = -1;

        /// <summary>The first byte of the part, held until the second tells the encoding; -1 when none.</summary>
        private int _first = -1;

        /// <summary>Wraps a part's stream.</summary>
        /// <param name="inner">The part's decompressing stream; disposed with this one.</param>
        /// <param name="declared">The part's length as the directory declares it.</param>
        public BoundedPartStream(Stream inner, long declared)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            ArgumentOutOfRangeException.ThrowIfNegative(declared);
            _declared = declared;
        }

        /// <inheritdoc />
        public override bool CanRead => true;

        /// <inheritdoc />
        public override bool CanSeek => false;

        /// <inheritdoc />
        public override bool CanWrite => false;

        /// <inheritdoc />
        public override long Length => throw new NotSupportedException();

        /// <inheritdoc />
        public override long Position
        {
            get => _read;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override int Read(byte[] buffer, int offset, int count)
        {
            return Read(buffer.AsSpan(offset, count));
        }

        /// <inheritdoc />
        public override int Read(Span<byte> buffer)
        {
            int read = _inner.Read(buffer);
            Account(buffer[..read]);
            return read;
        }

        /// <inheritdoc />
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            int read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            Account(buffer.Span[..read]);
            return read;
        }

        /// <inheritdoc />
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }

        /// <inheritdoc />
        public override void Flush()
        {
        }

        /// <inheritdoc />
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _inner.Dispose();
            }

            base.Dispose(disposing);
        }

        /// <summary>Counts bytes just read and scans them for delimiters.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The part ran past its declared length, or a run exceeds the bound.</exception>
        private void Account(ReadOnlySpan<byte> bytes)
        {
            // --- The declared length: a part that inflates past it is refused at the first byte beyond ---
            _read += bytes.Length;
            if (_read > _declared)
            {
                throw ImportFaults.TooLarge(_read, _declared);
            }

            // --- UTF-8, once known, scans a chunk at a time; until then, and in UTF-16, byte by byte ---
            for (int index = 0; index < bytes.Length; index++)
            {
                if (_width == 1)
                {
                    ScanUtf8(bytes[index..]);
                    return;
                }

                Scan(bytes[index]);
            }
        }

        /// <summary>Advances the delimiter scan over UTF-8 bytes: the run is what follows their last delimiter, or grows by them.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void ScanUtf8(ReadOnlySpan<byte> bytes)
        {
            // --- No delimiter: the run carried in grows by the whole chunk ---
            int first = bytes.IndexOfAny((byte)'<', (byte)'>');
            if (first < 0)
            {
                _run += bytes.Length;
                Bound();
                return;
            }

            // --- The run carried in ends at the chunk's first delimiter ---
            _run += first;
            Bound();

            // --- Runs wholly inside the chunk: none can exceed the bound unless the chunk itself does ---
            int last = bytes.LastIndexOfAny((byte)'<', (byte)'>');
            if (bytes.Length > MaxTokenBytes)
            {
                for (int start = first + 1; start <= last;)
                {
                    int next = bytes[start..].IndexOfAny((byte)'<', (byte)'>');
                    _run = next;
                    Bound();
                    start += next + 1;
                }
            }

            // --- The run carried out starts after the chunk's last delimiter ---
            _run = bytes.Length - last - 1;
            Bound();
        }

        /// <summary>Refuses the part once the current run exceeds the bound.</summary>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void Bound()
        {
            if (_run > MaxTokenBytes)
            {
                throw ImportFaults.Malformed(ImportFaults.XmlReason);
            }
        }

        /// <summary>Advances the delimiter scan by one byte.</summary>
        /// <param name="b">The byte.</param>
        private void Scan(byte b)
        {
            // --- The encoding, from the first two bytes: a byte order mark, an unmarked UTF-16 '<', else UTF-8 ---
            if (_width == 0)
            {
                if (_first < 0)
                {
                    _first = b;
                    return;
                }

                (_width, _bigEndian) = (_first, b) switch
                {
                    (0xFF, 0xFE) or (0x3C, 0x00) => (2, false),
                    (0xFE, 0xFF) or (0x00, 0x3C) => (2, true),
                    _ => (1, false),
                };

                int held = _first;
                _first = -1;
                if (_width == 1)
                {
                    Unit(held);
                    Unit(b);
                }
                else
                {
                    Unit(_bigEndian ? (held << 8) | b : (b << 8) | held);
                }

                return;
            }

            // --- A code unit: one byte in UTF-8, two in UTF-16, the first held across reads ---
            if (_width == 1)
            {
                Unit(b);
            }
            else if (_pending < 0)
            {
                _pending = b;
            }
            else
            {
                Unit(_bigEndian ? (_pending << 8) | b : (b << 8) | _pending);
                _pending = -1;
            }
        }

        /// <summary>Counts one code unit into the current run, ending it at a delimiter.</summary>
        /// <param name="unit">The code unit.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void Unit(int unit)
        {
            if (unit is '<' or '>')
            {
                _run = 0;
                return;
            }

            _run += _width;
            Bound();
        }
    }
}
