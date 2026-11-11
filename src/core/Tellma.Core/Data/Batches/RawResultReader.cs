// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Data.Batches
{
    /// <summary>
    ///     Reads a raw statement's result sets into columnar row sets, typing each column from the
    ///     reader's schema.
    /// </summary>
    internal static class RawResultReader
    {
        /// <summary>Reads the statement's result sets.</summary>
        /// <param name="cursor">The round trip's cursor.</param>
        /// <param name="ordinal">The statement's ordinal.</param>
        /// <param name="count">How many result sets the statement declared.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The result.</returns>
        public static async ValueTask<RawResult> ReadAsync(ResultSetCursor cursor, int ordinal, int count, CancellationToken cancellationToken)
        {
            if (count == 0)
            {
                return RawResult.Empty;
            }

            List<QueryRowSet> sets = new(count);
            for (int index = 0; index < count; index++)
            {
                DbDataReader reader = await cursor.NextAsync(ordinal, cancellationToken).ConfigureAwait(false);
                sets.Add(await ReadSetAsync(reader, cursor, cancellationToken).ConfigureAwait(false));
            }

            return new RawResult(sets);
        }

        /// <summary>Reads the current result set.</summary>
        /// <param name="reader">The reader, positioned on the set.</param>
        /// <param name="cursor">The cursor the rows are counted on.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        /// <returns>The row set.</returns>
        private static async ValueTask<QueryRowSet> ReadSetAsync(DbDataReader reader, ResultSetCursor cursor, CancellationToken cancellationToken)
        {
            // --- The columns, typed from the reader's schema ---
            ReadOnlyCollection<DbColumn> schema = reader.GetColumnSchema();
            List<QueryColumn> columns = new(schema.Count);
            List<Type> fieldTypes = new(schema.Count);
            for (int ordinal = 0; ordinal < schema.Count; ordinal++)
            {
                DbColumn column = schema[ordinal];
                string dataType = column.DataTypeName ?? string.Empty;
                Type fieldType = IsHierarchyId(dataType) ? typeof(string) : reader.GetFieldType(ordinal);
                QueryexType type = TypeOf(column.ColumnName, dataType, fieldType);
                columns.Add(new QueryColumn(
                    column.ColumnName,
                    type,
                    ScaleOf(fieldType, column),
                    fieldType == typeof(decimal) ? column.NumericPrecision : null,
                    column.AllowDBNull ?? true,
                    Path: null,
                    GroupingKey: false));
                fieldTypes.Add(fieldType);
            }

            // --- The rows ---
            QueryRowSet.Builder builder = new(columns, fieldTypes, capacity: 16);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                builder.AppendRow(reader);
            }

            cursor.CountRows(builder.RowCount);
            return builder.Build();
        }

        /// <summary>The query type a result column holds.</summary>
        /// <param name="name">The column name, for the message.</param>
        /// <param name="dataType">The SQL type name.</param>
        /// <param name="fieldType">The reader's field type.</param>
        /// <returns>The query type.</returns>
        /// <exception cref="InvalidOperationException">No query type holds the column.</exception>
        private static QueryexType TypeOf(string name, string dataType, Type fieldType)
        {
            return IsHierarchyId(dataType) ? QueryexType.QxHierarchyId
                : IsInteger(fieldType) || fieldType == typeof(decimal) || fieldType == typeof(double) || fieldType == typeof(float) ? QueryexType.QxNumeric
                : fieldType == typeof(bool) ? QueryexType.QxBool
                : fieldType == typeof(string) ? QueryexType.QxString
                : fieldType == typeof(DateTime) ? (string.Equals(dataType, "date", StringComparison.OrdinalIgnoreCase) ? QueryexType.QxDate : QueryexType.QxDateTime)
                : fieldType == typeof(Guid) ? QueryexType.QxGuid
                : fieldType == typeof(DateTimeOffset) ? QueryexType.QxDateTimeOffset
                : throw new InvalidOperationException(
                    $"The raw result column '{name}' is {dataType}, which no query type holds. Convert it in the statement's text, for example to nvarchar.");
        }

        /// <summary>The scale a numeric column reports: a decimal's own, zero for an integer, none otherwise.</summary>
        /// <param name="fieldType">The reader's field type.</param>
        /// <param name="column">The column's schema.</param>
        /// <returns>The scale, or null.</returns>
        private static int? ScaleOf(Type fieldType, DbColumn column)
        {
            return fieldType == typeof(decimal) ? column.NumericScale
                : IsInteger(fieldType) ? 0
                : null;
        }

        /// <summary>Whether a field type is one of the integer types a result column reads as.</summary>
        /// <param name="fieldType">The reader's field type.</param>
        /// <returns>True for <c>int</c>, <c>bigint</c>, <c>smallint</c> and <c>tinyint</c>.</returns>
        private static bool IsInteger(Type fieldType)
        {
            return fieldType == typeof(int) || fieldType == typeof(long) || fieldType == typeof(short) || fieldType == typeof(byte);
        }

        /// <summary>Whether a SQL type name is <c>hierarchyid</c>, which travels as its path string.</summary>
        /// <param name="dataType">The SQL type name.</param>
        /// <returns>True when it is.</returns>
        private static bool IsHierarchyId(string dataType)
        {
            return dataType.EndsWith("hierarchyid", StringComparison.OrdinalIgnoreCase);
        }
    }
}
