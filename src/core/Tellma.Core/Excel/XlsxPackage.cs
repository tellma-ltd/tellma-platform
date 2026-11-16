// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     An <c>.xlsx</c> package written forward-only: a zip archive in create mode whose parts are
    ///     streamed one after the other, with the content types and relationships the workbook needs
    ///     written by hand. Nothing is buffered beyond the part being written, so a million-row sheet
    ///     costs no more memory than a ten-row one.
    /// </summary>
    /// <remarks>
    ///     The package holds the workbook, its styles and worksheets <c>sheet1.xml</c> to
    ///     <c>sheetN.xml</c>, the workbook's relationships numbering the worksheets <c>rId1</c> to
    ///     <c>rIdN</c> and the styles <c>rIdN+1</c>.
    /// </remarks>
    internal sealed class XlsxPackage : IDisposable
    {
        /// <summary>The path of the workbook part.</summary>
        public const string WorkbookPath = "xl/workbook.xml";

        /// <summary>The path of the styles part.</summary>
        public const string StylesPath = "xl/styles.xml";

        /// <summary>The content types namespace.</summary>
        private const string ContentTypesNamespace = "http://schemas.openxmlformats.org/package/2006/content-types";

        /// <summary>The relationships namespace.</summary>
        private const string RelationshipsNamespace = "http://schemas.openxmlformats.org/package/2006/relationships";

        /// <summary>The relationship type prefix of the office document relationships.</summary>
        private const string RelationshipTypes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

        /// <summary>The settings of every hand-written part: UTF-8 without a byte order mark, standalone.</summary>
        private static readonly XmlWriterSettings XmlSettings = new() { Encoding = new UTF8Encoding(false), CloseOutput = false };

        /// <summary>The archive.</summary>
        private readonly ZipArchive _zip;

        /// <summary>The time every entry is stamped with: the request's, never the clock's.</summary>
        private readonly DateTimeOffset _timestamp;

        /// <summary>
        ///     Opens a package over a writable stream. The first write the stream refuses is thrown and
        ///     every later write dropped, so the parts closed while that failure propagates cannot throw
        ///     over it; the package is then abandoned, never disposed.
        /// </summary>
        /// <param name="destination">The stream; left open when the package is disposed.</param>
        /// <param name="timestamp">The time every entry is stamped with.</param>
        public XlsxPackage(Stream destination, DateTimeOffset timestamp)
        {
            ArgumentNullException.ThrowIfNull(destination);
            _zip = new ZipArchive(new LatchingStream(destination), ZipArchiveMode.Create, leaveOpen: true);

            // A zip entry's time is a DOS time, which starts in 1980.
            DateTimeOffset floor = new(1980, 1, 1, 0, 0, 0, TimeSpan.Zero);
            _timestamp = timestamp < floor ? floor : timestamp;
        }

        /// <summary>The path of a worksheet part.</summary>
        /// <param name="number">The worksheet's one-based number.</param>
        /// <returns>The path.</returns>
        public static string WorksheetPath(int number)
        {
            return string.Create(CultureInfo.InvariantCulture, $"xl/worksheets/sheet{number}.xml");
        }

        /// <summary>The relationship id of a worksheet in the workbook's relationships.</summary>
        /// <param name="number">The worksheet's one-based number.</param>
        /// <returns>The id.</returns>
        public static string WorksheetRelationshipId(int number)
        {
            return string.Create(CultureInfo.InvariantCulture, $"rId{number}");
        }

        /// <summary>Opens a part for writing; the previous part must be closed first.</summary>
        /// <param name="path">The part's path in the package.</param>
        /// <returns>The part's stream, written forward-only.</returns>
        public Stream CreatePart(string path)
        {
            ZipArchiveEntry entry = _zip.CreateEntry(path, CompressionLevel.Optimal);
            entry.LastWriteTime = _timestamp;
            return entry.Open();
        }

        /// <summary>Writes the package's content types and relationships for a workbook of some worksheets.</summary>
        /// <param name="worksheets">The number of worksheets.</param>
        public void WriteStructure(int worksheets)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(worksheets);

            // --- [Content_Types].xml: XML and relationship parts by default, each main part by name ---
            WritePart("[Content_Types].xml", writer =>
            {
                writer.WriteStartElement("Types", ContentTypesNamespace);
                Default(writer, "rels", "application/vnd.openxmlformats-package.relationships+xml");
                Default(writer, "xml", "application/xml");
                Override(writer, "/" + WorkbookPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
                Override(writer, "/" + StylesPath, "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
                for (int number = 1; number <= worksheets; number++)
                {
                    Override(writer, "/" + WorksheetPath(number), "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
                }

                writer.WriteEndElement();
            });

            // --- _rels/.rels: the package's one relationship, to the workbook ---
            WritePart("_rels/.rels", writer =>
            {
                writer.WriteStartElement("Relationships", RelationshipsNamespace);
                Relationship(writer, "rId1", "officeDocument", WorkbookPath);
                writer.WriteEndElement();
            });

            // --- xl/_rels/workbook.xml.rels: the worksheets, then the styles ---
            WritePart("xl/_rels/workbook.xml.rels", writer =>
            {
                writer.WriteStartElement("Relationships", RelationshipsNamespace);
                for (int number = 1; number <= worksheets; number++)
                {
                    Relationship(writer, WorksheetRelationshipId(number), "worksheet", string.Create(CultureInfo.InvariantCulture, $"worksheets/sheet{number}.xml"));
                }

                Relationship(writer, WorksheetRelationshipId(worksheets + 1), "styles", "styles.xml");
                writer.WriteEndElement();
            });
        }

        /// <summary>Closes the archive, writing its central directory.</summary>
        public void Dispose()
        {
            _zip.Dispose();
        }

        /// <summary>Writes a small part through an XML writer.</summary>
        /// <param name="path">The part's path.</param>
        /// <param name="body">Writes the part's root element.</param>
        private void WritePart(string path, Action<XmlWriter> body)
        {
            using Stream stream = CreatePart(path);
            using var writer = XmlWriter.Create(stream, XmlSettings);
            writer.WriteStartDocument(standalone: true);
            body(writer);
            writer.WriteEndDocument();
        }

        /// <summary>Writes a default content type by extension.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="extension">The extension.</param>
        /// <param name="contentType">The content type.</param>
        private static void Default(XmlWriter writer, string extension, string contentType)
        {
            writer.WriteStartElement("Default", ContentTypesNamespace);
            writer.WriteAttributeString("Extension", extension);
            writer.WriteAttributeString("ContentType", contentType);
            writer.WriteEndElement();
        }

        /// <summary>Writes a content type for one part.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="partName">The part's name, from the package root.</param>
        /// <param name="contentType">The content type.</param>
        private static void Override(XmlWriter writer, string partName, string contentType)
        {
            writer.WriteStartElement("Override", ContentTypesNamespace);
            writer.WriteAttributeString("PartName", partName);
            writer.WriteAttributeString("ContentType", contentType);
            writer.WriteEndElement();
        }

        /// <summary>Writes a relationship.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="id">The relationship's id.</param>
        /// <param name="type">The relationship type's last segment.</param>
        /// <param name="target">The target, relative to the source part's folder.</param>
        private static void Relationship(XmlWriter writer, string id, string type, string target)
        {
            writer.WriteStartElement("Relationship", RelationshipsNamespace);
            writer.WriteAttributeString("Id", id);
            writer.WriteAttributeString("Type", RelationshipTypes + type);
            writer.WriteAttributeString("Target", target);
            writer.WriteEndElement();
        }

        /// <summary>
        ///     The destination as the archive sees it: every call passes through until a write or
        ///     flush throws, which is rethrown; from then on writes and flushes are dropped, since the
        ///     package is lost and only the first failure says why.
        /// </summary>
        /// <param name="inner">The destination; never disposed here.</param>
        private sealed class LatchingStream(Stream inner) : Stream
        {
            /// <summary>Whether a write or flush has failed.</summary>
            private bool _faulted;

            /// <inheritdoc />
            public override bool CanRead => false;

            /// <inheritdoc />
            public override bool CanSeek => inner.CanSeek;

            /// <inheritdoc />
            public override bool CanWrite => inner.CanWrite;

            /// <inheritdoc />
            public override long Length => inner.Length;

            /// <inheritdoc />
            public override long Position
            {
                get => inner.Position;
                set => inner.Position = value;
            }

            /// <inheritdoc />
            public override void Flush()
            {
                if (_faulted)
                {
                    return;
                }

                try
                {
                    inner.Flush();
                }
                catch
                {
                    _faulted = true;
                    throw;
                }
            }

            /// <inheritdoc />
            public override void Write(byte[] buffer, int offset, int count)
            {
                Write(buffer.AsSpan(offset, count));
            }

            /// <inheritdoc />
            public override void Write(ReadOnlySpan<byte> buffer)
            {
                if (_faulted)
                {
                    return;
                }

                try
                {
                    inner.Write(buffer);
                }
                catch
                {
                    _faulted = true;
                    throw;
                }
            }

            /// <inheritdoc />
            public override void WriteByte(byte value)
            {
                Write([value]);
            }

            /// <inheritdoc />
            public override long Seek(long offset, SeekOrigin origin)
            {
                return inner.Seek(offset, origin);
            }

            /// <inheritdoc />
            public override void SetLength(long value)
            {
                inner.SetLength(value);
            }

            /// <inheritdoc />
            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }
        }
    }
}
