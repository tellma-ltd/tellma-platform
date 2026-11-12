// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Queries
{
    // The reader contract is spec 0012 §11.3's: the buffers and the columns depend on the compiled
    // columns and the result set's own metadata, never on the data.
    /// <summary>Reads a compiled query's result set into a columnar row set.</summary>
    internal static class QueryexResultReader
    {
        /// <summary>The rows a builder holds before it first grows.</summary>
        private const int InitialCapacity = 16;

        /// <summary>
        ///     Reads the current result set. The builder is created from the compiled columns and the
        ///     open reader's metadata before the first row is read; a numeric column's scale and a
        ///     decimal's precision come from the result set's column schema, so a computed decimal
        ///     reports what SQL Server computed.
        /// </summary>
        /// <param name="reader">The reader, positioned on the set.</param>
        /// <param name="columns">The compiled columns.</param>
        /// <param name="cursor">The cursor the rows are counted on.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row set.</returns>
        /// <exception cref="InvalidOperationException">
        ///     The result set has another number of columns than the compiled query, or a column's
        ///     query type and field type are a pair no buffer holds.
        /// </exception>
        public static async ValueTask<QueryRowSet> ReadAsync(
            DbDataReader reader, IReadOnlyList<QueryexColumn> columns, ResultSetCursor cursor, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(reader);
            ArgumentNullException.ThrowIfNull(columns);
            ArgumentNullException.ThrowIfNull(cursor);

            // --- The columns: the compiled type, path and grouping, with the result set's facets ---
            ReadOnlyCollection<DbColumn> schema = reader.GetColumnSchema();
            if (schema.Count != columns.Count)
            {
                throw new InvalidOperationException(
                    $"The query's result set has {schema.Count} columns but its compilation describes {columns.Count}.");
            }

            List<QueryColumn> described = new(columns.Count);
            List<Type> fieldTypes = new(columns.Count);
            for (int ordinal = 0; ordinal < columns.Count; ordinal++)
            {
                QueryexColumn column = columns[ordinal];
                DbColumn facets = schema[ordinal];

                // A hierarchy position travels as its path string; asking the provider for the UDT's
                // field type would load the type for nothing.
                Type fieldType = column.Type == QueryexType.QxHierarchyId || RawResultReader.IsHierarchyId(facets.DataTypeName ?? string.Empty)
                    ? typeof(string)
                    : reader.GetFieldType(ordinal);
                bool numeric = column.Type == QueryexType.QxNumeric;
                described.Add(new QueryColumn(
                    column.Text,
                    column.Type,
                    numeric ? RawResultReader.ScaleOf(fieldType, facets) : null,
                    numeric && fieldType == typeof(decimal) ? facets.NumericPrecision : null,
                    column.Nullity != QueryexNullity.NotNull,
                    column.Path,
                    column.IsGroupingKey));
                fieldTypes.Add(fieldType);
            }

            // --- The rows ---
            QueryRowSet.Builder builder = new(described, fieldTypes, InitialCapacity);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                builder.AppendRow(reader);
            }

            cursor.CountRows(builder.RowCount);
            return builder.Build();
        }
    }
}
