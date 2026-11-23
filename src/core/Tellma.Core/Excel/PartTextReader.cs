// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A part's text, decoded in exactly the encoding its bounds are scanned in. An XML reader over
    ///     a text reader never switches encoding, so the characters it parses are the ones the bounds
    ///     counted, whatever the part's XML declaration names; a declaration naming an encoding other
    ///     than UTF-8 or UTF-16, the only two a package's parts may use, is refused all the same.
    /// </summary>
    /// <remarks>
    ///     Refusals surface as <see cref="XmlException" />, as the reader's own do: a declaration of
    ///     another encoding, one that does not close within <see cref="MaxDeclarationLength" />
    ///     characters, and a byte sequence that is not valid in the part's encoding.
    /// </remarks>
    /// <param name="part">The part; disposed with this reader.</param>
    internal sealed partial class PartTextReader(BoundedPartStream part) : TextReader
    {
        /// <summary>The most characters an XML declaration may hold, its delimiters included.</summary>
        public const int MaxDeclarationLength = 1024;

        /// <summary>The bytes the decoder takes from the part at a time.</summary>
        private const int BufferSize = 4096;

        /// <summary>What an XML declaration opens with, before the white space that must follow it.</summary>
        private const string DeclarationOpener = "<?xml";

        /// <summary>What an XML declaration closes with.</summary>
        private const string DeclarationCloser = "?>";

        /// <summary>The part, owned until the decoder takes it.</summary>
        private readonly BoundedPartStream _part = part ?? throw new ArgumentNullException(nameof(part));

        /// <summary>The decoder over the part, made at the first read; owns the part.</summary>
        private StreamReader? _text;

        /// <summary>The characters read ahead to check the declaration, returned before any other.</summary>
        private string _head = string.Empty;

        /// <summary>Where the next read starts in <see cref="_head" />.</summary>
        private int _headAt;

        /// <inheritdoc />
        public override int Peek()
        {
            StreamReader text = Text();
            return _headAt < _head.Length ? _head[_headAt] : Decode(text.Peek);
        }

        /// <inheritdoc />
        public override int Read()
        {
            StreamReader text = Text();
            return _headAt < _head.Length ? _head[_headAt++] : Decode(text.Read);
        }

        /// <inheritdoc />
        public override int Read(char[] buffer, int index, int count)
        {
            ArgumentNullException.ThrowIfNull(buffer);
            return Read(buffer.AsSpan(index, count));
        }

        /// <inheritdoc />
        public override int Read(Span<char> buffer)
        {
            StreamReader text = Text();

            // --- The characters read ahead come first, then the decoder's ---
            if (_headAt < _head.Length)
            {
                int taken = Math.Min(_head.Length - _headAt, buffer.Length);
                _head.AsSpan(_headAt, taken).CopyTo(buffer);
                _headAt += taken;
                return taken;
            }

            try
            {
                return text.Read(buffer);
            }
            catch (DecoderFallbackException)
            {
                throw Invalid();
            }
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // The decoder owns the part once it is made.
                if (_text is null)
                {
                    _part.Dispose();
                }
                else
                {
                    _text.Dispose();
                }
            }

            base.Dispose(disposing);
        }

        /// <summary>The refusal of a byte sequence not valid in the part's encoding.</summary>
        /// <returns>The refusal.</returns>
        private static XmlException Invalid()
        {
            return new XmlException("The part holds a byte sequence that is not valid in its encoding.");
        }

        /// <summary>Whether a character is XML white space.</summary>
        /// <param name="character">The character.</param>
        /// <returns>True when it is.</returns>
        private static bool IsXmlWhiteSpace(char character)
        {
            return character is ' ' or '\t' or '\r' or '\n';
        }

        /// <summary>Reads a character from the decoder.</summary>
        /// <param name="read">The decoder's read or peek.</param>
        /// <returns>The character; -1 at the end.</returns>
        /// <exception cref="XmlException">The bytes are not valid in the part's encoding.</exception>
        private static int Decode(Func<int> read)
        {
            try
            {
                return read();
            }
            catch (DecoderFallbackException)
            {
                throw Invalid();
            }
        }

        /// <summary>An XML declaration's encoding pseudo-attribute and its quoted value.</summary>
        /// <returns>The expression.</returns>
        [GeneratedRegex("[ \\t\\r\\n]encoding[ \\t\\r\\n]*=[ \\t\\r\\n]*(?:\"(?<name>[^\"]*)\"|'(?<name>[^']*)')", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex EncodingDeclaration();

        /// <summary>
        ///     Reads ahead the part's XML declaration, when the text opens with one, and refuses it
        ///     when it names an encoding other than UTF-8 or UTF-16.
        /// </summary>
        /// <param name="text">The decoder, at the start of the text.</param>
        /// <returns>The characters read ahead: the declaration, or the few that show there is none.</returns>
        /// <exception cref="XmlException">The declaration names another encoding or does not close, or the bytes are not valid.</exception>
        private static string ReadDeclaration(StreamReader text)
        {
            // --- '<?xml' and white space open a declaration; anything else is no declaration ---
            StringBuilder head = new();
            int next;
            while (head.Length <= DeclarationOpener.Length && (next = Decode(text.Read)) >= 0)
            {
                _ = head.Append((char)next);
            }

            if (head.Length <= DeclarationOpener.Length
                || !head.ToString(0, DeclarationOpener.Length).Equals(DeclarationOpener, StringComparison.Ordinal)
                || !IsXmlWhiteSpace(head[DeclarationOpener.Length]))
            {
                return head.ToString();
            }

            // --- The declaration, to its '?>', within the bound ---
            while (!EndsWith(head, DeclarationCloser))
            {
                if (head.Length >= MaxDeclarationLength)
                {
                    throw new XmlException("The part's XML declaration is too long.");
                }

                next = Decode(text.Read);
                if (next < 0)
                {
                    // An unclosed declaration is the reader's to refuse.
                    return head.ToString();
                }

                _ = head.Append((char)next);
            }

            // --- Its encoding, when it names one, is UTF-8 or UTF-16 ---
            string declaration = head.ToString();
            Match encoding = EncodingDeclaration().Match(declaration);
            bool unicode = !encoding.Success
                || encoding.Groups["name"].Value.Equals("UTF-8", StringComparison.OrdinalIgnoreCase)
                || encoding.Groups["name"].Value.Equals("UTF-16", StringComparison.OrdinalIgnoreCase);
            return unicode
                ? declaration
                : throw new XmlException("The part's XML declaration names an encoding other than UTF-8 or UTF-16.");
        }

        /// <summary>Whether a builder's characters end with a text.</summary>
        /// <param name="builder">The builder.</param>
        /// <param name="text">The text.</param>
        /// <returns>True when they do.</returns>
        private static bool EndsWith(StringBuilder builder, string text)
        {
            return builder.Length >= text.Length
                && builder.ToString(builder.Length - text.Length, text.Length).Equals(text, StringComparison.Ordinal);
        }

        /// <summary>
        ///     The decoder, made at the first read in the encoding the part's bounds are scanned in,
        ///     with the part's XML declaration, when it has one, read ahead and checked.
        /// </summary>
        /// <returns>The decoder.</returns>
        /// <exception cref="XmlException">The declaration names another encoding or does not close, or the bytes are not valid.</exception>
        private StreamReader Text()
        {
            if (_text is not null)
            {
                return _text;
            }

            // Byte order marks are not looked for: the encoding is the scan's, and it skips its own mark.
            _text = new StreamReader(_part, _part.DetectEncoding(), detectEncodingFromByteOrderMarks: false, BufferSize, leaveOpen: false);
            _head = ReadDeclaration(_text);
            return _text;
        }
    }
}
