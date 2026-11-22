// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Buffers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;

namespace Tellma.Core.Mcp.Rendering
{
    /// <summary>
    ///     Writes the cells of a row set in the wire's invariant scalar encoding, the encoding the
    ///     web surface's rows carry: as JSON values for the objects rendering, and as their text for
    ///     a Markdown table.
    /// </summary>
    internal sealed class RowCells : IDisposable
    {
        /// <summary>The row set.</summary>
        private readonly QueryRowSet _rows;

        /// <summary>One cell writer per column, chosen once.</summary>
        private readonly Action<Utf8JsonWriter, int>[] _writers;

        /// <summary>The buffer one cell's text is written into.</summary>
        private readonly ArrayBufferWriter<byte> _scratch = new(256);

        /// <summary>The writer over <see cref="_scratch" />.</summary>
        private readonly Utf8JsonWriter _cell;

        /// <summary>Chooses each column's writer.</summary>
        /// <param name="rows">The row set.</param>
        /// <exception cref="JsonException">A column holds cells no row set holds.</exception>
        public RowCells(QueryRowSet rows)
        {
            ArgumentNullException.ThrowIfNull(rows);
            _rows = rows;
            _writers = new Action<Utf8JsonWriter, int>[rows.Columns.Count];
            for (int column = 0; column < _writers.Length; column++)
            {
                _writers[column] = WriterOf(rows.GetBuffer(column), rows.Columns[column]);
            }

            _cell = new Utf8JsonWriter(_scratch, WriterOptions);
        }

        /// <summary>The options of every writer the renderings use: the wire's encoder, unindented.</summary>
        public static JsonWriterOptions WriterOptions { get; } = new() { Encoder = TellmaJson.Options.Encoder, Indented = false };

        /// <summary>The row set's columns.</summary>
        public IReadOnlyList<QueryColumn> Columns => _rows.Columns;

        /// <summary>The row set's row count.</summary>
        public int RowCount => _rows.RowCount;

        /// <inheritdoc />
        public void Dispose()
        {
            _cell.Dispose();
        }

        /// <summary>A cell as text: a string as it is, any other value as the wire writes it.</summary>
        /// <param name="column">The column.</param>
        /// <param name="row">The row.</param>
        /// <returns>The text, or null for an absent value.</returns>
        public string? Text(int column, int row)
        {
            if (_rows.IsNull(column, row))
            {
                return null;
            }

            // --- The cell as the wire writes it ---
            _scratch.ResetWrittenCount();
            _cell.Reset(_scratch);
            _writers[column](_cell, row);
            _cell.Flush();
            ReadOnlySpan<byte> written = _scratch.WrittenSpan;

            // --- A JSON string unquoted and unescaped; a number or a boolean as its literal ---
            if (written.Length > 0 && written[0] == (byte)'"')
            {
                Utf8JsonReader reader = new(written);
                _ = reader.Read();
                return reader.GetString();
            }

            return Encoding.UTF8.GetString(written);
        }

        /// <summary>Writes a row as a JSON object keyed by column name.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="row">The row.</param>
        public void WriteObject(Utf8JsonWriter writer, int row)
        {
            ArgumentNullException.ThrowIfNull(writer);
            writer.WriteStartObject();
            for (int column = 0; column < _writers.Length; column++)
            {
                writer.WritePropertyName(_rows.Columns[column].Name);
                if (_rows.IsNull(column, row))
                {
                    writer.WriteNullValue();
                }
                else
                {
                    _writers[column](writer, row);
                }
            }

            writer.WriteEndObject();
        }

        /// <summary>The writer of a column's cells, by its buffer's type.</summary>
        /// <param name="buffer">The column's buffer.</param>
        /// <param name="column">The column.</param>
        /// <returns>The writer.</returns>
        /// <exception cref="JsonException">The buffer is of a type no row set holds.</exception>
        private static Action<Utf8JsonWriter, int> WriterOf(Array buffer, QueryColumn column)
        {
            return buffer switch
            {
                int[] values => Cells(values),
                long[] values => Cells(values),
                short[] values => Cells(values),
                byte[] values => Cells(values),
                decimal[] values => Cells(values),
                double[] values => Cells(values),
                bool[] values => Cells(values),
                string[] values => Cells(values),
                Guid[] values => Cells(values),
                DateOnly[] values => Cells(values),
                DateTime[] values => Cells(values),
                DateTimeOffset[] values => Cells(values),
                _ => throw new JsonException($"The column '{column.Name}' holds {buffer.GetType().Name} cells, a buffer no row set holds."),
            };
        }

        /// <summary>A column's writer through the wire's converter of its type.</summary>
        /// <typeparam name="T">The cells' type.</typeparam>
        /// <param name="values">The cells.</param>
        /// <returns>The writer.</returns>
        private static Action<Utf8JsonWriter, int> Cells<T>(T[] values)
        {
            JsonSerializerOptions options = TellmaJson.Options;
            var converter = (JsonConverter<T>)options.GetConverter(typeof(T));
            return (writer, row) => converter.Write(writer, values[row], options);
        }
    }
}
