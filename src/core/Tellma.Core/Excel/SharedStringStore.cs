// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Xml;

namespace Tellma.Core.Excel
{
    /// <summary>
    ///     A workbook's shared-string table, read once and looked up by index. Each entry is charged
    ///     its UTF-16 text bytes plus a fixed overhead against a memory budget, so a flood of empty
    ///     entries counts like text. Within the budget the strings stay in memory; beyond it every
    ///     string moves to a scratch file, located through a fixed-width index in a second one, so
    ///     resident memory stays bounded whatever the table holds. A table above the entry cap is
    ///     refused.
    /// </summary>
    internal sealed class SharedStringStore : IDisposable
    {
        /// <summary>The fixed charge of one entry beyond its text: the reference and the object header it costs.</summary>
        public const int EntryOverhead = 16;

        /// <summary>The width of one entry of the spilled index: the text's offset and its length in characters.</summary>
        private const int IndexWidth = sizeof(long) + sizeof(int);

        /// <summary>How many spilled strings the lookup keeps, by index modulo this size.</summary>
        private const int CacheSize = 4096;

        /// <summary>The longest spilled string the lookup keeps, in characters.</summary>
        private const int MaxCachedLength = 1024;

        /// <summary>The resident strings, until the table spills.</summary>
        private List<string>? _resident = [];

        /// <summary>The spilled texts, UTF-16 back to back; null until the table spills.</summary>
        private FileStream? _texts;

        /// <summary>The spilled index; null until the table spills.</summary>
        private FileStream? _index;

        /// <summary>The recent spilled strings, by index modulo the cache size; null until the table spills.</summary>
        private (int Index, string? Text)[]? _cache;

        /// <summary>Creates an empty table.</summary>
        private SharedStringStore()
        {
        }

        /// <summary>How many entries the table holds.</summary>
        public int Count { get; private set; }

        /// <summary>The bytes the entries were charged, text and overhead together.</summary>
        public long ChargedBytes { get; private set; }

        /// <summary>Whether the table moved to scratch files.</summary>
        public bool Spilled => _texts is not null;

        /// <summary>An empty table, for a workbook without one.</summary>
        /// <returns>The table.</returns>
        public static SharedStringStore Empty()
        {
            return new SharedStringStore();
        }

        /// <summary>Reads a shared-string part.</summary>
        /// <param name="part">The part's stream, bounded; closed when the table is read.</param>
        /// <param name="budget">The bytes the entries may be charged before the table spills.</param>
        /// <param name="maxEntries">The most entries the table may hold.</param>
        /// <param name="scratchDirectory">The directory the spilled files go to.</param>
        /// <returns>The table.</returns>
        /// <exception cref="Abstractions.Excel.ImportException">
        ///     The part holds more than <paramref name="maxEntries" /> entries, is not well-formed, or an
        ///     entry joins to more text than one string may hold.
        /// </exception>
        public static SharedStringStore Load(Stream part, long budget, int maxEntries, string scratchDirectory)
        {
            ArgumentNullException.ThrowIfNull(part);
            ArgumentException.ThrowIfNullOrWhiteSpace(scratchDirectory);
            ArgumentOutOfRangeException.ThrowIfNegative(budget);
            ArgumentOutOfRangeException.ThrowIfNegative(maxEntries);

            SharedStringStore store = new();
            try
            {
                using XmlReader reader = XlsxXml.Open(part);
                long entries = 0;
                while (reader.Read())
                {
                    // --- The declared count refuses a table above the cap before any entry is read ---
                    if (XlsxXml.IsSpreadsheet(reader, "sst")
                        && long.TryParse(reader.GetAttribute("uniqueCount"), out long declared)
                        && declared > maxEntries)
                    {
                        throw ImportFaults.TooManySharedStrings(declared, maxEntries);
                    }

                    if (!XlsxXml.IsSpreadsheet(reader, "si"))
                    {
                        continue;
                    }

                    // --- Each entry: kept until the cap, then only counted, so the refusal says how many there are ---
                    entries++;
                    if (entries > maxEntries)
                    {
                        reader.Skip();
                        while (!reader.EOF)
                        {
                            if (XlsxXml.IsSpreadsheet(reader, "si"))
                            {
                                entries++;
                                reader.Skip();
                            }
                            else
                            {
                                _ = reader.Read();
                            }
                        }

                        throw ImportFaults.TooManySharedStrings(entries, maxEntries);
                    }

                    store.Add(XlsxXml.ReadStringItem(reader, sheet: null), budget, scratchDirectory);
                }

                store.Seal();
                return store;
            }
            catch (XmlException)
            {
                store.Dispose();
                throw ImportFaults.Malformed(ImportFaults.XmlReason);
            }
            catch
            {
                store.Dispose();
                throw;
            }
        }

        /// <summary>The string at an index.</summary>
        /// <param name="index">The zero-based index.</param>
        /// <returns>The string; null for an index the table lacks.</returns>
        public string? Get(int index)
        {
            if (index < 0 || index >= Count)
            {
                return null;
            }

            if (_resident is not null)
            {
                return _resident[index];
            }

            // --- Spilled: the cache, else the index entry and the text it locates ---
            ref (int Index, string? Text) cached = ref _cache![index % CacheSize];
            if (cached.Text is not null && cached.Index == index)
            {
                return cached.Text;
            }

            Span<byte> entry = stackalloc byte[IndexWidth];
            _index!.Position = (long)index * IndexWidth;
            _index.ReadExactly(entry);
            long offset = BinaryPrimitives.ReadInt64LittleEndian(entry);
            int length = BinaryPrimitives.ReadInt32LittleEndian(entry[sizeof(long)..]);

            FileStream texts = _texts!;
            string text = string.Create(length, (texts, offset), static (chars, state) =>
            {
                state.texts.Position = state.offset;
                state.texts.ReadExactly(MemoryMarshal.AsBytes(chars));
            });

            if (length <= MaxCachedLength)
            {
                cached = (index, text);
            }

            return text;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            _texts?.Dispose();
            _index?.Dispose();
            _texts = null;
            _index = null;
            _resident = null;
        }

        /// <summary>Adds an entry, spilling the table when its charge passes the budget.</summary>
        /// <param name="text">The entry's text.</param>
        /// <param name="budget">The budget.</param>
        /// <param name="scratchDirectory">The directory the spilled files go to.</param>
        private void Add(string text, long budget, string scratchDirectory)
        {
            ChargedBytes += ((long)text.Length * sizeof(char)) + EntryOverhead;
            if (_resident is not null && ChargedBytes > budget)
            {
                // --- The budget is passed: every string so far moves to the scratch files ---
                _texts = ScratchFiles.Create(scratchDirectory);
                _index = ScratchFiles.Create(scratchDirectory);
                List<string> resident = _resident;
                _resident = null;
                foreach (string earlier in resident)
                {
                    Write(earlier);
                }
            }

            if (_resident is not null)
            {
                _resident.Add(text);
            }
            else
            {
                Write(text);
            }

            Count++;
        }

        /// <summary>Appends a string to the spilled files.</summary>
        /// <param name="text">The string.</param>
        private void Write(string text)
        {
            Span<byte> entry = stackalloc byte[IndexWidth];
            BinaryPrimitives.WriteInt64LittleEndian(entry, _texts!.Position);
            BinaryPrimitives.WriteInt32LittleEndian(entry[sizeof(long)..], text.Length);
            _index!.Write(entry);
            _texts.Write(MemoryMarshal.AsBytes(text.AsSpan()));
        }

        /// <summary>Ends the load: the spilled files flushed for reading and the cache made.</summary>
        private void Seal()
        {
            if (_texts is null)
            {
                return;
            }

            _texts.Flush();
            _index!.Flush();
            _cache = new (int, string?)[CacheSize];
        }
    }
}
