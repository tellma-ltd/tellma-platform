// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     Reads a zip archive's end-of-central-directory record, and its zip64 counterpart whenever
    ///     one is located, before the archive is opened: the disks, the entry counts and the
    ///     directory's size, which the archive reader would otherwise load into memory whole. A
    ///     workbook holds tens of parts, so a directory of millions of entries is refused before a
    ///     byte of it is parsed, whichever record the archive reader would take it from.
    /// </summary>
    internal static class ZipDirectory
    {
        /// <summary>The most entries a workbook's archive may list.</summary>
        public const int MaxEntries = 65_535;

        /// <summary>The largest central directory a workbook's archive may carry, in bytes.</summary>
        public const long MaxDirectoryBytes = 16L * 1024 * 1024;

        /// <summary>The signature of the end-of-central-directory record.</summary>
        private const uint EndSignature = 0x06054b50;

        /// <summary>The signature of the zip64 end-of-central-directory locator.</summary>
        private const uint Zip64LocatorSignature = 0x07064b50;

        /// <summary>The signature of the zip64 end-of-central-directory record.</summary>
        private const uint Zip64EndSignature = 0x06064b50;

        /// <summary>The fixed length of the end-of-central-directory record, without its comment.</summary>
        private const int EndLength = 22;

        /// <summary>The length of the zip64 locator, which sits just before the end record.</summary>
        private const int Zip64LocatorLength = 20;

        /// <summary>The fixed length of the zip64 end record read here.</summary>
        private const int Zip64EndLength = 56;

        /// <summary>The longest comment the end record may carry.</summary>
        private const int MaxCommentLength = ushort.MaxValue;

        /// <summary>
        ///     Checks the archive's directory: one disk, at most <see cref="MaxEntries" /> entries and at
        ///     most <see cref="MaxDirectoryBytes" /> of directory. Leaves the stream's position where it
        ///     found it.
        /// </summary>
        /// <param name="archive">The archive: readable and seekable.</param>
        /// <exception cref="Abstractions.Excel.ImportException">The archive has no end record, spans disks, or its directory is out of bounds.</exception>
        public static void Check(Stream archive)
        {
            ArgumentNullException.ThrowIfNull(archive);
            long position = archive.Position;
            try
            {
                // --- The end record: the last signature in the tail whose comment fits, as the archive reader finds it ---
                long length = archive.Length;
                int tailLength = (int)Math.Min(length, EndLength + MaxCommentLength);
                byte[] tail = Read(archive, length - tailLength, tailLength);
                int end = -1;
                for (int at = tailLength - EndLength; at >= 0; at--)
                {
                    if (BinaryPrimitives.ReadUInt32LittleEndian(tail.AsSpan(at)) == EndSignature
                        && at + EndLength + BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(at + 20)) <= tailLength)
                    {
                        end = at;
                        break;
                    }
                }

                if (end < 0)
                {
                    throw ImportFaults.Malformed(ImportFaults.ZipReason);
                }

                ReadOnlySpan<byte> record = tail.AsSpan(end, EndLength);
                ushort disk = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
                ushort directoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
                ushort entries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
                uint directoryBytes = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
                uint directoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);

                // --- The end record's own fields, each unless it is at its maximum and so defers to the zip64 record ---
                if ((disk != 0 && disk != ushort.MaxValue)
                    || (directoryDisk != 0 && directoryDisk != ushort.MaxValue)
                    || (directoryBytes != uint.MaxValue && directoryBytes > MaxDirectoryBytes))
                {
                    throw ImportFaults.Malformed(ImportFaults.ZipReason);
                }

                // --- The zip64 record, whenever its locator precedes the end record: the archive reader
                // may take any of its fields over the end record's, so both are held to the bounds. A
                // field at its maximum, the directory's offset among them, needs the zip64 record ---
                long recordAt = length - tailLength + end;
                bool deferred = entries == ushort.MaxValue || directoryBytes == uint.MaxValue || directoryOffset == uint.MaxValue
                    || disk == ushort.MaxValue || directoryDisk == ushort.MaxValue;
                byte[]? locator = recordAt >= Zip64LocatorLength ? Read(archive, recordAt - Zip64LocatorLength, Zip64LocatorLength) : null;
                if (locator is null || BinaryPrimitives.ReadUInt32LittleEndian(locator) != Zip64LocatorSignature)
                {
                    if (deferred)
                    {
                        throw ImportFaults.Malformed(ImportFaults.ZipReason);
                    }

                    return;
                }

                ulong zip64At = BinaryPrimitives.ReadUInt64LittleEndian(locator.AsSpan(8));
                if (zip64At > (ulong)(length - Zip64EndLength))
                {
                    throw ImportFaults.Malformed(ImportFaults.ZipReason);
                }

                byte[] zip64 = Read(archive, (long)zip64At, Zip64EndLength);
                if (BinaryPrimitives.ReadUInt32LittleEndian(zip64) != Zip64EndSignature
                    || BinaryPrimitives.ReadUInt32LittleEndian(zip64.AsSpan(16)) != 0
                    || BinaryPrimitives.ReadUInt32LittleEndian(zip64.AsSpan(20)) != 0
                    || BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(24)) > MaxEntries
                    || BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(32)) > MaxEntries
                    || BinaryPrimitives.ReadUInt64LittleEndian(zip64.AsSpan(40)) > MaxDirectoryBytes)
                {
                    throw ImportFaults.Malformed(ImportFaults.ZipReason);
                }
            }
            finally
            {
                archive.Position = position;
            }
        }

        /// <summary>Reads exactly a range of the archive.</summary>
        /// <param name="archive">The archive.</param>
        /// <param name="offset">Where the range starts.</param>
        /// <param name="count">How many bytes it holds.</param>
        /// <returns>The bytes.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">The archive ends before the range does.</exception>
        private static byte[] Read(Stream archive, long offset, int count)
        {
            byte[] buffer = new byte[count];
            archive.Position = offset;
            try
            {
                archive.ReadExactly(buffer);
            }
            catch (EndOfStreamException)
            {
                throw ImportFaults.Malformed(ImportFaults.ZipReason);
            }

            return buffer;
        }
    }
}
