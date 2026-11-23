// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using System.Xml;
using Tellma.Core.Excel;

namespace Tellma.Core.Tests.Excel
{
    /// <summary>
    ///     A part's text as the reader parses it: decoded in exactly the encoding its bounds were
    ///     scanned in, with an XML declaration naming any other encoding refused, and parsed with the
    ///     reader settings every part is read with.
    /// </summary>
    public sealed class PartTextReaderTests
    {
        /// <summary>Registers the code-page encodings for the whole process, as a database client does, so that declarations of them are live.</summary>
        public PartTextReaderTests()
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        [Theory]
        [InlineData("iso-2022-jp")]
        [InlineData("hz-gb-2312")]
        [InlineData("windows-1252")]
        [InlineData("iso-8859-1")]
        [InlineData("us-ascii")]
        [InlineData("utf-32")]
        [InlineData("UTF-16LE")]
        public void A_declaration_of_an_encoding_other_than_utf8_or_utf16_is_refused_though_the_encoding_is_available(string encoding)
        {
            Assert.NotNull(Encoding.GetEncoding(encoding));
            byte[] part = Encoding.ASCII.GetBytes($"<?xml version=\"1.0\" encoding=\"{encoding}\"?><a>b</a>");

            _ = Assert.Throws<XmlException>(() => ReadAll(part));
        }

        [Theory]
        [InlineData("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>", "utf-8")]
        [InlineData("<?xml version='1.0' encoding='utf-8'?>", "utf-8 bom")]
        [InlineData("<?xml version=\"1.0\"?>", "utf-8")]
        [InlineData("", "utf-8")]
        [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", "utf-16le bom")]
        [InlineData("<?xml version=\"1.0\" encoding=\"utf-16\" ?>", "utf-16be bom")]
        [InlineData("<?xml version=\"1.0\" encoding=\"UTF-16\"?>", "utf-16le")]
        public void A_part_declared_or_not_in_utf8_or_utf16_is_read_in_the_encoding_it_was_scanned_in(string declaration, string encoding)
        {
            byte[] part = Encode($"{declaration}<a b=\"é中\">\U0001F600 text</a>", encoding);

            (string name, string attribute, string text) = ReadAll(part);

            Assert.Equal(("a", "é中", "\U0001F600 text"), (name, attribute, text));
        }

        [Fact]
        public void A_declaration_that_does_not_close_within_its_bound_is_refused()
        {
            string spaces = new(' ', PartTextReader.MaxDeclarationLength);
            byte[] part = Encoding.UTF8.GetBytes($"<?xml version=\"1.0\"{spaces} encoding=\"iso-2022-jp\"?><a>b</a>");

            _ = Assert.Throws<XmlException>(() => ReadAll(part));
        }

        [Fact]
        public void A_byte_sequence_not_valid_in_the_parts_encoding_is_refused()
        {
            byte[] part = [.. Encoding.UTF8.GetBytes("<a>"), 0xC3, 0x28, .. Encoding.UTF8.GetBytes("</a>")];

            _ = Assert.Throws<XmlException>(() => ReadAll(part));
        }

        [Fact]
        public void A_part_is_read_with_document_types_prohibited_and_its_own_bounded_name_table()
        {
            byte[] part = Encoding.UTF8.GetBytes("<a>b</a>");
            MemoryStream inner = new(part, writable: false);

            using (XmlReader reader = XlsxXml.Open(new BoundedPartStream(inner, part.Length)))
            {
                XmlReaderSettings settings = reader.Settings!;
                Assert.Equal(
                    (DtdProcessing.Prohibit, true, true, typeof(BoundedNameTable)),
                    (settings.DtdProcessing, settings.IgnoreComments, settings.IgnoreProcessingInstructions, reader.NameTable.GetType()));
            }

            // The reader closes the part, through the text reader it was made over.
            Assert.False(inner.CanRead);
        }

        [Fact]
        public void A_document_type_is_refused()
        {
            byte[] part = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><!DOCTYPE a [<!ENTITY e \"x\">]><a>&e;</a>");

            _ = Assert.Throws<XmlException>(() => ReadAll(part));
        }

        /// <summary>Reads a part to its end through the reader every part is parsed with.</summary>
        /// <param name="part">The part's bytes.</param>
        /// <returns>The first element's name, its <c>b</c> attribute and its text.</returns>
        private static (string Name, string Attribute, string Text) ReadAll(byte[] part)
        {
            using XmlReader reader = XlsxXml.Open(new BoundedPartStream(new MemoryStream(part, writable: false), part.Length));
            _ = reader.MoveToContent();
            string name = reader.LocalName;
            string attribute = reader.GetAttribute("b") ?? string.Empty;
            string text = reader.ReadElementContentAsString();
            while (reader.Read())
            {
            }

            return (name, attribute, text);
        }

        /// <summary>A text in one of the encodings a part's markup is found in.</summary>
        /// <param name="text">The text.</param>
        /// <param name="encoding">UTF-8 or UTF-16 of a byte order, with "bom" when marked.</param>
        /// <returns>The bytes.</returns>
        private static byte[] Encode(string text, string encoding)
        {
            Encoding chosen = encoding.StartsWith("utf-16be", StringComparison.Ordinal) ? Encoding.BigEndianUnicode
                : encoding.StartsWith("utf-16le", StringComparison.Ordinal) ? Encoding.Unicode
                : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            byte[] bytes = chosen.GetBytes(text);
            return encoding.EndsWith("bom", StringComparison.Ordinal) ? [.. chosen.GetPreamble(), .. bytes] : bytes;
        }
    }
}
