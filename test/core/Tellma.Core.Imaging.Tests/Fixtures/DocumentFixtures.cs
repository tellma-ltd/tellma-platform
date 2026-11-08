// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Tellma.Core.Imaging.Tests.Fixtures
{
    /// <summary>
    ///     Generates the PDFs and the OOXML file the thumbnail suites render, with correct
    ///     cross-reference tables, so PDFium parses them as written rather than by recovery.
    /// </summary>
    internal static class DocumentFixtures
    {
        /// <summary>
        ///     A one-page PDF of <paramref name="width" /> x <paramref name="height" /> points whose
        ///     left half is filled black and right half left blank.
        /// </summary>
        /// <param name="width">The page width in points.</param>
        /// <param name="height">The page height in points.</param>
        /// <returns>The PDF.</returns>
        public static byte[] HalfBlackPage(int width, int height)
        {
            string content = string.Create(CultureInfo.InvariantCulture, $"0 0 0 rg 0 0 {width / 2} {height} re f");
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 {width} {height}] /Contents 4 0 R >>"),
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"),
            ],
            trailerExtra: string.Empty);
        }

        /// <summary>
        ///     A one-page PDF under the standard security handler (RC4, revision 2) whose user
        ///     password is not empty: it cannot be opened without the password.
        /// </summary>
        /// <returns>The PDF.</returns>
        public static byte[] PasswordProtected()
        {
            // Arbitrary O and U values: no empty password reproduces this U, so the open fails on
            // the password, as a document encrypted with a real password does.
            string o = new('1', 64);
            string u = new('2', 64);
            string id = new('3', 32);
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] >>",
                $"<< /Filter /Standard /V 1 /R 2 /O <{o}> /U <{u}> /P -44 >>",
            ],
            trailerExtra: $" /Encrypt 4 0 R /ID [<{id}> <{id}>]");
        }

        /// <summary>A PDF whose page tree is empty.</summary>
        /// <returns>The PDF.</returns>
        public static byte[] NoPages()
        {
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [] /Count 0 >>",
            ],
            trailerExtra: string.Empty);
        }

        /// <summary>A PDF header followed by bytes that are no PDF at all.</summary>
        /// <returns>The bytes.</returns>
        public static byte[] Garbage()
        {
            byte[] junk = new byte[4096];
            new Random(5).NextBytes(junk);
            return [.. "%PDF-1.7\n"u8, .. junk];
        }

        /// <summary>A minimal Word document: a zip with a content-types part and a document part.</summary>
        /// <returns>The document.</returns>
        public static byte[] WordDocument()
        {
            using var stream = new MemoryStream();
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
            {
                Write(zip, "[Content_Types].xml", "<?xml version=\"1.0\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
                Write(zip, "word/document.xml", "<?xml version=\"1.0\"?><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>");
            }

            return stream.ToArray();

            static void Write(ZipArchive zip, string name, string text)
            {
                using Stream entry = zip.CreateEntry(name).Open();
                entry.Write(Encoding.UTF8.GetBytes(text));
            }
        }

        /// <summary>Lays out numbered objects, the cross-reference table and the trailer.</summary>
        /// <param name="objects">The objects, numbered from 1.</param>
        /// <param name="trailerExtra">Extra trailer entries, with a leading space.</param>
        /// <returns>The PDF.</returns>
        private static byte[] Build(string[] objects, string trailerExtra)
        {
            var text = new StringBuilder("%PDF-1.4\n");
            var offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++)
            {
                offsets.Add(text.Length);
                text.Append(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n{objects[i]}\nendobj\n");
            }

            int xref = text.Length;
            text.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n");
            foreach (int offset in offsets)
            {
                text.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
            }

            text.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R{trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n");
            return Encoding.ASCII.GetBytes(text.ToString());
        }
    }
}
