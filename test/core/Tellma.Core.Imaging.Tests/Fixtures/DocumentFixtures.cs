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

        /// <summary>
        ///     A one-page PDF whose page draws an image XObject declaring <paramref name="width" /> x
        ///     <paramref name="height" /> pixels through <paramref name="filter" />, over a few bytes of
        ///     data: the declaration is what a render would allocate for.
        /// </summary>
        /// <param name="width">The declared width.</param>
        /// <param name="height">The declared height.</param>
        /// <param name="filter">The image's filter, such as FlateDecode or JPXDecode.</param>
        /// <returns>The PDF.</returns>
        public static byte[] DeclaringImage(int width, int height, string filter)
        {
            const string content = "q 100 0 0 100 0 0 cm /Im0 Do Q";
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /XObject << /Im0 5 0 R >> >> /Contents 4 0 R >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {content.Length} >>\nstream\n{content}\nendstream"),
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /{filter} /Length 4 >>\nstream\nabcd\nendstream"),
            ],
            trailerExtra: string.Empty);
        }

        /// <summary>
        ///     A one-page PDF whose page draws form 0, which draws form 1 <paramref name="breadth" />
        ///     times, and so on <paramref name="depth" /> levels down to a filled square: a render
        ///     would draw breadth to the power of depth objects.
        /// </summary>
        /// <param name="depth">The number of nested form levels.</param>
        /// <param name="breadth">How often each form draws the next.</param>
        /// <returns>The PDF.</returns>
        public static byte[] FormFanOut(int depth, int breadth)
        {
            // Objects: 1 catalog, 2 pages, 3 page, 4 page content, then forms 5 .. 5 + depth - 1.
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 100] /Resources << /XObject << /F 5 0 R >> >> /Contents 4 0 R >>",
                "<< /Length 5 >>\nstream\n/F Do\nendstream",
            };

            for (int level = 0; level < depth; level++)
            {
                bool leaf = level == depth - 1;
                string body = leaf ? "0 0 1 1 re f" : string.Concat(Enumerable.Repeat("/F Do\n", breadth));
                string resources = leaf ? string.Empty : string.Create(CultureInfo.InvariantCulture, $" /Resources << /XObject << /F {6 + level} 0 R >> >>");
                objects.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"<< /Type /XObject /Subtype /Form /BBox [0 0 100 100]{resources} /Length {body.Length} >>\nstream\n{body}\nendstream"));
            }

            return Build([.. objects], trailerExtra: string.Empty);
        }

        /// <summary>
        ///     A two-page PDF: page 1 is 200 x 100 points with its left half black; page 2 is
        ///     100 x 200 points and entirely black.
        /// </summary>
        /// <returns>The PDF.</returns>
        public static byte[] TwoPages()
        {
            const string first = "0 0 0 rg 0 0 100 100 re f";
            const string second = "0 0 0 rg 0 0 100 200 re f";
            return Build(
            [
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R 5 0 R] /Count 2 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {first.Length} >>\nstream\n{first}\nendstream"),
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 100 200] /Contents 6 0 R >>",
                string.Create(CultureInfo.InvariantCulture, $"<< /Length {second.Length} >>\nstream\n{second}\nendstream"),
            ],
            trailerExtra: string.Empty);
        }

        /// <summary>
        ///     A 200 x 100 point page whose left half is black, encrypted with the standard security
        ///     handler (RC4 40-bit, revision 2) under an empty user password and a non-empty owner
        ///     password: encrypted, yet openable by anyone, as permission-restricted PDFs are.
        /// </summary>
        /// <returns>The PDF.</returns>
        public static byte[] EncryptedWithEmptyUserPassword()
        {
            // Section: the keys of algorithms 2, 3 and 4 of the standard security handler.
            byte[] id = Convert.FromHexString("0123456789ABCDEF0123456789ABCDEF");
            const int permissions = -44;
            byte[] o = Rc4(Md5(Pad("owner-secret"))[..5], Pad(string.Empty));
            byte[] key = Md5([.. Pad(string.Empty), .. o, .. BitConverter.GetBytes(permissions), .. id])[..5];
            byte[] u = Rc4(key, Pad(string.Empty));

            // Section: the content stream (object 4) encrypted with its object key.
            byte[] content = Encoding.ASCII.GetBytes("0 0 0 rg 0 0 100 100 re f");
            byte[] objectKey = Md5([.. key, 4, 0, 0, 0, 0])[..10];
            byte[] encrypted = Rc4(objectKey, content);

            byte[] stream = [.. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"<< /Length {encrypted.Length} >>\nstream\n")), .. encrypted, .. "\nendstream"u8];
            return BuildBytes(
            [
                Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
                Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 200 100] /Contents 4 0 R >>"),
                stream,
                Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"<< /Filter /Standard /V 1 /R 2 /O <{Convert.ToHexString(o)}> /U <{Convert.ToHexString(u)}> /P {permissions} >>")),
            ],
            trailerExtra: $" /Encrypt 5 0 R /ID [<{Convert.ToHexString(id)}> <{Convert.ToHexString(id)}>]");
        }

        /// <summary>A valid PDF followed by padding that takes it past <paramref name="length" /> bytes.</summary>
        /// <param name="length">The length to exceed.</param>
        /// <returns>The bytes.</returns>
        public static byte[] LongerThan(long length)
        {
            byte[] pdf = HalfBlackPage(100, 100);
            byte[] padded = new byte[length + 1];
            pdf.CopyTo(padded, 0);
            padded.AsSpan(pdf.Length).Fill((byte)' ');
            return padded;
        }

        /// <summary>Lays out numbered objects, the cross-reference table and the trailer.</summary>
        /// <param name="objects">The objects, numbered from 1.</param>
        /// <param name="trailerExtra">Extra trailer entries, with a leading space.</param>
        /// <returns>The PDF.</returns>
        private static byte[] Build(string[] objects, string trailerExtra)
        {
            return BuildBytes([.. objects.Select(static o => Encoding.ASCII.GetBytes(o))], trailerExtra);
        }

        /// <summary>Lays out numbered objects given as bytes, the cross-reference table and the trailer.</summary>
        /// <param name="objects">The objects, numbered from 1.</param>
        /// <param name="trailerExtra">Extra trailer entries, with a leading space.</param>
        /// <returns>The PDF.</returns>
        private static byte[] BuildBytes(byte[][] objects, string trailerExtra)
        {
            using var pdf = new MemoryStream();
            Write(pdf, "%PDF-1.4\n");
            var offsets = new List<long>();
            for (int i = 0; i < objects.Length; i++)
            {
                offsets.Add(pdf.Position);
                Write(pdf, string.Create(CultureInfo.InvariantCulture, $"{i + 1} 0 obj\n"));
                pdf.Write(objects[i]);
                Write(pdf, "\nendobj\n");
            }

            long xref = pdf.Position;
            Write(pdf, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {objects.Length + 1}\n0000000000 65535 f \n"));
            foreach (long offset in offsets)
            {
                Write(pdf, string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n"));
            }

            Write(pdf, string.Create(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Length + 1} /Root 1 0 R{trailerExtra} >>\nstartxref\n{xref}\n%%EOF\n"));
            return pdf.ToArray();

            static void Write(MemoryStream stream, string text)
            {
                stream.Write(Encoding.ASCII.GetBytes(text));
            }
        }

        /// <summary>A password padded or truncated to 32 bytes with the standard security handler's padding.</summary>
        /// <param name="password">The password.</param>
        /// <returns>The 32 bytes.</returns>
        private static byte[] Pad(string password)
        {
            byte[] padding = Convert.FromHexString("28BF4E5E4E758A4164004E56FFFA01082E2E00B6D0683E802F0CA9FE6453697A");
            byte[] bytes = Encoding.ASCII.GetBytes(password);
            return [.. bytes.Take(32), .. padding.Take(32 - Math.Min(32, bytes.Length))];
        }

        /// <summary>MD5, which the 40-bit standard security handler is defined over.</summary>
        /// <param name="data">The data.</param>
        /// <returns>The digest.</returns>
#pragma warning disable CA5351 // The PDF format's legacy encryption is built from MD5; this reproduces it and protects nothing.
        private static byte[] Md5(byte[] data)
        {
            return System.Security.Cryptography.MD5.HashData(data);
        }
#pragma warning restore CA5351

        /// <summary>RC4, which the 40-bit standard security handler encrypts with.</summary>
        /// <param name="key">The key.</param>
        /// <param name="data">The data.</param>
        /// <returns>The transformed data.</returns>
        private static byte[] Rc4(byte[] key, byte[] data)
        {
            byte[] s = [.. Enumerable.Range(0, 256).Select(static i => (byte)i)];
            for (int i = 0, j = 0; i < 256; i++)
            {
                j = (j + s[i] + key[i % key.Length]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
            }

            byte[] output = new byte[data.Length];
            for (int n = 0, i = 0, j = 0; n < data.Length; n++)
            {
                i = (i + 1) & 0xFF;
                j = (j + s[i]) & 0xFF;
                (s[i], s[j]) = (s[j], s[i]);
                output[n] = (byte)(data[n] ^ s[(s[i] + s[j]) & 0xFF]);
            }

            return output;
        }
    }
}
