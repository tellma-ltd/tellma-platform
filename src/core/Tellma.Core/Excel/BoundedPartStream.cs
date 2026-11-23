// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using System.Numerics;
using System.Runtime.InteropServices;

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
    ///     with a byte order mark or an unmarked <c>&lt;</c>: a package's parts may be encoded in
    ///     those alone. A part holding a NUL byte in UTF-8, or a NUL unit in UTF-16, is refused:
    ///     no XML document may hold U+0000, and every UCS-4 part, which an XML reader would
    ///     otherwise decode in units this scan cannot see, holds one. Content is skipped to its next
    ///     <c>&lt;</c>, and a markup section to its terminator, a vector at a time, so a part costs
    ///     about the same to scan whatever its encoding and its markup, and whatever its text holds.
    /// </remarks>
    internal sealed class BoundedPartStream : Stream
    {
        /// <summary>The longest run of bytes from one <c>&lt;</c> to the next a part may hold, a markup section counted whole.</summary>
        public const int MaxTokenBytes = 4 * 1024 * 1024;

        /// <summary>The rest of a character data section's opener after <c>&lt;![</c>.</summary>
        private const string CharacterDataOpener = "CDATA[";

        /// <summary>The units that can change a UTF-8 scan, as bytes.</summary>
        private static readonly Markup<byte> Utf8Markup = new(static unit => (byte)unit);

        /// <summary>The units that can change a UTF-16 scan read in the machine's byte order.</summary>
        private static readonly Markup<char> NativeUtf16Markup = new(static unit => (char)unit);

        /// <summary>The units that can change a UTF-16 scan read in the other byte order, each as it lies in memory.</summary>
        private static readonly Markup<char> SwappedUtf16Markup = new(static unit => (char)BinaryPrimitives.ReverseEndianness((ushort)unit));

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

        /// <summary>How a unit, as it is read, reads as a code unit.</summary>
        /// <typeparam name="T">The type the units are read as.</typeparam>
        private interface ICodeUnit<T>
        {
            /// <summary>Reads a unit as a code unit.</summary>
            /// <param name="unit">The unit as it is read.</param>
            /// <returns>The code unit.</returns>
            static abstract int Read(T unit);
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

        /// <summary>Refuses a part that holds U+0000 or a run past the bound.</summary>
        /// <returns>The refusal.</returns>
        private static Abstractions.Excel.ImportException Malformed()
        {
            return ImportFaults.Malformed(ImportFaults.XmlReason);
        }

        /// <summary>Finds a section's terminator in units read as bytes or as UTF-16 units.</summary>
        /// <typeparam name="T">The type the units are read as: a byte or a UTF-16 unit.</typeparam>
        /// <param name="units">The units.</param>
        /// <param name="terminator">The terminator, two or three units.</param>
        /// <returns>Where the first terminator starts; -1 when none does.</returns>
        private static int IndexOfTerminator<T>(ReadOnlySpan<T> units, ReadOnlySpan<T> terminator)
            where T : unmanaged
        {
            // The units are compared as the unsigned integers of their width; the branch is the JIT's to fold.
            return typeof(T) == typeof(byte)
                ? FindTerminator(MemoryMarshal.Cast<T, byte>(units), MemoryMarshal.Cast<T, byte>(terminator))
                : FindTerminator(MemoryMarshal.Cast<T, ushort>(units), MemoryMarshal.Cast<T, ushort>(terminator));
        }

        /// <summary>
        ///     Finds a section's terminator, comparing each of its units at every position of a vector
        ///     at once: the cost is the same whatever the units hold, where a search that confirms each
        ///     place its first and last units match would slow on units dense in such places.
        /// </summary>
        /// <typeparam name="TNumber">The unsigned integer the units are compared as.</typeparam>
        /// <param name="units">The units.</param>
        /// <param name="terminator">The terminator, two or three units.</param>
        /// <returns>Where the first terminator starts; -1 when none does.</returns>
        private static int FindTerminator<TNumber>(ReadOnlySpan<TNumber> units, ReadOnlySpan<TNumber> terminator)
            where TNumber : unmanaged, IBinaryInteger<TNumber>, IUnsignedNumber<TNumber>
        {
            int last = terminator.Length - 1;
            int index = 0;

            // --- A vector of starting places at a time: a place matches when each of its units does ---
            if (Vector.IsHardwareAccelerated)
            {
                var first = new Vector<TNumber>(terminator[0]);
                var middle = new Vector<TNumber>(terminator[last - 1]);
                var end = new Vector<TNumber>(terminator[last]);
                ref TNumber start = ref MemoryMarshal.GetReference(units);
                for (; index + last + Vector<TNumber>.Count <= units.Length; index += Vector<TNumber>.Count)
                {
                    // A two-unit terminator compares its first unit twice, which changes nothing.
                    Vector<TNumber> found = Vector.Equals(Vector.LoadUnsafe(ref start, (nuint)index), first)
                        & Vector.Equals(Vector.LoadUnsafe(ref start, (nuint)(index + last - 1)), middle)
                        & Vector.Equals(Vector.LoadUnsafe(ref start, (nuint)(index + last)), end);
                    if (found != Vector<TNumber>.Zero)
                    {
                        // The first place that matches lies among this vector's places.
                        return index + units.Slice(index, Vector<TNumber>.Count + last).IndexOf(terminator);
                    }
                }
            }

            // --- The places a vector no longer covers, or every place without vectors ---
            int tail = units[index..].IndexOf(terminator);
            return tail < 0 ? -1 : index + tail;
        }

        /// <summary>Counts bytes just read and scans them for the markup that bounds a run.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The part ran past its declared length, holds U+0000, or a run exceeds the bound.</exception>
        private void Account(ReadOnlySpan<byte> bytes)
        {
            // --- The declared length: a part that inflates past it is refused at the first byte beyond ---
            _read += bytes.Length;
            if (_read > _declared)
            {
                throw ImportFaults.TooLarge(_read, _declared);
            }

            // --- The encoding, from the first two bytes, which may arrive in separate reads ---
            int index = 0;
            while (_width == 0 && index < bytes.Length)
            {
                Detect(bytes[index++]);
            }

            // --- The rest, scanned in the encoding's own units ---
            if (_width == 1)
            {
                Scan<byte, Utf8Unit>(bytes[index..], Utf8Markup);
            }
            else if (_width == 2)
            {
                ScanUtf16(bytes[index..]);
            }
        }

        /// <summary>
        ///     Takes one of the part's first two bytes: a UTF-16 byte order mark or an unmarked UTF-16
        ///     <c>&lt;</c> tells UTF-16, and anything else UTF-8.
        /// </summary>
        /// <param name="b">The byte.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The bytes hold U+0000, or a run exceeds the bound.</exception>
        private void Detect(byte b)
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

            // The two bytes are scanned as the encoding reads them: two UTF-8 units, or one UTF-16 unit.
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
        }

        /// <summary>Scans UTF-16 bytes, a code unit split across reads held for the next.</summary>
        /// <param name="bytes">The bytes.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The bytes hold U+0000, or a run exceeds the bound.</exception>
        private void ScanUtf16(ReadOnlySpan<byte> bytes)
        {
            // --- A unit whose first byte ended the last read ---
            if (_pending >= 0 && bytes.Length > 0)
            {
                Unit(_bigEndian ? (_pending << 8) | bytes[0] : (bytes[0] << 8) | _pending);
                _pending = -1;
                bytes = bytes[1..];
            }

            // --- Whole units, read in place: in the machine's byte order, or compared byte-swapped ---
            int whole = bytes.Length & ~1;
            ReadOnlySpan<char> units = MemoryMarshal.Cast<byte, char>(bytes[..whole]);
            if (_bigEndian == BitConverter.IsLittleEndian)
            {
                Scan<char, SwappedUtf16Unit>(units, SwappedUtf16Markup);
            }
            else
            {
                Scan<char, NativeUtf16Unit>(units, NativeUtf16Markup);
            }

            // --- A trailing first byte waits for its second ---
            if (whole < bytes.Length)
            {
                _pending = bytes[whole];
            }
        }

        /// <summary>
        ///     Advances the scan over code units, after refusing any NUL among them. Content is skipped
        ///     to its next <c>&lt;</c>, and a markup section to the end of its terminator, a vector at a
        ///     time; a terminator a read splits is carried as the marks it ends on, and every other
        ///     unit (an opener's) is taken one at a time.
        /// </summary>
        /// <typeparam name="T">The type the units are read as.</typeparam>
        /// <typeparam name="TUnit">How a unit read reads as a code unit.</typeparam>
        /// <param name="units">The units.</param>
        /// <param name="markup">The units that can change the scan, as they are read.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The units hold U+0000, or a run exceeds the bound.</exception>
        private void Scan<T, TUnit>(ReadOnlySpan<T> units, Markup<T> markup)
            where T : unmanaged, IEquatable<T>
            where TUnit : struct, ICodeUnit<T>
        {
            // --- U+0000, refused wherever it lies ---
            if (units.Contains(markup.Nul))
            {
                throw Malformed();
            }

            int index = 0;
            while (index < units.Length)
            {
                ReadOnlySpan<T> rest = units[index..];
                Section<T>? section = _state switch
                {
                    ScanState.CharacterData => markup.CharacterData,
                    ScanState.Comment => markup.Comment,
                    ScanState.Instruction => markup.Instruction,
                    ScanState.Content or ScanState.Open or ScanState.Bang or ScanState.Dash or ScanState.CharacterDataOpener => null,
                    _ => null,
                };

                // --- Content: everything before the next '<' joins the run ---
                if (_state == ScanState.Content)
                {
                    int next = rest.IndexOf(markup.Open);
                    if (next < 0)
                    {
                        Count(rest.Length);
                        return;
                    }

                    Count(next);
                    index += next;
                }

                // --- A section with no terminator begun: to the end of its terminator, or past this read
                // with the marks it ends on carried, since they may begin the terminator the next read ends ---
                else if (section is not null && _matched == 0)
                {
                    int end = IndexOfTerminator(rest, section.Terminator);
                    if (end < 0)
                    {
                        _matched = rest.Length - 1 - rest.LastIndexOfAnyExcept(section.Mark);
                        Count(rest.Length);
                        return;
                    }

                    int closed = end + section.Terminator.Length;
                    Count(closed);
                    _state = ScanState.Content;
                    index += closed;
                    continue;
                }

                // --- A terminator begun in the last read: its run of marks counted whole; the unit after
                // them closes the section or not ---
                else if (section is not null)
                {
                    int marks = rest.IndexOfAnyExcept(section.Mark);
                    marks = marks < 0 ? rest.Length : marks;
                    if (marks > 0)
                    {
                        _matched += marks;
                        Count(marks);
                        index += marks;
                        continue;
                    }
                }

                // --- A unit that may change the scan: a '<', one of an opener, or the one after a terminator's marks ---
                Unit(TUnit.Read(units[index]));
                index++;
            }
        }

        /// <summary>Counts code units skipped within the current run.</summary>
        /// <param name="units">How many.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void Count(int units)
        {
            _run += (long)units * _width;
            Bound();
        }

        /// <summary>Refuses the part once the current run exceeds the bound.</summary>
        /// <exception cref="Abstractions.Excel.ImportException">The run exceeds the bound.</exception>
        private void Bound()
        {
            if (_run > MaxTokenBytes)
            {
                throw Malformed();
            }
        }

        /// <summary>Counts one code unit into the current run; a <c>&lt;</c> outside a markup section starts the next.</summary>
        /// <param name="unit">The code unit.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The unit is U+0000, or the run exceeds the bound.</exception>
        private void Unit(int unit)
        {
            if (unit == 0)
            {
                throw Malformed();
            }

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

        /// <summary>A UTF-8 byte, its own code unit.</summary>
        private readonly struct Utf8Unit : ICodeUnit<byte>
        {
            /// <inheritdoc />
            public static int Read(byte unit)
            {
                return unit;
            }
        }

        /// <summary>A UTF-16 unit read in the machine's byte order.</summary>
        private readonly struct NativeUtf16Unit : ICodeUnit<char>
        {
            /// <inheritdoc />
            public static int Read(char unit)
            {
                return unit;
            }
        }

        /// <summary>A UTF-16 unit read in the other byte order.</summary>
        private readonly struct SwappedUtf16Unit : ICodeUnit<char>
        {
            /// <inheritdoc />
            public static int Read(char unit)
            {
                return BinaryPrimitives.ReverseEndianness(unit);
            }
        }

        /// <summary>The units that can change the scan, as they are read.</summary>
        /// <typeparam name="T">The type the units are read as.</typeparam>
        /// <param name="encode">Writes a code unit as it is read.</param>
        private sealed class Markup<T>(Func<int, T> encode)
            where T : IEquatable<T>
        {
            /// <summary>The <c>&lt;</c> that starts a run.</summary>
            public T Open { get; } = encode('<');

            /// <summary>The NUL every part is refused for.</summary>
            public T Nul { get; } = encode(0);

            /// <summary>A character data section's terminator.</summary>
            public Section<T> CharacterData { get; } = new(encode, ']', "]]>");

            /// <summary>A comment's terminator.</summary>
            public Section<T> Comment { get; } = new(encode, '-', "-->");

            /// <summary>A processing instruction's terminator.</summary>
            public Section<T> Instruction { get; } = new(encode, '?', "?>");
        }

        /// <summary>A markup section's terminator, as it is read.</summary>
        /// <typeparam name="T">The type the units are read as.</typeparam>
        /// <param name="encode">Writes a code unit as it is read.</param>
        /// <param name="mark">The mark the terminator repeats before its <c>&gt;</c>.</param>
        /// <param name="terminator">The terminator.</param>
        private sealed class Section<T>(Func<int, T> encode, char mark, string terminator)
        {
            /// <summary>The mark the terminator repeats before its <c>&gt;</c>.</summary>
            public T Mark { get; } = encode(mark);

            /// <summary>The terminator.</summary>
            public T[] Terminator { get; } = [.. terminator.Select(unit => encode(unit))];
        }
    }
}
