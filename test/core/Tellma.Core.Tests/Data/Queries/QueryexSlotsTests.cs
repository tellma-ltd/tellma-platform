// Copyright (c) Tellma Ltd. All rights reserved.
//
// This source code is licensed under the Apache-2.0 license found in the
// LICENSE file in the root directory of this source tree.

using System.Data;
using Tellma.Core.Abstractions.Data;
using Tellma.Core.Data.Batches;
using Tellma.Core.Data.Queries;
using Tellma.Core.Queryex;

namespace Tellma.Core.Tests.Data.Queries
{
    /// <summary>The binding of a compiled query's slots: by origin, typed by store type, sized so no value is clipped.</summary>
    public sealed class QueryexSlotsTests
    {
        /// <summary>The context values the slots bind.</summary>
        private static readonly QueryexContextValues Context = new(
            new DateOnly(2026, 3, 14), new DateTimeOffset(2026, 3, 14, 9, 30, 0, TimeSpan.FromHours(3)), 7, "Arab Standard Time");

        [Fact]
        public void Each_origin_binds_its_value()
        {
            CompiledQuery compiled = Compiled(
                new QueryexParameterSlot("@qx0_p0", QueryexType.QxNumeric, QueryexStoreType.QxInt, QueryexParameterOrigin.Literal, 5),
                new QueryexParameterSlot("@qx0_p1", QueryexType.QxDate, QueryexStoreType.QxDate, QueryexParameterOrigin.Today),
                new QueryexParameterSlot("@qx0_p2", QueryexType.QxDateTimeOffset, QueryexStoreType.QxDateTimeOffset(7), QueryexParameterOrigin.Now),
                new QueryexParameterSlot("@qx0_p3", QueryexType.QxNumeric, QueryexStoreType.QxInt, QueryexParameterOrigin.UserId),
                new QueryexParameterSlot("@qx0_p4", QueryexType.QxString, QueryexStoreType.QxNVarChar(null), QueryexParameterOrigin.TimeZone),
                new QueryexParameterSlot("@qx0_p5", QueryexType.QxNumeric, QueryexStoreType.QxDecimal(19, 4), QueryexParameterOrigin.Declared, DeclaredName: "Min"),
                new QueryexParameterSlot("@qx0_p6", QueryexType.QxNumeric, QueryexStoreType.QxInt, QueryexParameterOrigin.Declared, DeclaredName: "min"));

            IReadOnlyList<ScalarParameter> parameters = QueryexSlots.Parameters(compiled, Context, new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase) { ["Min"] = 2.5m });

            Assert.Equal([5, new DateTime(2026, 3, 14), Context.Now, 7, "Arab Standard Time", 2.5m, 2.5m], parameters.Select(static parameter => parameter.Value));
            Assert.Equal(
                [SqlDbType.Int, SqlDbType.Date, SqlDbType.DateTimeOffset, SqlDbType.Int, SqlDbType.NVarChar, SqlDbType.Decimal, SqlDbType.Int],
                parameters.Select(static parameter => parameter.Type));
        }

        [Fact]
        public void A_declared_slot_without_a_value_is_a_composer_bug()
        {
            CompiledQuery compiled = Compiled(new QueryexParameterSlot("@qx0_p0", QueryexType.QxNumeric, QueryexStoreType.QxInt, QueryexParameterOrigin.Declared, DeclaredName: "Min"));

            _ = Assert.Throws<InvalidOperationException>(() => QueryexSlots.Parameters(compiled, Context, new Dictionary<string, object?>()));
        }

        [Fact]
        public void A_text_slot_without_a_length_is_bound_at_the_family_bound_unless_the_value_is_longer()
        {
            Assert.Equal(4000, QueryexSlots.Describe("@p", QueryexStoreType.QxNVarChar(null), "x").Size);
            Assert.Equal(-1, QueryexSlots.Describe("@p", QueryexStoreType.QxNVarChar(null), new string('x', 4001)).Size);
            Assert.Equal(8000, QueryexSlots.Describe("@p", QueryexStoreType.QxVarChar(null), null).Size);
            Assert.Equal(-1, QueryexSlots.Describe("@p", QueryexStoreType.QxVarChar(null), new string('x', 8001)).Size);
            Assert.Equal(50, QueryexSlots.Describe("@p", QueryexStoreType.QxNVarChar(50), "x").Size);
            Assert.Equal(SqlDbType.Char, QueryexSlots.Describe("@p", QueryexStoreType.QxChar(3), "abc").Type);
            Assert.Equal(SqlDbType.NChar, QueryexSlots.Describe("@p", QueryexStoreType.QxNChar(3), "abc").Type);
        }

        [Fact]
        public void A_decimal_states_its_facets_only_when_the_slot_does()
        {
            ScalarParameter written = QueryexSlots.Describe("@p", QueryexStoreType.QxDecimal(19, 4), 1.5m);
            Assert.Equal(((byte?)19, (byte?)4), (written.Precision, written.Scale));
            Assert.Equal(19, written.Create().Precision);

            ScalarParameter free = QueryexSlots.Describe("@p", new QueryexStoreType(QueryexStoreFamily.QxDecimal), 1.5m);
            Assert.Equal((null, null), (free.Precision, free.Scale));
        }

        [Fact]
        public void Every_family_binds_its_type_and_the_spatial_and_hierarchy_families_are_refused()
        {
            Assert.Equal(SqlDbType.Bit, QueryexSlots.Describe("@p", QueryexStoreType.QxBit, true).Type);
            Assert.Equal(SqlDbType.TinyInt, QueryexSlots.Describe("@p", QueryexStoreType.QxTinyInt, (byte)1).Type);
            Assert.Equal(SqlDbType.SmallInt, QueryexSlots.Describe("@p", QueryexStoreType.QxSmallInt, (short)1).Type);
            Assert.Equal(SqlDbType.BigInt, QueryexSlots.Describe("@p", QueryexStoreType.QxBigInt, 1L).Type);
            Assert.Equal(SqlDbType.UniqueIdentifier, QueryexSlots.Describe("@p", QueryexStoreType.QxUniqueIdentifier, Guid.Empty).Type);
            Assert.Equal(SqlDbType.DateTime, QueryexSlots.Describe("@p", QueryexStoreType.QxDateTime, DateTime.MinValue).Type);
            Assert.Equal(SqlDbType.DateTime2, QueryexSlots.Describe("@p", QueryexStoreType.QxDateTime2(7), DateTime.MinValue).Type);
            Assert.Null(QueryexSlots.Describe("@p", QueryexStoreType.QxDate, null).Value);
            _ = Assert.Throws<InvalidOperationException>(() => QueryexSlots.Describe("@p", QueryexStoreType.QxHierarchyId, "/1/"));
            _ = Assert.Throws<InvalidOperationException>(() => QueryexSlots.Describe("@p", QueryexStoreType.QxGeography, null));
        }

        /// <summary>A compiled query with only the given slots.</summary>
        /// <param name="slots">The slots.</param>
        /// <returns>The compiled query.</returns>
        private static CompiledQuery Compiled(params QueryexParameterSlot[] slots)
        {
            return new CompiledQuery { Sql = "SELECT 1;\n", Parameters = slots, Columns = [] };
        }
    }
}
