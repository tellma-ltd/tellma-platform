// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text;
using Tellma.Core.Abstractions.Blobs;
using Tellma.Core.Blobs;

namespace Tellma.Core.Tests.Blobs
{
    /// <summary>Content-type determination over fixture byte prefixes: signatures, zip documents, text, and what is never stored.</summary>
    public class BlobContentSnifferTests
    {
        private static readonly IReadOnlyList<string> Attachment = BlobKindPolicy.Attachment.AllowedContentTypes;
        private static readonly IReadOnlyList<string> Images = BlobKindPolicy.Avatar.AllowedContentTypes;

        // Fixture prefixes: real signatures followed by plausible bytes.
        private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8, 0x00];
        private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. "IHDR"u8];
        private static readonly byte[] Gif87 = [.. "GIF87a"u8, 0x01, 0x00, 0x01, 0x00];
        private static readonly byte[] Gif89 = [.. "GIF89a"u8, 0x01, 0x00, 0x01, 0x00];
        private static readonly byte[] WebP = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];
        private static readonly byte[] Pdf = [.. "%PDF-1.7\n%\xE2\xE3\xCF\xD3\n"u8];

        // A zip archive's local file header, as every OOXML document starts.
        private static readonly byte[] OoxmlZip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, .. "[Content_Types].xml"u8];

        [Theory]
        [MemberData(nameof(Signatures))]
        public void A_signature_decides_the_type_whatever_was_declared(byte[] head, string expected)
        {
            foreach (string? declared in new[] { null, "text/plain", "application/octet-stream", "image/png", "garbage" })
            {
                ContentTypeDecision decision = BlobContentSniffer.Determine(head, declared, Attachment);

                Assert.True(decision.IsAccepted, $"declared {declared ?? "nothing"}");
                Assert.Equal(expected, decision.ContentType);
            }
        }

        public static TheoryData<byte[], string> Signatures()
        {
            return new()
            {
                { Jpeg, "image/jpeg" },
                { Png, "image/png" },
                { Gif87, "image/gif" },
                { Gif89, "image/gif" },
                { WebP, "image/webp" },
                { Pdf, "application/pdf" },
            };
        }

        [Fact]
        public void A_sniffed_type_the_kind_does_not_allow_is_refused()
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine(Pdf, "application/pdf", Images);

            Assert.Equal(ContentTypeRefusal.NotAllowed, decision.Refusal);
            Assert.Equal("application/pdf", decision.ContentType);
        }

        [Theory]
        [InlineData("application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
        [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
        [InlineData("application/vnd.openxmlformats-officedocument.presentationml.presentation")]
        [InlineData("application/zip")]
        public void A_zip_archive_takes_a_declared_ooxml_or_zip_type(string declared)
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine(OoxmlZip, declared.ToUpperInvariant() + "; charset=binary", Attachment);

            Assert.True(decision.IsAccepted);
            Assert.Equal(declared, decision.ContentType);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("application/octet-stream")]
        [InlineData("text/plain")]
        [InlineData("application/x-zip-compressed")]
        [InlineData("application/java-archive")]
        public void A_zip_archive_with_any_other_declaration_is_a_plain_zip(string? declared)
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine(OoxmlZip, declared, Attachment);

            Assert.True(decision.IsAccepted);
            Assert.Equal("application/zip", decision.ContentType);
        }

        [Fact]
        public void A_zip_archive_is_refused_when_zip_is_not_allowed()
        {
            string[] allowed = ["application/pdf", "image/png"];

            ContentTypeDecision plain = BlobContentSniffer.Determine(OoxmlZip, null, allowed);
            ContentTypeDecision declared = BlobContentSniffer.Determine(
                OoxmlZip, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", allowed);

            Assert.Equal(ContentTypeRefusal.NotAllowed, plain.Refusal);
            Assert.Equal(ContentTypeRefusal.NotAllowed, declared.Refusal);
        }

        [Theory]
        [InlineData("text/plain")]
        [InlineData("text/csv")]
        [InlineData("application/json")]
        [InlineData("Text/CSV; charset=utf-8")]
        [InlineData("  application/json ; charset=utf-8")]
        public void Text_takes_its_declared_type_when_allowed_and_free_of_nul(string declared)
        {
            byte[] text = Encoding.UTF8.GetBytes("id,name\n1,Ünïcode\n");

            ContentTypeDecision decision = BlobContentSniffer.Determine(text, declared, Attachment);

            Assert.True(decision.IsAccepted);
            Assert.Equal(BlobContentSniffer.ParseMediaType(declared), decision.ContentType);
        }

        [Fact]
        public void Text_with_a_nul_byte_in_the_first_8_kib_is_refused()
        {
            byte[] text = [.. "hello"u8, 0x00, .. "world"u8];

            ContentTypeDecision decision = BlobContentSniffer.Determine(text, "text/plain", Attachment);

            Assert.Equal(ContentTypeRefusal.Unrecognised, decision.Refusal);
            Assert.Equal("text/plain", decision.ContentType);
        }

        [Fact]
        public void A_nul_byte_beyond_the_first_8_kib_is_not_inspected()
        {
            byte[] text = [.. Enumerable.Repeat((byte)'a', BlobContentSniffer.SniffLength), 0x00];

            Assert.True(BlobContentSniffer.Determine(text, "text/plain", Attachment).IsAccepted);
        }

        [Fact]
        public void Text_is_refused_when_its_declared_type_is_not_allowed()
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine("hello"u8, "text/plain", Images);

            Assert.Equal(ContentTypeRefusal.NotAllowed, decision.Refusal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("application/octet-stream")]
        [InlineData("image/png")]
        [InlineData("text/xml")]
        [InlineData("not a media type")]
        public void Bytes_with_no_signature_and_no_text_declaration_are_refused(string? declared)
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine("just some bytes"u8, declared, Attachment);

            Assert.Equal(ContentTypeRefusal.Unrecognised, decision.Refusal);
        }

        [Theory]
        [MemberData(nameof(AlwaysRejected))]
        public void Executables_html_and_svg_are_refused_whatever_is_declared_or_allowed(byte[] head)
        {
            string[] everything = [.. Attachment, "text/html", "image/svg+xml", "application/octet-stream", "application/x-msdownload"];
            foreach (string? declared in new[] { null, "text/plain", "image/png", "application/pdf", "application/zip", "text/html", "image/svg+xml" })
            {
                ContentTypeDecision decision = BlobContentSniffer.Determine(head, declared, everything);

                Assert.Equal(ContentTypeRefusal.AlwaysRejected, decision.Refusal);
            }
        }

        public static TheoryData<byte[]> AlwaysRejected()
        {
            return new()
            {
                // Native executables: PE, ELF, and Mach-O in every byte order plus the universal container.
                { [.. "MZ"u8, 0x90, 0x00, 0x03, 0x00] },
                { [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01] },
                { [0xFE, 0xED, 0xFA, 0xCE, 0x00] },
                { [0xFE, 0xED, 0xFA, 0xCF, 0x00] },
                { [0xCE, 0xFA, 0xED, 0xFE, 0x00] },
                { [0xCF, 0xFA, 0xED, 0xFE, 0x00] },
                { [0xCA, 0xFE, 0xBA, 0xBE, 0x00] },

                // HTML, after leading whitespace, in any case, behind a byte-order mark, in UTF-16.
                { "<!DOCTYPE html><html></html>"u8.ToArray() },
                { " \t\r\n\f  <!doctype HTML>"u8.ToArray() },
                { "\n\n<html lang=\"en\">"u8.ToArray() },
                { "<HTML>"u8.ToArray() },
                { "<script>alert(1)</script>"u8.ToArray() },
                { "<body onload=x()>"u8.ToArray() },
                { "<iframe src=x>"u8.ToArray() },
                { "<!-- a comment --><p>"u8.ToArray() },
                { "<p>hello</p>"u8.ToArray() },
                { "<br/>"u8.ToArray() },
                { [0xEF, 0xBB, 0xBF, .. "<html>"u8] },
                { [0xFF, 0xFE, .. Encoding.Unicode.GetBytes("  <html>")] },
                { [0xFE, 0xFF, .. Encoding.BigEndianUnicode.GetBytes("<svg/>")] },

                // SVG, bare and behind an XML prolog (with a doctype between them), in any case.
                { "<svg xmlns=\"http://www.w3.org/2000/svg\"/>"u8.ToArray() },
                { "  <SVG>"u8.ToArray() },
                { "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE svg PUBLIC \"-//W3C//DTD SVG 1.1//EN\" \"x\">\n<svg>"u8.ToArray() },
                { "<?xml version=\"1.0\"?><!-- drawn by hand --><Svg>"u8.ToArray() },
                { "<!DOCTYPE svg><svg/>"u8.ToArray() },
            };
        }

        [Fact]
        public void Text_that_merely_mentions_markup_is_not_refused()
        {
            byte[] text = "Remember to escape <html> and <svg> in the template.\n"u8.ToArray();

            Assert.True(BlobContentSniffer.Determine(text, "text/plain", Attachment).IsAccepted);
        }

        [Fact]
        public void A_tag_name_must_end_where_the_signature_ends()
        {
            // "<brand" is not "<br", and "<applet" is not "<a".
            Assert.True(BlobContentSniffer.Determine("<brand>Tellma</brand>"u8, "text/plain", Attachment).IsAccepted);
            Assert.True(BlobContentSniffer.Determine("<applet>"u8, "text/plain", Attachment).IsAccepted);
        }

        [Fact]
        public void An_xml_document_without_an_svg_root_is_not_refused_as_svg()
        {
            byte[] xml = "<?xml version=\"1.0\"?><invoice><total>10</total></invoice>"u8.ToArray();

            ContentTypeDecision decision = BlobContentSniffer.Determine(xml, "text/plain", Attachment);

            Assert.True(decision.IsAccepted);
        }

        [Fact]
        public void An_svg_beyond_the_first_8_kib_is_not_inspected()
        {
            byte[] xml = [.. "<?xml version=\"1.0\"?>"u8, .. Enumerable.Repeat((byte)' ', BlobContentSniffer.SniffLength), .. "<svg>"u8];

            Assert.True(BlobContentSniffer.Determine(xml, "text/plain", Attachment).IsAccepted);
        }

        [Fact]
        public void An_html_polyglot_behind_a_jpeg_signature_is_a_jpeg()
        {
            // The image processor's re-encode is what neutralises the payload; the sniffer's job is
            // only to name the format the signature claims.
            byte[] polyglot = [.. Jpeg, .. "<html><script>alert(1)</script></html>"u8];

            ContentTypeDecision decision = BlobContentSniffer.Determine(polyglot, "text/html", Images);

            Assert.True(decision.IsAccepted);
            Assert.Equal("image/jpeg", decision.ContentType);
        }

        [Fact]
        public void An_empty_file_is_refused_unless_declared_as_text()
        {
            Assert.Equal(ContentTypeRefusal.Unrecognised, BlobContentSniffer.Determine([], null, Attachment).Refusal);
            Assert.True(BlobContentSniffer.Determine([], "text/plain", Attachment).IsAccepted);
        }

        [Fact]
        public void A_refusal_without_a_usable_declaration_describes_the_file_as_octet_stream()
        {
            ContentTypeDecision decision = BlobContentSniffer.Determine("?"u8, "bogus", Attachment);

            Assert.Equal("application/octet-stream", decision.ContentType);
        }

        [Theory]
        [InlineData("image/png", "image/png")]
        [InlineData("IMAGE/PNG", "image/png")]
        [InlineData(" text/plain ; charset=utf-8", "text/plain")]
        [InlineData("application/vnd.ms-excel;x=y", "application/vnd.ms-excel")]
        [InlineData("text/plain;", "text/plain")]
        [InlineData("text", null)]
        [InlineData("/plain", null)]
        [InlineData("text/", null)]
        [InlineData("text/pl ain", null)]
        [InlineData("text/plain/x", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void A_declaration_is_parsed_as_a_media_type_without_parameters(string? value, string? expected)
        {
            Assert.Equal(expected, BlobContentSniffer.ParseMediaType(value));
        }
    }
}
