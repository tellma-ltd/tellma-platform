// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using System.Data.Common;
using System.Text.Json;
using Tellma.Core.Abstractions.Api;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Queryex;

namespace Tellma.Core.Abstractions.Tests.Api
{
    /// <summary>
    ///     The rows envelope on the wire: a row set's columns and row-major cells, each cell encoded by
    ///     its buffer's element type, and a page's capped count and ancestors.
    /// </summary>
    public class RowsEnvelopeTests
    {
        [Fact]
        public void A_row_set_writes_its_columns_and_its_rows_cell_by_cell_in_the_scalar_encoding()
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", typeof(int));
            _ = table.Columns.Add("Code", typeof(string));
            _ = table.Columns.Add("Big", typeof(long));
            _ = table.Columns.Add("Total", typeof(decimal));
            _ = table.Columns.Add("Ratio", typeof(double));
            _ = table.Columns.Add("Small", typeof(short));
            _ = table.Columns.Add("Tiny", typeof(byte));
            _ = table.Columns.Add("IsActive", typeof(bool));
            _ = table.Columns.Add("Key", typeof(Guid));
            _ = table.Columns.Add("On", typeof(DateTime));
            _ = table.Columns.Add("At", typeof(DateTime));
            _ = table.Columns.Add("Stamp", typeof(DateTimeOffset));
            _ = table.Columns.Add("Node", typeof(string));
            Guid key = new("0f8fad5b-d9cb-469f-a165-70867728950e");
            _ = table.Rows.Add(
                5, "E-01", 9_007_199_254_740_993L, 1250.5000m, 0.25, (short)3, (byte)4, true, key,
                new DateTime(2026, 11, 8), new DateTime(2026, 11, 8, 9, 30, 0), new DateTimeOffset(2026, 11, 8, 9, 30, 0, TimeSpan.Zero), "/1/3/");
            _ = table.Rows.Add(
                7, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value,
                DBNull.Value, DBNull.Value, DBNull.Value, DBNull.Value);

            QueryRowSet rows = Read(
                table,
                new QueryColumn("Id", QueryexType.QxNumeric, Scale: 0, Precision: null, Nullable: false, Path: ["Id"], GroupingKey: false),
                Column("Code", QueryexType.QxString, path: ["Code"]),
                Column("Big", QueryexType.QxNumeric, scale: 0),
                new QueryColumn("Sum(Amount)", QueryexType.QxNumeric, Scale: 4, Precision: 38, Nullable: true, Path: null, GroupingKey: false),
                Column("Ratio", QueryexType.QxNumeric),
                Column("Small", QueryexType.QxNumeric, scale: 0),
                Column("Tiny", QueryexType.QxNumeric, scale: 0),
                Column("IsActive", QueryexType.QxBool),
                Column("Key", QueryexType.QxGuid),
                Column("On", QueryexType.QxDate),
                Column("At", QueryexType.QxDateTime),
                Column("Stamp", QueryexType.QxDateTimeOffset),
                new QueryColumn("Node", QueryexType.QxHierarchyId, Scale: null, Precision: null, Nullable: true, Path: ["Node"], GroupingKey: true));

            string json = Write(rows);

            // The integer column's scale is 0 and the sum's is its decimal scale; a floating-point
            // column has none, and no column writes its precision.
            Assert.Equal(
                /*lang=json,strict*/
                """
                {"columns":[{"name":"Id","type":"Numeric","scale":0,"nullable":false,"path":["Id"],"groupingKey":false},{"name":"Code","type":"String","nullable":true,"path":["Code"],"groupingKey":false},{"name":"Big","type":"Numeric","scale":0,"nullable":true,"groupingKey":false},{"name":"Sum(Amount)","type":"Numeric","scale":4,"nullable":true,"groupingKey":false},{"name":"Ratio","type":"Numeric","nullable":true,"groupingKey":false},{"name":"Small","type":"Numeric","scale":0,"nullable":true,"groupingKey":false},{"name":"Tiny","type":"Numeric","scale":0,"nullable":true,"groupingKey":false},{"name":"IsActive","type":"Bool","nullable":true,"groupingKey":false},{"name":"Key","type":"Guid","nullable":true,"groupingKey":false},{"name":"On","type":"Date","nullable":true,"groupingKey":false},{"name":"At","type":"DateTime","nullable":true,"groupingKey":false},{"name":"Stamp","type":"DateTimeOffset","nullable":true,"groupingKey":false},{"name":"Node","type":"HierarchyId","nullable":true,"path":["Node"],"groupingKey":true}],"rows":[[5,"E-01","9007199254740993","1250.5000",0.25,3,4,true,"0f8fad5b-d9cb-469f-a165-70867728950e","2026-11-08","2026-11-08T09:30:00.0000000","2026-11-08T09:30:00.0000000+00:00","/1/3/"],[7,null,null,null,null,null,null,null,null,null,null,null,null]]}
                """,
                json);
        }

        [Fact]
        public void A_long_id_column_writes_its_ids_as_strings_and_an_int_id_column_as_numbers()
        {
            Assert.Equal(
                /*lang=json,strict*/ """{"columns":[{"name":"Id","type":"Numeric","scale":0,"nullable":false,"path":["Id"],"groupingKey":false}],"rows":[[17]]}""",
                Write(IdRows(typeof(int), 17)));
            Assert.Equal(
                /*lang=json,strict*/ """{"columns":[{"name":"Id","type":"Numeric","scale":0,"nullable":false,"path":["Id"],"groupingKey":false}],"rows":[["17"]]}""",
                Write(IdRows(typeof(long), 17L)));
        }

        [Fact]
        public void A_row_set_without_rows_still_writes_its_columns()
        {
            DataTable table = new();
            _ = table.Columns.Add("Name", typeof(string));

            Assert.Equal(
                /*lang=json,strict*/ """{"columns":[{"name":"Name","type":"String","nullable":true,"groupingKey":false}],"rows":[]}""",
                Write(Read(table, Column("Name", QueryexType.QxString))));
        }

        [Theory]
        [InlineData(10_001, 10_000, 10_000, true)]
        [InlineData(10_000, 10_000, 10_000, false)]
        [InlineData(42, 10_000, 42, false)]
        [InlineData(0, 10_000, 0, false)]
        public void A_page_reports_its_count_held_to_the_cap_and_whether_more_rows_matched(int count, int cap, int written, bool capped)
        {
            QueryRowSet rows = IdRows(typeof(int), 5);

            using var page = JsonDocument.Parse(Write(new RowPage(rows, count, Ancestors: null, cap)));

            Assert.Equal(written, page.RootElement.GetProperty("count").GetInt32());
            Assert.Equal(capped, page.RootElement.GetProperty("countCapped").GetBoolean());
            Assert.False(page.RootElement.TryGetProperty("ancestors", out _));
        }

        [Fact]
        public void A_page_without_a_count_or_ancestors_is_its_rows_alone()
        {
            QueryRowSet rows = IdRows(typeof(int), 5);

            Assert.Equal("{\"rows\":" + Write(rows) + "}", Write(new RowPage(rows, Count: null, Ancestors: null)));
        }

        [Fact]
        public void A_page_writes_its_ancestors_as_a_row_set_beside_its_rows()
        {
            QueryRowSet rows = IdRows(typeof(int), 7);
            QueryRowSet ancestors = IdRows(typeof(int), 1);

            string json = Write(new RowPage(rows, Count: 1, ancestors, CountCap: 100));

            Assert.Equal("{\"rows\":" + Write(rows) + ",\"count\":1,\"countCapped\":false,\"ancestors\":" + Write(ancestors) + "}", json);
        }

        [Fact]
        public void A_page_that_counted_without_a_cap_reports_its_count_uncapped()
        {
            using var page = JsonDocument.Parse(Write(new RowPage(IdRows(typeof(int), 5), Count: 3, Ancestors: null)));

            Assert.Equal(3, page.RootElement.GetProperty("count").GetInt32());
            Assert.False(page.RootElement.GetProperty("countCapped").GetBoolean());
        }

        [Fact]
        public void The_envelopes_are_written_only()
        {
            _ = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<QueryRowSet>("{}", TellmaJson.Options));
            _ = Assert.Throws<NotSupportedException>(() => JsonSerializer.Deserialize<RowPage>("{}", TellmaJson.Options));
        }

        /// <summary>A row set of one non-null id column of the given field type, one row per id.</summary>
        /// <param name="fieldType">The column's field type: <see cref="int" /> or <see cref="long" />.</param>
        /// <param name="ids">The ids.</param>
        /// <returns>The row set.</returns>
        private static QueryRowSet IdRows(Type fieldType, params object[] ids)
        {
            DataTable table = new();
            _ = table.Columns.Add("Id", fieldType);
            foreach (object id in ids)
            {
                _ = table.Rows.Add(id);
            }

            return Read(table, new QueryColumn("Id", QueryexType.QxNumeric, Scale: 0, Precision: null, Nullable: false, Path: ["Id"], GroupingKey: false));
        }

        /// <summary>A nullable column that is not a grouping key.</summary>
        /// <param name="name">The column's name.</param>
        /// <param name="type">The column's query type.</param>
        /// <param name="scale">The column's scale.</param>
        /// <param name="path">The column's path; null for a computed column.</param>
        /// <returns>The column.</returns>
        private static QueryColumn Column(string name, QueryexType type, int? scale = null, IReadOnlyList<string>? path = null)
        {
            return new QueryColumn(name, type, scale, Precision: null, Nullable: true, path, GroupingKey: false);
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

        /// <summary>Serializes a value with the platform's options.</summary>
        /// <typeparam name="T">The value's type.</typeparam>
        /// <param name="value">The value.</param>
        /// <returns>The JSON text.</returns>
        private static string Write<T>(T value)
        {
            return JsonSerializer.Serialize(value, TellmaJson.Options);
        }
    }
}
