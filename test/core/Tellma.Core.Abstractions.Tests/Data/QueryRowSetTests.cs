// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Tests.Data
{
    /// <summary>The columnar row set: buffers chosen by query type and field type, null masks, growth past the capacity.</summary>
    public class QueryRowSetTests
    {
        [Fact]
        public void Each_column_gets_the_buffer_of_its_query_and_field_type_and_nulls_are_masked()
        {
            DataTable table = new();
            _ = table.Columns.Add("Count", typeof(int));
            _ = table.Columns.Add("Big", typeof(long));
            _ = table.Columns.Add("Amount", typeof(decimal));
            _ = table.Columns.Add("Ratio", typeof(float));
            _ = table.Columns.Add("Flag", typeof(int));
            _ = table.Columns.Add("Name", typeof(string));
            _ = table.Columns.Add("Key", typeof(Guid));
            _ = table.Columns.Add("On", typeof(DateTime));
            _ = table.Columns.Add("At", typeof(DateTimeOffset));
            _ = table.Columns.Add("Node", typeof(string));
            var key = Guid.NewGuid();
            DateTimeOffset at = new(2026, 11, 8, 9, 30, 0, TimeSpan.Zero);
            _ = table.Rows.Add(7, 8L, 1.25m, 0.5f, 1, "Cash", key, new DateTime(2026, 11, 8), at, "/1/2/");
            _ = table.Rows.Add(DBNull.Value, 9L, 2.5m, 1.5f, 0, DBNull.Value, key, new DateTime(2026, 11, 9), at, DBNull.Value);

            QueryRowSet rows = Read(
                table,
                Column("Count", QueryexType.QxNumeric),
                Column("Big", QueryexType.QxNumeric),
                Column("Amount", QueryexType.QxNumeric),
                Column("Ratio", QueryexType.QxNumeric),
                Column("Flag", QueryexType.QxBool),
                Column("Name", QueryexType.QxString),
                Column("Key", QueryexType.QxGuid),
                Column("On", QueryexType.QxDate),
                Column("At", QueryexType.QxDateTimeOffset),
                Column("Node", QueryexType.QxHierarchyId));

            Assert.Equal(2, rows.RowCount);
            Assert.Equal([7, 0], Assert.IsType<int[]>(rows.GetBuffer(0)));
            Assert.Equal([8L, 9L], Assert.IsType<long[]>(rows.GetBuffer(1)));
            Assert.Equal([1.25m, 2.5m], Assert.IsType<decimal[]>(rows.GetBuffer(2)));
            Assert.Equal([0.5, 1.5], Assert.IsType<double[]>(rows.GetBuffer(3)));
            Assert.Equal([true, false], Assert.IsType<bool[]>(rows.GetBuffer(4)));
            Assert.Equal<IEnumerable<string?>>(["Cash", null], Assert.IsType<string[]>(rows.GetBuffer(5)));
            Assert.Equal([key, key], Assert.IsType<Guid[]>(rows.GetBuffer(6)));
            Assert.Equal([new DateOnly(2026, 11, 8), new DateOnly(2026, 11, 9)], Assert.IsType<DateOnly[]>(rows.GetBuffer(7)));
            Assert.Equal([at, at], Assert.IsType<DateTimeOffset[]>(rows.GetBuffer(8)));
            Assert.Equal<IEnumerable<string?>>(["/1/2/", null], Assert.IsType<string[]>(rows.GetBuffer(9)));

            Assert.Equal((false, true), (rows.IsNull(0, 0), rows.IsNull(0, 1)));
            Assert.Equal((false, true), (rows.IsNull(5, 0), rows.IsNull(5, 1)));
            Assert.False(rows.IsNull(1, 1));
        }

        /// <summary>Every pair a buffer holds: the query type, the provider's field type, a value read, and the buffer and element it lands as.</summary>
        public static TheoryData<QueryexType, Type, object, Type, object> HeldPairs { get; } = new()
        {
            { QueryexType.QxNumeric, typeof(int), 7, typeof(int[]), 7 },
            { QueryexType.QxNumeric, typeof(long), 8L, typeof(long[]), 8L },
            { QueryexType.QxNumeric, typeof(short), (short)9, typeof(short[]), (short)9 },
            { QueryexType.QxNumeric, typeof(byte), (byte)10, typeof(byte[]), (byte)10 },
            { QueryexType.QxNumeric, typeof(decimal), 1.25m, typeof(decimal[]), 1.25m },
            { QueryexType.QxNumeric, typeof(double), 2.5d, typeof(double[]), 2.5d },
            { QueryexType.QxNumeric, typeof(float), 0.5f, typeof(double[]), 0.5d },
            { QueryexType.QxBool, typeof(bool), true, typeof(bool[]), true },
            { QueryexType.QxBool, typeof(int), 2, typeof(bool[]), true },
            { QueryexType.QxBool, typeof(long), 0L, typeof(bool[]), false },
            { QueryexType.QxBool, typeof(short), (short)1, typeof(bool[]), true },
            { QueryexType.QxBool, typeof(byte), (byte)0, typeof(bool[]), false },
            { QueryexType.QxString, typeof(string), "Cash", typeof(string[]), "Cash" },
            { QueryexType.QxGuid, typeof(Guid), new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff"), typeof(Guid[]), new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff") },
            { QueryexType.QxDate, typeof(DateTime), new DateTime(2026, 11, 8), typeof(DateOnly[]), new DateOnly(2026, 11, 8) },
            { QueryexType.QxDateTime, typeof(DateTime), new DateTime(2026, 11, 8, 9, 30, 0), typeof(DateTime[]), new DateTime(2026, 11, 8, 9, 30, 0) },
            { QueryexType.QxDateTimeOffset, typeof(DateTimeOffset), new DateTimeOffset(2026, 11, 8, 9, 30, 0, TimeSpan.FromHours(3)), typeof(DateTimeOffset[]), new DateTimeOffset(2026, 11, 8, 9, 30, 0, TimeSpan.FromHours(3)) },
            { QueryexType.QxHierarchyId, typeof(string), "/1/2/", typeof(string[]), "/1/2/" },
        };

        /// <summary>Pairs no buffer holds: a query type with a field type the backend never sends for it.</summary>
        public static TheoryData<QueryexType, Type> RefusedPairs { get; } = new()
        {
            { QueryexType.QxNumeric, typeof(string) },
            { QueryexType.QxNumeric, typeof(bool) },
            { QueryexType.QxBool, typeof(string) },
            { QueryexType.QxBool, typeof(decimal) },
            { QueryexType.QxString, typeof(int) },
            { QueryexType.QxString, typeof(Guid) },
            { QueryexType.QxGuid, typeof(string) },
            { QueryexType.QxDate, typeof(DateTimeOffset) },
            { QueryexType.QxDate, typeof(string) },
            { QueryexType.QxDateTime, typeof(DateTimeOffset) },
            { QueryexType.QxDateTime, typeof(DateOnly) },
            { QueryexType.QxDateTimeOffset, typeof(DateTime) },
            { QueryexType.QxGeography, typeof(byte[]) },
        };

        [Theory]
        [MemberData(nameof(HeldPairs))]
        public void Every_held_pair_reads_into_its_buffer(QueryexType type, Type fieldType, object value, Type bufferType, object expected)
        {
            DataTable table = new();
            _ = table.Columns.Add("Value", fieldType);
            _ = table.Rows.Add(value);
            _ = table.Rows.Add(DBNull.Value);

            QueryRowSet rows = Read(table, Column("Value", type));

            Array buffer = rows.GetBuffer(0);
            Assert.IsType(bufferType, buffer);
            Assert.Equal(expected, buffer.GetValue(0));
            Assert.Equal((false, true), (rows.IsNull(0, 0), rows.IsNull(0, 1)));
        }

        [Theory]
        [MemberData(nameof(RefusedPairs))]
        public void Every_other_pair_is_refused_naming_the_column(QueryexType type, Type fieldType)
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new QueryRowSet.Builder([Column("Value", type)], [fieldType], 0));

            Assert.Contains("'Value'", exception.Message, StringComparison.Ordinal);
            Assert.Contains(fieldType.Name, exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_pair_no_buffer_holds_is_refused_naming_the_column()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => new QueryRowSet.Builder([Column("Name", QueryexType.QxString)], [typeof(int)], 0));

            Assert.Equal("The column 'Name' is QxString in the query but Int32 in the result set, a pair no buffer holds.", exception.Message);
            _ = Assert.Throws<InvalidOperationException>(() => new QueryRowSet.Builder([Column("Shape", QueryexType.QxGeography)], [typeof(byte[])], 0));
        }

        [Fact]
        public void The_buffers_grow_past_the_capacity_and_hold_exactly_the_rows()
        {
            DataTable table = new();
            _ = table.Columns.Add("Count", typeof(int));
            for (int i = 0; i < 10; i++)
            {
                _ = table.Rows.Add(i);
            }

            QueryRowSet rows = Read(table, Column("Count", QueryexType.QxNumeric));

            Assert.Equal(Enumerable.Range(0, 10), Assert.IsType<int[]>(rows.GetBuffer(0)));
        }

        [Fact]
        public void An_empty_result_has_empty_buffers_and_a_built_builder_takes_no_more_rows()
        {
            QueryRowSet.Builder builder = new([Column("Count", QueryexType.QxNumeric)], [typeof(int)], 16);
            QueryRowSet rows = builder.Build();

            Assert.Equal(0, rows.RowCount);
            Assert.Empty(rows.GetBuffer(0));
            _ = Assert.Throws<ObjectDisposedException>(builder.Build);
            _ = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => rows.IsNull(0, 0));
            _ = Assert.ThrowsAny<ArgumentOutOfRangeException>(() => rows.GetBuffer(1));
        }

        [Fact]
        public void Mismatched_column_and_field_type_counts_are_refused()
        {
            _ = Assert.Throws<ArgumentException>(() => new QueryRowSet.Builder([Column("A", QueryexType.QxNumeric)], [], 0));
        }

        /// <summary>A non-null column of a query type, with no path.</summary>
        /// <param name="name">The column's name.</param>
        /// <param name="type">Its query type.</param>
        /// <returns>The column.</returns>
        private static QueryColumn Column(string name, QueryexType type)
        {
            return new QueryColumn(name, type, null, null, Nullable: true, Path: null, GroupingKey: false);
        }

        /// <summary>Reads every row of a table into a row set, as the batch's reader does.</summary>
        /// <param name="table">The table.</param>
        /// <param name="columns">The query's columns, one per table column.</param>
        /// <returns>The row set.</returns>
        private static QueryRowSet Read(DataTable table, params QueryColumn[] columns)
        {
            using DbDataReader reader = table.CreateDataReader();
            Type[] fieldTypes = [.. Enumerable.Range(0, reader.FieldCount).Select(reader.GetFieldType)];
            QueryRowSet.Builder builder = new(columns, fieldTypes, capacity: 1);
            while (reader.Read())
            {
                builder.AppendRow(reader);
            }

            return builder.Build();
        }
    }
}
