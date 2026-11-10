// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Collections.ObjectModel;
using System.Data.Common;
using System.Globalization;
using Tellma.Core.Abstractions.Internal;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Data
{
    /// <summary>
    ///     The rows of a query result in columnar form: one typed buffer per column and a null mask, with
    ///     no per-row objects. What a rows query returns and the wire serialises column by column.
    /// </summary>
    /// <remarks>
    ///     Each column's buffer type follows from its query type and the result set's field type:
    ///     <c>int[]</c>, <c>long[]</c>, <c>short[]</c>, <c>byte[]</c>, <c>decimal[]</c> or <c>double[]</c>
    ///     for a numeric column (a <c>real</c> widens to <c>double</c>), <c>bool[]</c>, <c>string?[]</c>
    ///     (also for a hierarchy path), <c>Guid[]</c>, <c>DateOnly[]</c>, <c>DateTime[]</c> or
    ///     <c>DateTimeOffset[]</c>. A buffer holds exactly <see cref="RowCount" /> values; a null cell holds
    ///     the type's default and is reported by <see cref="IsNull" />.
    /// </remarks>
    public sealed class QueryRowSet
    {
        private readonly Array[] _buffers;
        private readonly bool[][] _nulls;

        /// <summary>Creates a row set over filled buffers.</summary>
        /// <param name="columns">The columns.</param>
        /// <param name="buffers">One buffer per column, each exactly <paramref name="rowCount" /> long.</param>
        /// <param name="nulls">One null mask per column, each exactly <paramref name="rowCount" /> long.</param>
        /// <param name="rowCount">The number of rows.</param>
        private QueryRowSet(IReadOnlyList<QueryColumn> columns, Array[] buffers, bool[][] nulls, int rowCount)
        {
            Columns = columns;
            _buffers = buffers;
            _nulls = nulls;
            RowCount = rowCount;
        }

        /// <summary>The columns, in result-set order.</summary>
        public IReadOnlyList<QueryColumn> Columns { get; }

        /// <summary>The number of rows.</summary>
        public int RowCount { get; }

        /// <summary>The buffer of one column, holding one value per row.</summary>
        /// <param name="column">The column's position.</param>
        /// <returns>The column's typed array.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="column" /> is not a column's position.</exception>
        public Array GetBuffer(int column)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, _buffers.Length);
            return _buffers[column];
        }

        /// <summary>Whether one cell is null.</summary>
        /// <param name="column">The column's position.</param>
        /// <param name="row">The row's position.</param>
        /// <returns>True when the cell is null.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="column" /> or <paramref name="row" /> is out of range.</exception>
        public bool IsNull(int column, int row)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(column);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, _nulls.Length);
            ArgumentOutOfRangeException.ThrowIfNegative(row);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, RowCount);
            return _nulls[column][row];
        }

        /// <summary>
        ///     Fills a <see cref="QueryRowSet" /> from an open data reader, one row per
        ///     <see cref="AppendRow" />. Created from the compiled query's columns and the reader's field
        ///     types before the first read: a result set's column metadata arrives before its rows, so the
        ///     buffers never depend on the data.
        /// </summary>
        /// <remarks>Not thread-safe; one reader fills one builder.</remarks>
        public sealed class Builder
        {
            private readonly ReadOnlyCollection<QueryColumn> _columns;
            private readonly ColumnBuffer[] _buffers;
            private bool _built;

            /// <summary>Chooses each column's buffer from its query type and the reader's field type.</summary>
            /// <param name="columns">The columns, in result-set order.</param>
            /// <param name="fieldTypes">The reader's field type of each column (<see cref="DbDataReader.GetFieldType" />), in the same order.</param>
            /// <param name="capacity">The number of rows to size the buffers for; they grow past it.</param>
            /// <exception cref="ArgumentNullException">A list is null or holds a null entry.</exception>
            /// <exception cref="ArgumentException">The lists differ in length.</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is negative.</exception>
            /// <exception cref="InvalidOperationException">A column pairs its query type with a field type no buffer holds.</exception>
            public Builder(IReadOnlyList<QueryColumn> columns, IReadOnlyList<Type> fieldTypes, int capacity)
            {
                ArgumentNullException.ThrowIfNull(columns);
                ArgumentNullException.ThrowIfNull(fieldTypes);
                ArgumentOutOfRangeException.ThrowIfNegative(capacity);
                if (columns.Count != fieldTypes.Count)
                {
                    throw new ArgumentException($"The result set has {fieldTypes.Count} field types for {columns.Count} columns.", nameof(fieldTypes));
                }

                _columns = ReadOnlyCopies.OfEntries(columns, requireOne: false, nameof(columns));
                _buffers = new ColumnBuffer[_columns.Count];
                for (int i = 0; i < _columns.Count; i++)
                {
                    Type fieldType = fieldTypes[i] ?? throw new ArgumentNullException(nameof(fieldTypes), "The list must not hold a null entry.");
                    _buffers[i] = ColumnBuffer.Create(_columns[i], fieldType, capacity);
                }
            }

            /// <summary>The number of rows appended so far.</summary>
            public int RowCount { get; private set; }

            /// <summary>Appends the reader's current row: column <c>i</c> from the reader's field <c>i</c>.</summary>
            /// <param name="reader">The reader, positioned on a row.</param>
            /// <exception cref="ArgumentNullException"><paramref name="reader" /> is null.</exception>
            /// <exception cref="InvalidOperationException"><see cref="Build" /> was already called.</exception>
            public void AppendRow(DbDataReader reader)
            {
                ArgumentNullException.ThrowIfNull(reader);
                ObjectDisposedException.ThrowIf(_built, this);

                for (int i = 0; i < _buffers.Length; i++)
                {
                    _buffers[i].Read(reader, i, RowCount);
                }

                RowCount++;
            }

            /// <summary>The row set of every appended row. The builder accepts no more rows afterwards.</summary>
            /// <returns>The row set.</returns>
            /// <exception cref="InvalidOperationException"><see cref="Build" /> was already called.</exception>
            public QueryRowSet Build()
            {
                ObjectDisposedException.ThrowIf(_built, this);
                _built = true;

                var buffers = new Array[_buffers.Length];
                bool[][] nulls = new bool[_buffers.Length][];
                for (int i = 0; i < _buffers.Length; i++)
                {
                    (buffers[i], nulls[i]) = _buffers[i].Trim(RowCount);
                }

                return new QueryRowSet(_columns, buffers, nulls, RowCount);
            }
        }

        /// <summary>One column's growing buffer and null mask, and how it reads a cell.</summary>
        private abstract class ColumnBuffer
        {
            /// <summary>Reads one cell into the buffer.</summary>
            /// <param name="reader">The reader, positioned on a row.</param>
            /// <param name="ordinal">The field's ordinal.</param>
            /// <param name="row">The row's position in the buffer.</param>
            public abstract void Read(DbDataReader reader, int ordinal, int row);

            /// <summary>The buffer and null mask cut to the row count.</summary>
            /// <param name="rowCount">The row count.</param>
            /// <returns>The buffer and the mask.</returns>
            public abstract (Array Buffer, bool[] Nulls) Trim(int rowCount);

            /// <summary>The buffer of a column, chosen by the pair (query type, field type).</summary>
            /// <param name="column">The column.</param>
            /// <param name="fieldType">The reader's field type.</param>
            /// <param name="capacity">The initial capacity.</param>
            /// <returns>The buffer.</returns>
            /// <exception cref="InvalidOperationException">No buffer holds the pair.</exception>
            public static ColumnBuffer Create(QueryColumn column, Type fieldType, int capacity)
            {
                ColumnBuffer? buffer = column.Type switch
                {
                    // --- Numbers keep their physical width; a real widens to double ---
                    QueryexType.QxNumeric when fieldType == typeof(int) => new ColumnBuffer<int>(capacity, static (r, i) => r.GetInt32(i)),
                    QueryexType.QxNumeric when fieldType == typeof(long) => new ColumnBuffer<long>(capacity, static (r, i) => r.GetInt64(i)),
                    QueryexType.QxNumeric when fieldType == typeof(short) => new ColumnBuffer<short>(capacity, static (r, i) => r.GetInt16(i)),
                    QueryexType.QxNumeric when fieldType == typeof(byte) => new ColumnBuffer<byte>(capacity, static (r, i) => r.GetByte(i)),
                    QueryexType.QxNumeric when fieldType == typeof(decimal) => new ColumnBuffer<decimal>(capacity, static (r, i) => r.GetDecimal(i)),
                    QueryexType.QxNumeric when fieldType == typeof(double) => new ColumnBuffer<double>(capacity, static (r, i) => r.GetDouble(i)),
                    QueryexType.QxNumeric when fieldType == typeof(float) => new ColumnBuffer<double>(capacity, static (r, i) => r.GetFloat(i)),

                    // --- A predicate may arrive as a bit or as an integer; non-zero is true ---
                    QueryexType.QxBool when fieldType == typeof(bool) => new ColumnBuffer<bool>(capacity, static (r, i) => r.GetBoolean(i)),
                    QueryexType.QxBool when fieldType == typeof(int) => new ColumnBuffer<bool>(capacity, static (r, i) => r.GetInt32(i) != 0),
                    QueryexType.QxBool when fieldType == typeof(long) => new ColumnBuffer<bool>(capacity, static (r, i) => r.GetInt64(i) != 0),
                    QueryexType.QxBool when fieldType == typeof(short) => new ColumnBuffer<bool>(capacity, static (r, i) => r.GetInt16(i) != 0),
                    QueryexType.QxBool when fieldType == typeof(byte) => new ColumnBuffer<bool>(capacity, static (r, i) => r.GetByte(i) != 0),

                    // --- Text, identifiers and the temporal types ---
                    QueryexType.QxString when fieldType == typeof(string) => new ColumnBuffer<string?>(capacity, static (r, i) => r.GetString(i)),
                    QueryexType.QxGuid when fieldType == typeof(Guid) => new ColumnBuffer<Guid>(capacity, static (r, i) => r.GetGuid(i)),
                    QueryexType.QxDate when fieldType == typeof(DateTime) => new ColumnBuffer<DateOnly>(capacity, static (r, i) => DateOnly.FromDateTime(r.GetDateTime(i))),
                    QueryexType.QxDate when fieldType == typeof(DateOnly) => new ColumnBuffer<DateOnly>(capacity, static (r, i) => r.GetFieldValue<DateOnly>(i)),
                    QueryexType.QxDateTime when fieldType == typeof(DateTime) => new ColumnBuffer<DateTime>(capacity, static (r, i) => r.GetDateTime(i)),
                    QueryexType.QxDateTimeOffset when fieldType == typeof(DateTimeOffset) => new ColumnBuffer<DateTimeOffset>(capacity, static (r, i) => r.GetFieldValue<DateTimeOffset>(i)),

                    // --- A hierarchy node travels as its path, whatever type the provider reads it as ---
                    QueryexType.QxHierarchyId => new ColumnBuffer<string?>(capacity, static (r, i) => Convert.ToString(r.GetValue(i), CultureInfo.InvariantCulture)),

                    QueryexType.QxNumeric or QueryexType.QxBool or QueryexType.QxString or QueryexType.QxGuid or QueryexType.QxDate
                        or QueryexType.QxDateTime or QueryexType.QxDateTimeOffset or QueryexType.QxGeography => null,
                    _ => null,
                };

                return buffer ?? throw new InvalidOperationException(
                    $"The column '{column.Name}' is {column.Type} in the query but {fieldType.Name} in the result set, a pair no buffer holds.");
            }
        }

        /// <summary>A column buffer of one element type.</summary>
        /// <typeparam name="T">The element type.</typeparam>
        /// <param name="capacity">The initial capacity.</param>
        /// <param name="read">Reads a non-null cell.</param>
        private sealed class ColumnBuffer<T>(int capacity, Func<DbDataReader, int, T> read) : ColumnBuffer
        {
            private T[] _values = new T[capacity];
            private bool[] _nulls = new bool[capacity];

            /// <inheritdoc />
            public override void Read(DbDataReader reader, int ordinal, int row)
            {
                // Double the arrays when a row would not fit, as a list grows.
                if (row >= _values.Length)
                {
                    int size = Math.Max(4, _values.Length * 2);
                    Array.Resize(ref _values, size);
                    Array.Resize(ref _nulls, size);
                }

                if (reader.IsDBNull(ordinal))
                {
                    _values[row] = default!;
                    _nulls[row] = true;
                }
                else
                {
                    _values[row] = read(reader, ordinal);
                    _nulls[row] = false;
                }
            }

            /// <inheritdoc />
            public override (Array Buffer, bool[] Nulls) Trim(int rowCount)
            {
                if (_values.Length != rowCount)
                {
                    Array.Resize(ref _values, rowCount);
                    Array.Resize(ref _nulls, rowCount);
                }

                return (_values, _nulls);
            }
        }
    }
}
