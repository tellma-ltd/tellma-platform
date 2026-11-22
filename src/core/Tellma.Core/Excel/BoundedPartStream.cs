// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A part of a workbook's package, read: it counts the decompressed bytes against the length
    ///     the archive's directory declares, and the longest run of bytes from one <c>&lt;</c> to
    ///     the next against a bound. Neither character data nor an attribute value may hold a
    ///     literal <c>&lt;</c>, and a character data section, a comment or a processing
    ///     instruction, which may, counts as one run from its opener to its terminator. So every
    ///     node an XML reader materialises, a start tag with all its attributes, a text node or a
    ///     section, lies inside one run, and the bound caps what one node costs in memory however
    ///     the part was crafted.
    /// </summary>
    /// <remarks>
    ///     The markup is found in UTF-8, and in UTF-16 of either byte order when the part opens
    ///     with a byte order mark or an unmarked <c>&lt;</c>; XML allows no other encoding without a
    ///     declaration, and a workbook's producers write UTF-8.
    /// </remarks>
    internal sealed class BoundedPartStream : Stream
    {
        /// <summary>The longest run of bytes from one <c>&lt;</c> to the next a part may hold, a markup section counted whole.</summary>
        public const int MaxTokenBytes = 4 * 1024 * 1024;

        /// <summary>The rest of a character data section's opener after <c>&lt;![</c>.</summary>
        private const string CharacterDataOpener = "CDATA[";

        /// <summary>The part's stream, owned.</summary>
        private readonly Stream _inner;

        /// <summary>The part's length as the directory declares it.</summary>
        private readonly long _declared;

        /// <summary>The bytes read so far.</summary>
        private long _read;

        /// <summary>The bytes of the current run: since the last <c>&lt;</c> outside a markup section, that one included.</summary>
        private long _run;

        /// <summary>Where the scan stands in the markup.</summary>
        private ScanState _state;

        /// <summary>The characters of an opener or a terminator matched so far.</summary>
        private int _matched;

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

        /// <summary>Where the scan stands in the markup.</summary>
        private enum ScanState
        {
            /// <summary>In content or inside a tag, where only a <c>&lt;</c> starts a run.</summary>
            Content,

            /// <summary>Just after a <c>&lt;</c>.</summary>
            Open,

            /// <summary>Just after <c>&lt;!</c>.</summary>
            Bang,

            /// <summary>Just after <c>&lt;!-</c>.</summary>
            Dash,

            /// <summary>After <c>&lt;![</c>, matching the rest of a character data section's opener.</summary>
            CharacterDataOpener,

            /// <summary>Inside a character data section, until <c>]]&gt;</c>.</summary>
            CharacterData,

            /// <summary>Inside a comment, until <c>--&gt;</c>.</summary>
            Comment,

            /// <summary>Inside a processing instruction, until <c>?&gt;</c>.</summary>
            Instruction,
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

        /// <summary>Counts bytes just read and scans them for the markup that bounds a run.</summary>
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

            // --- UTF-8, once known, skips to each '<' a chunk at a time; until then, and in UTF-16, unit by unit ---
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

        /// <summary>Advances the scan over UTF-8 bytes.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <exception cref="Abstractions.Excel.ImportException">A run exceeds the bound.</exception>
        private void ScanUtf8(ReadOnlySpan<byte> bytes)
        {
            // In content only a '<' changes the state, so the bytes before it are counted whole; a
            // multi-byte character holds no byte below 0x80, so an ASCII byte is always that character.
            int index = 0;
            while (index < bytes.Length)
            {
                if (_state == ScanState.Content)
                {
                    int next = bytes[index..].IndexOf((byte)'<');
                    if (next < 0)
                    {
                        _run += bytes.Length - index;
                        Bound();
                        return;
                    }

                    _run += next;
                    Bound();
                    index += next;
                }

                Unit(bytes[index]);
                index++;
            }
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

        /// <summary>Advances the scan by one byte.</summary>
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

        /// <summary>Counts one code unit into the current run; a <c>&lt;</c> outside a markup section starts the next.</summary>
        /// <param name="unit">The code unit.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void Unit(int unit)
        {
            _run = Advance(unit) ? _width : _run + _width;
            Bound();
        }

        /// <summary>
        ///     Moves the scan by one code unit: past the opener of a character data section, a comment
        ///     or a processing instruction into it, and past its terminator back out.
        /// </summary>
        /// <param name="unit">The code unit.</param>
        /// <returns>True when the unit is a <c>&lt;</c> that starts a run.</returns>
        private bool Advance(int unit)
        {
            switch (_state)
            {
                // --- After a '<': '?' opens an instruction, '!' perhaps a comment or a character data section ---
                case ScanState.Open when unit == '?':
                    (_state, _matched) = (ScanState.Instruction, 0);
                    return false;
                case ScanState.Open when unit == '!':
                    _state = ScanState.Bang;
                    return false;
                case ScanState.Bang when unit == '-':
                    _state = ScanState.Dash;
                    return false;
                case ScanState.Dash when unit == '-':
                    (_state, _matched) = (ScanState.Comment, 0);
                    return false;
                case ScanState.Bang when unit == '[':
                    (_state, _matched) = (ScanState.CharacterDataOpener, 0);
                    return false;
                case ScanState.CharacterDataOpener when unit == CharacterDataOpener[_matched]:
                    _matched++;
                    if (_matched == CharacterDataOpener.Length)
                    {
                        (_state, _matched) = (ScanState.CharacterData, 0);
                    }

                    return false;

                // --- Inside a section a '<' is content; only the section's terminator closes it ---
                case ScanState.CharacterData:
                    Close(unit, ']', needed: 2);
                    return false;
                case ScanState.Comment:
                    Close(unit, '-', needed: 2);
                    return false;
                case ScanState.Instruction:
                    Close(unit, '?', needed: 1);
                    return false;

                // --- Anything else after a '<' is a tag, and in content or a tag a '<' starts the next run ---
                case ScanState.Content:
                case ScanState.Open:
                case ScanState.Bang:
                case ScanState.Dash:
                case ScanState.CharacterDataOpener:
                default:
                    if (unit == '<')
                    {
                        _state = ScanState.Open;
                        return true;
                    }

                    _state = ScanState.Content;
                    return false;
            }
        }

        /// <summary>Tracks a section's terminator: at least a number of one mark, then <c>&gt;</c>.</summary>
        /// <param name="unit">The code unit.</param>
        /// <param name="mark">The terminator's mark: <c>]</c> for character data, <c>-</c> for a comment, <c>?</c> for an instruction.</param>
        /// <param name="needed">How many marks must precede the <c>&gt;</c>.</param>
        private void Close(int unit, char mark, int needed)
        {
            if (unit == mark)
            {
                _matched++;
            }
            else if (unit == '>' && _matched >= needed)
            {
                (_state, _matched) = (ScanState.Content, 0);
            }
            else
            {
                _matched = 0;
            }
        }
    }
}
