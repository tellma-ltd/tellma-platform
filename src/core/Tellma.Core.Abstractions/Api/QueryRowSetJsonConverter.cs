// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Text.Json;
using System.Text.Json.Serialization;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Api
{
    /// <summary>
    ///     Writes a <see cref="QueryRowSet" /> as its columns and its rows, row-major:
    ///     <c>{ "columns": [ { "name", "type", "scale", "nullable", "path", "groupingKey" } ], "rows": [ [ … ] ] }</c>.
    ///     Write-only: a row set is never read from JSON.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>columns</c> is always present, so a client interprets a cell without knowing the
    ///         select that produced it. A column's <c>type</c> is its query type's name without the
    ///         <c>Qx</c> prefix (<c>Numeric</c>, <c>String</c>, <c>HierarchyId</c>); its <c>scale</c>
    ///         is omitted when null and its <c>path</c> when the column is computed; its precision
    ///         is never written.
    ///     </para>
    ///     <para>
    ///         Each cell is written by its buffer's element type through the options' converter for
    ///         that type, chosen once per column, so a cell encodes exactly as an entity property of
    ///         the same type: a <see cref="long" /> or a <see cref="decimal" /> cell is a string, an
    ///         <see cref="int" /> cell a number, a hierarchy cell its path string. A null cell is
    ///         <c>null</c>.
    ///     </para>
    /// </remarks>
    internal sealed class QueryRowSetJsonConverter : JsonConverter<QueryRowSet>
    {
        /// <summary>The wire name of each query type: the member name without its <c>Qx</c> prefix.</summary>
        private static readonly Dictionary<QueryexType, string> TypeNames = Enum.GetValues<QueryexType>()
            .ToDictionary(static type => type, static type => type.ToString()[2..]);

        /// <summary>Writes one non-null cell of a column's buffer.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="row">The row's position.</param>
        private delegate void CellWriter(Utf8JsonWriter writer, int row);

        /// <inheritdoc />
        public override QueryRowSet Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotSupportedException("A query row set is written to JSON only; it is never read from it.");
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, QueryRowSet value, JsonSerializerOptions options)
        {
            ArgumentNullException.ThrowIfNull(writer);
            ArgumentNullException.ThrowIfNull(value);
            ArgumentNullException.ThrowIfNull(options);

            writer.WriteStartObject();

            // --- The columns, always present ---
            writer.WriteStartArray("columns");
            foreach (QueryColumn column in value.Columns)
            {
                WriteColumn(writer, column);
            }

            writer.WriteEndArray();

            // --- The rows: one writer per column, chosen once, then row-major arrays ---
            int columns = value.Columns.Count;
            var cells = new CellWriter[columns];
            for (int column = 0; column < columns; column++)
            {
                cells[column] = CellsOf(value.GetBuffer(column), value.Columns[column], options);
            }

            writer.WriteStartArray("rows");
            for (int row = 0; row < value.RowCount; row++)
            {
                writer.WriteStartArray();
                for (int column = 0; column < columns; column++)
                {
                    if (value.IsNull(column, row))
                    {
                        writer.WriteNullValue();
                    }
                    else
                    {
                        cells[column](writer, row);
                    }
                }

                writer.WriteEndArray();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>Writes one column's description.</summary>
        /// <param name="writer">The writer.</param>
        /// <param name="column">The column.</param>
        private static void WriteColumn(Utf8JsonWriter writer, QueryColumn column)
        {
            writer.WriteStartObject();
            writer.WriteString("name", column.Name);
            writer.WriteString("type", TypeNames.TryGetValue(column.Type, out string? type)
                ? type
                : throw new JsonException($"The column '{column.Name}' has the query type {(int)column.Type}, which the language does not define."));
            if (column.Scale is int scale)
            {
                writer.WriteNumber("scale", scale);
            }

            writer.WriteBoolean("nullable", column.Nullable);
            if (column.Path is IReadOnlyList<string> path)
            {
                writer.WriteStartArray("path");
                foreach (string segment in path)
                {
                    writer.WriteStringValue(segment);
                }

                writer.WriteEndArray();
            }

            writer.WriteBoolean("groupingKey", column.GroupingKey);
            writer.WriteEndObject();
        }

        /// <summary>The cell writer of one column, by its buffer's element type.</summary>
        /// <param name="buffer">The column's buffer.</param>
        /// <param name="column">The column, for the refusal's message.</param>
        /// <param name="options">The options whose converters encode the cells.</param>
        /// <returns>The cell writer.</returns>
        /// <exception cref="JsonException">The buffer is of a type no row set holds.</exception>
        private static CellWriter CellsOf(Array buffer, QueryColumn column, JsonSerializerOptions options)
        {
            return buffer switch
            {
                int[] values => Cells(values, options),
                long[] values => Cells(values, options),
                short[] values => Cells(values, options),
                byte[] values => Cells(values, options),
                decimal[] values => Cells(values, options),
                double[] values => Cells(values, options),
                bool[] values => Cells(values, options),
                string[] values => Cells(values, options),
                Guid[] values => Cells(values, options),
                DateOnly[] values => Cells(values, options),
                DateTime[] values => Cells(values, options),
                DateTimeOffset[] values => Cells(values, options),
                _ => throw new JsonException($"The column '{column.Name}' holds {buffer.GetType().Name} cells, a buffer no row set holds."),
            };
        }

        /// <summary>A cell writer over a typed buffer, through the options' converter for the element type.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="values">The buffer.</param>
        /// <param name="options">The options.</param>
        /// <returns>The cell writer.</returns>
        private static CellWriter Cells<T>(T[] values, JsonSerializerOptions options)
        {
            var converter = (JsonConverter<T>)options.GetConverter(typeof(T));
            return (writer, row) => converter.Write(writer, values[row], options);
        }
    }
}
