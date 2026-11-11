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
